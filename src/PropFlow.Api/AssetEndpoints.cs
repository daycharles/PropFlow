using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Application.Assets;
using PropFlow.Domain.Assets;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/assets").RequireAuthorization(Capabilities.ReadWork);

        group.MapGet("/", async (Guid? propertyId, OperationsStore store, CancellationToken ct) =>
        {
            var query = store.Assets.AsNoTracking();
            if (propertyId is { } id) query = query.Where(x => x.PropertyId == id);
            return Results.Ok(await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Take(500).ToListAsync(ct));
        });

        group.MapGet("/{id:guid}", async (Guid id, OperationsStore store, CancellationToken ct) =>
            await store.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) is { } asset
                ? Results.Ok(asset) : Results.NotFound());

        // The maintenance history: every work item linked to this asset, newest first, plus the
        // roll-ups PF-6.05's repeat-repair warning wants (count, total cost, age, warranty).
        group.MapGet("/{id:guid}/history", async (Guid id, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var asset = await store.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (asset is null) return Results.NotFound();

            var history = await (
                from w in store.WorkItems.AsNoTracking().Where(x => x.AssetId == id)
                join c in store.Categories on w.CategoryId equals (Guid?)c.Id into categories
                from c in categories.DefaultIfEmpty()
                join v in store.Vendors on w.VendorId equals (Guid?)v.Id into vendors
                from v in vendors.DefaultIfEmpty()
                orderby w.CreatedAt descending, w.Id
                select new AssetWorkHistoryItem(w.Id, w.Title, w.Status, w.Priority,
                    c == null ? null : c.Name, v == null ? null : v.Name, w.CreatedAt, w.CompletedAt, w.Cost)
            ).ToListAsync(ct);

            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.Date);
            return Results.Ok(new AssetHistoryResponse(
                asset, asset.AgeInYears(today), asset.IsUnderWarranty(today),
                history.Count, history.Sum(x => x.Cost ?? 0m), history));
        });

        group.MapGet("/{id:guid}/lifecycle", async (Guid id, int? warrantyAlertDays, OperationsStore store,
            TimeProvider clock, CancellationToken ct) =>
        {
            var asset = await store.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (asset is null) return Results.NotFound();
            var costs = await store.AssetLifecycleCosts.AsNoTracking().Where(x => x.AssetId == id).ToListAsync(ct);
            var asOf = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.Date);
            return Results.Ok(AssetLifecycleService.PlanReplacement(asset, asOf, warrantyAlertDays ?? 90, costs));
        });

        group.MapGet("/{id:guid}/maintenance-plans", async (Guid id, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.PreventiveMaintenancePlans.AsNoTracking().Where(x => x.AssetId == id)
                .OrderBy(x => x.Name).ToListAsync(ct)));

        group.MapPost("/{id:guid}/maintenance-plans", async (Guid id, MaintenancePlanRequest request,
            OperationsStore store, ITenantContext tenant, CancellationToken ct) =>
        {
            try
            {
                if (!await store.Assets.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
                var plan = new PreventiveMaintenancePlan(tenant.OrganizationId, Guid.NewGuid(), id, request.Name,
                    request.Recurrence, request.FirstDueOn);
                store.PreventiveMaintenancePlans.Add(plan);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/assets/{id}/maintenance-plans/{plan.Id}", plan);
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageAssets);

        group.MapGet("/{id:guid}/meters", async (Guid id, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.MeterReadings.AsNoTracking().Where(x => x.AssetId == id)
                .OrderByDescending(x => x.ReadOn).ThenBy(x => x.MeterName).Take(500).ToListAsync(ct)));

        group.MapPost("/{id:guid}/meters", async (Guid id, MeterReadingRequest request, OperationsStore store,
            ITenantContext tenant, CancellationToken ct) =>
        {
            try
            {
                if (!await store.Assets.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
                var reading = new MeterReading(tenant.OrganizationId, Guid.NewGuid(), id, request.MeterName,
                    request.Unit, request.Reading, request.ReadOn);
                store.MeterReadings.Add(reading);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/assets/{id}/meters/{reading.Id}", reading);
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageAssets);

        group.MapGet("/{id:guid}/lifecycle-costs", async (Guid id, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.AssetLifecycleCosts.AsNoTracking().Where(x => x.AssetId == id)
                .OrderByDescending(x => x.IncurredOn).ToListAsync(ct)));

        group.MapPost("/{id:guid}/lifecycle-costs", async (Guid id, LifecycleCostRequest request,
            OperationsStore store, ITenantContext tenant, CancellationToken ct) =>
        {
            try
            {
                if (!await store.Assets.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
                var cost = new AssetLifecycleCost(tenant.OrganizationId, Guid.NewGuid(), id, request.Type,
                    request.Amount, request.IncurredOn, request.Description, request.WorkItemId);
                store.AssetLifecycleCosts.Add(cost);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/assets/{id}/lifecycle-costs/{cost.Id}", cost);
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageAssets);

        // The organization's repeat-repair thresholds (PF-6.04). The defaults apply until one is set.
        group.MapGet("/repeat-repair-policy", async (IRepeatRepairDetector detector, CancellationToken ct) =>
            Results.Ok(await detector.GetPolicyAsync(ct)));

        group.MapPut("/repeat-repair-policy", async (RepeatRepairPolicyRequest request, IRepeatRepairDetector detector, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await detector.SetPolicyAsync(request.RepairThreshold, request.WindowDays, request.MatchByCategory, ct));
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return Results.Problem(statusCode: 400, title: exception.Message);
            }
        }).RequireAuthorization(Capabilities.ManageAssets);

        // Assess one asset against the current policy. categoryId narrows the count when the
        // policy's category-similarity option is on (the "new work about this asset" surface).
        group.MapGet("/{id:guid}/repeat-repair", async (Guid id, Guid? categoryId, IRepeatRepairDetector detector, CancellationToken ct) =>
            await detector.AssessAsync(id, categoryId, ct) is { } assessment ? Results.Ok(assessment) : Results.NotFound());

        group.MapPost("/", async (AssetRequest request, OperationsStore store, ITenantContext tenant, CancellationToken ct) =>
        {
            Asset asset;
            try
            {
                asset = new Asset(tenant.OrganizationId, Guid.NewGuid(), request.PropertyId, request.SpaceId, request.Kind, request.Name);
                Apply(asset, request);
            }
            catch (ArgumentException exception)
            {
                return Results.Problem(statusCode: 400, title: exception.Message);
            }

            store.Assets.Add(asset);
            try
            {
                await store.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return Results.Problem(statusCode: 400, title: "Unknown property or space");
            }
            return Results.Created($"/api/assets/{asset.Id}", asset);
        }).RequireAuthorization(Capabilities.ManageAssets);

        group.MapPut("/{id:guid}", async (Guid id, AssetRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var asset = await store.Assets.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (asset is null) return Results.NotFound();

            try
            {
                Apply(asset, request);
            }
            catch (ArgumentException exception)
            {
                return Results.Problem(statusCode: 400, title: exception.Message);
            }

            await store.SaveChangesAsync(ct);
            return Results.Ok(asset);
        }).RequireAuthorization(Capabilities.ManageAssets);
    }

    // The property and space are fixed at creation; the rest of the record is editable.
    private static void Apply(Asset asset, AssetRequest request)
    {
        asset.Describe(request.Name, request.Kind, request.Manufacturer, request.Model, request.SerialNumber);
        asset.SetLifecycle(request.InstalledOn, request.WarrantyExpiresOn, request.ExpectedServiceLifeYears);
        asset.RecordCondition(request.Condition);
        asset.SetReplacementCost(request.ReplacementCostEstimate);
        asset.SetNotes(request.Notes);
    }
}

public sealed record AssetWorkHistoryItem(Guid Id, string Title, WorkStatus Status, WorkPriority Priority,
    string? CategoryName, string? VendorName, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, decimal? Cost);
public sealed record AssetHistoryResponse(Asset Asset, int? AgeInYears, bool UnderWarranty,
    int WorkOrderCount, decimal TotalCost, IReadOnlyList<AssetWorkHistoryItem> History);

public sealed record RepeatRepairPolicyRequest(int RepairThreshold, int WindowDays, bool MatchByCategory);
public sealed record MaintenancePlanRequest(string Name, MaintenanceRecurrence Recurrence, DateOnly FirstDueOn);
public sealed record MeterReadingRequest(string MeterName, MeterUnit Unit, decimal Reading, DateOnly ReadOn);
public sealed record LifecycleCostRequest(AssetCostType Type, decimal Amount, DateOnly IncurredOn, string Description, Guid? WorkItemId);

// Tenant comes from the verified session.
public sealed record AssetRequest(
    AssetKind Kind,
    string Name,
    Guid PropertyId,
    Guid? SpaceId,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    DateOnly? InstalledOn,
    DateOnly? WarrantyExpiresOn,
    int? ExpectedServiceLifeYears,
    AssetCondition Condition,
    decimal? ReplacementCostEstimate,
    string? Notes);
