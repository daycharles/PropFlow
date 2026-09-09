using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Assets;
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
