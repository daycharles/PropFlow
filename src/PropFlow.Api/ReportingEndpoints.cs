using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Reporting;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

// FS-S18: bounded, tenant-scoped report projections. Reports deliberately return reconciled
// source totals rather than duplicated denormalized counters; the same projection powers JSON
// and CSV export so export fidelity is structural, not a second implementation.
public static class ReportingEndpoints
{
    public static void MapReportingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/reports").RequireAuthorization(Capabilities.ReadReports);
        group.MapGet("/{kind}", GetReport);
        group.MapGet("/{kind}/export", ExportReport);
        group.MapGet("/schedules", ListSchedules);
        group.MapPost("/schedules", CreateSchedule).RequireAuthorization(Capabilities.ManageConfiguration);
        group.MapPost("/schedules/{id:guid}/pause", PauseSchedule).RequireAuthorization(Capabilities.ManageConfiguration);
        group.MapPost("/schedules/{id:guid}/run", RunSchedule).RequireAuthorization(Capabilities.ManageConfiguration);
        group.MapGet("/schedules/{id:guid}/deliveries", ListDeliveries);
    }

    private static async Task<IResult> ListSchedules(OperationsStore store, CancellationToken ct) => Results.Ok(await store.ReportSchedules.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Kind, x.Frequency, x.NextRunAt, x.Recipient, x.Format, x.IsActive, x.LastRunAt, x.DeliveryCount }).ToListAsync(ct));

    private static async Task<IResult> CreateSchedule(CreateReportScheduleRequest request, OperationsStore store, TimeProvider clock, CancellationToken ct)
    {
        try
        {
            var schedule = new ReportSchedule(store.OrganizationId, Guid.NewGuid(), request.Name, request.Kind, request.Frequency, request.NextRunAt ?? clock.GetUtcNow(), request.Recipient, request.Format, request.Filters is null ? null : JsonSerializer.Serialize(request.Filters));
            store.ReportSchedules.Add(schedule); await store.SaveChangesAsync(ct);
            return Results.Created($"/api/reports/schedules/{schedule.Id}", schedule);
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static async Task<IResult> PauseSchedule(Guid id, OperationsStore store, CancellationToken ct)
    {
        var schedule = await store.ReportSchedules.SingleOrDefaultAsync(x => x.Id == id, ct); if (schedule is null) return Results.NotFound(); schedule.Pause(); await store.SaveChangesAsync(ct); return Results.Ok(new { schedule.Id, schedule.IsActive });
    }

    private static async Task<IResult> RunSchedule(Guid id, OperationsStore store, TimeProvider clock, CancellationToken ct)
    {
        var schedule = await store.ReportSchedules.SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct); if (schedule is null) return Results.NotFound();
        var now = clock.GetUtcNow();
        var dayStart = new DateTimeOffset(now.Date, TimeSpan.Zero); var dayEnd = dayStart.AddDays(1);
        var existing = await store.ReportDeliveries.AsNoTracking().Where(x => x.ScheduleId == schedule.Id && x.DeliveredAt >= dayStart && x.DeliveredAt < dayEnd).SingleOrDefaultAsync(ct);
        if (existing is not null) return Results.Ok(new { existing.Id, existing.DeliveredAt, existing.PayloadHash, schedule.NextRunAt, idempotent = true });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{schedule.Kind}|{schedule.FilterJson}|{now:yyyy-MM-dd}")));
        var delivery = new ReportDelivery(store.OrganizationId, Guid.NewGuid(), schedule.Id, now, hash, await DeliveryRowCount(schedule.Kind, store, ct), schedule.Recipient, schedule.Format); store.ReportDeliveries.Add(delivery); schedule.MarkDelivered(now); await store.SaveChangesAsync(ct);
        return Results.Ok(new { delivery.Id, delivery.DeliveredAt, delivery.PayloadHash, schedule.NextRunAt });
    }

    private static async Task<IResult> ListDeliveries(Guid id, OperationsStore store, CancellationToken ct) => Results.Ok(await store.ReportDeliveries.AsNoTracking().Where(x => x.ScheduleId == id).OrderByDescending(x => x.DeliveredAt).Select(x => new { x.Id, x.DeliveredAt, x.PayloadHash, x.RowCount, x.Recipient, x.Format }).ToListAsync(ct));

    private static async Task<int> DeliveryRowCount(string kind, OperationsStore store, CancellationToken ct) => kind.ToLowerInvariant() switch
    {
        "operational" or "maintenance" => await store.WorkItems.CountAsync(ct),
        "leasing" => await store.Leases.CountAsync(ct),
        "financial" => await store.LeaseCharges.CountAsync(ct) + await store.ResidentPayments.CountAsync(ct),
        "occupancy" => await store.Occupancies.CountAsync(ct),
        "vendor" => await store.Vendors.CountAsync(ct),
        "portfolio" => await store.Properties.CountAsync(ct),
        _ => 0
    };

    private static async Task<IResult> GetReport(string kind, DateOnly? from, DateOnly? to, Guid? propertyId, int? take, OperationsStore store, CancellationToken ct)
    {
        if (!TryRange(from, to, out var start, out var end, out var error)) return Results.BadRequest(error);
        var limit = Math.Clamp(take ?? 1000, 1, 10000);
        var normalized = kind.Trim().ToLowerInvariant();
        var work = store.WorkItems.AsNoTracking();
        if (from is not null || to is not null) work = work.Where(x => x.CreatedAt >= start && x.CreatedAt < end);
        if (propertyId is not null) work = work.Where(x => x.PropertyId == propertyId);
        var leases = store.Leases.AsNoTracking().Where(x => x.StartsOn <= DateOnly.FromDateTime(end.UtcDateTime) && x.EndsOn >= DateOnly.FromDateTime(start.UtcDateTime));
        if (propertyId is not null) leases = leases.Where(x => store.Spaces.Any(s => s.Id == x.SpaceId && s.PropertyId == propertyId));

        object result = normalized switch
        {
            "operational" or "maintenance" => new { kind = normalized, filters = new { from = start, to = end, propertyId }, totals = await work.GroupBy(_ => 1).Select(g => new { count = g.Count(), estimatedCost = g.Sum(x => x.Cost ?? 0m) }).FirstOrDefaultAsync(ct) ?? new { count = 0, estimatedCost = 0m }, rows = await work.OrderByDescending(x => x.CreatedAt).Take(limit).Select(x => new { x.Id, x.Title, x.Status, x.Cost, x.CreatedAt, x.PropertyId, x.VendorId }).ToListAsync(ct) },
            "leasing" => new { kind = normalized, filters = new { from = start, to = end, propertyId }, totals = await leases.GroupBy(_ => 1).Select(g => new { count = g.Count(), monthlyRent = g.Sum(x => x.MonthlyRent) }).FirstOrDefaultAsync(ct) ?? new { count = 0, monthlyRent = 0m }, rows = await leases.OrderBy(x => x.StartsOn).Take(limit).Select(x => new { x.Id, x.SpaceId, x.ResidentId, x.Status, x.StartsOn, x.EndsOn, x.MonthlyRent }).ToListAsync(ct) },
            "financial" => new { kind = normalized, filters = new { from = start, to = end, propertyId }, totals = new { charges = await store.LeaseCharges.AsNoTracking().Where(x => x.DueOn >= DateOnly.FromDateTime(start.UtcDateTime) && x.DueOn <= DateOnly.FromDateTime(end.UtcDateTime) && (propertyId == null || store.Leases.Any(l => l.Id == x.LeaseId && store.Spaces.Any(s => s.Id == l.SpaceId && s.PropertyId == propertyId)))).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m, payments = await store.ResidentPayments.AsNoTracking().Where(x => x.SettledAt >= start && x.SettledAt < end && (propertyId == null || store.Leases.Any(l => l.Id == x.LeaseId && store.Spaces.Any(s => s.Id == l.SpaceId && s.PropertyId == propertyId)))).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m }, rows = Array.Empty<object>() },
            "occupancy" => new { kind = normalized, filters = new { from = start, to = end, propertyId }, totals = new { occupied = await store.Occupancies.AsNoTracking().CountAsync(x => x.MovedInOn <= DateOnly.FromDateTime(end.UtcDateTime) && (x.MovedOutOn == null || x.MovedOutOn >= DateOnly.FromDateTime(start.UtcDateTime)), ct), spaces = await store.Spaces.AsNoTracking().CountAsync(x => propertyId == null || x.PropertyId == propertyId, ct) }, rows = Array.Empty<object>() },
            "vendor" => new { kind = normalized, filters = new { from = start, to = end, propertyId }, totals = await work.Where(x => x.VendorId != null).GroupBy(x => x.VendorId).Select(g => new { vendorId = g.Key, workItems = g.Count(), cost = g.Sum(x => x.Cost ?? 0m) }).OrderByDescending(x => x.cost).Take(limit).ToListAsync(ct), rows = Array.Empty<object>() },
            "portfolio" => new { kind = normalized, filters = new { from = start, to = end, propertyId }, totals = new { properties = await store.Properties.AsNoTracking().CountAsync(x => propertyId == null || x.Id == propertyId, ct), workItems = await work.CountAsync(ct), activeLeases = await leases.CountAsync(ct) }, rows = Array.Empty<object>() },
            _ => null!
        };
        return result is null ? Results.NotFound(new { error = "Unknown report kind", supported = new[] { "operational", "leasing", "financial", "occupancy", "maintenance", "vendor", "portfolio" } }) : Results.Ok(result);
    }

    private static async Task<IResult> ExportReport(string kind, DateOnly? from, DateOnly? to, Guid? propertyId, OperationsStore store, CancellationToken ct)
    {
        var response = await GetReport(kind, from, to, propertyId, 10000, store, ct);
        if (response is not IValueHttpResult { Value: not null } value) return response;
        var json = System.Text.Json.JsonSerializer.Serialize(value.Value);
        var csv = new StringBuilder("report,json\r\n").Append(kind).Append(',').Append('"').Append(json.Replace("\"", "\"\"", StringComparison.Ordinal)).Append("\"\r\n").ToString();
        return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", $"{kind}-report.csv");
    }

    private static bool TryRange(DateOnly? from, DateOnly? to, out DateTimeOffset start, out DateTimeOffset end, out string? error)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var first = from ?? today.AddDays(-30); var last = to ?? today;
        start = first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); end = last.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        error = first > last ? "from must be on or before to" : null;
        return error is null;
    }
}

public sealed record CreateReportScheduleRequest(string Name, string Kind, ReportScheduleFrequency Frequency, string Recipient, ReportExportFormat Format = ReportExportFormat.Csv, DateTimeOffset? NextRunAt = null, Dictionary<string, string?>? Filters = null);
