using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
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
    }

    private static async Task<IResult> GetReport(string kind, DateOnly? from, DateOnly? to, Guid? propertyId, int? take, OperationsStore store, CancellationToken ct)
    {
        if (!TryRange(from, to, out var start, out var end, out var error)) return Results.BadRequest(error);
        var limit = Math.Clamp(take ?? 1000, 1, 10000);
        var normalized = kind.Trim().ToLowerInvariant();
        var work = store.WorkItems.AsNoTracking().Where(x => x.CreatedAt >= start && x.CreatedAt < end);
        if (propertyId is not null) work = work.Where(x => x.PropertyId == propertyId);
        var leases = store.Leases.AsNoTracking().Where(x => x.StartsOn <= DateOnly.FromDateTime(end.UtcDateTime) && x.EndsOn >= DateOnly.FromDateTime(start.UtcDateTime));
        if (propertyId is not null) leases = leases.Where(x => store.Spaces.Any(s => s.Id == x.SpaceId && s.PropertyId == propertyId));

        object result = normalized switch
        {
            "operational" or "maintenance" => new { kind = normalized, filters = new { from = start, to = end, propertyId }, totals = await work.GroupBy(_ => 1).Select(g => new { count = g.Count(), estimatedCost = g.Sum(x => x.Cost ?? 0m) }).FirstOrDefaultAsync(ct) ?? new { count = 0, estimatedCost = 0m }, rows = await work.OrderByDescending(x => x.CreatedAt).Take(limit).Select(x => new { x.Id, x.Title, x.Status, x.Cost, x.CreatedAt, x.PropertyId, x.VendorId }).ToListAsync(ct) },
            "leasing" => new { kind = normalized, filters = new { from = start, to = end, propertyId }, totals = await leases.GroupBy(_ => 1).Select(g => new { count = g.Count(), monthlyRent = g.Sum(x => x.MonthlyRent) }).FirstOrDefaultAsync(ct) ?? new { count = 0, monthlyRent = 0m }, rows = await leases.OrderBy(x => x.StartsOn).Take(limit).Select(x => new { x.Id, x.SpaceId, x.ResidentId, x.Status, x.StartsOn, x.EndsOn, x.MonthlyRent }).ToListAsync(ct) },
            "financial" => new { kind = normalized, filters = new { from = start, to = end, propertyId }, totals = new { charges = await store.LeaseCharges.AsNoTracking().Where(x => x.DueOn >= DateOnly.FromDateTime(start.UtcDateTime) && x.DueOn <= DateOnly.FromDateTime(end.UtcDateTime)).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m, payments = await store.ResidentPayments.AsNoTracking().Where(x => x.SettledAt >= start && x.SettledAt < end).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m }, rows = Array.Empty<object>() },
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
