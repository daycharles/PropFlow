using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

/// <summary>
/// Read-only, organization-scoped lookup data used while creating and editing work.
/// </summary>
public static class ReferenceEndpoints
{
    // Lookup lists are unpaged; cap them so a large tenant cannot pull an unbounded response.
    // A tenant that outgrows this needs the paged work-list-style endpoint instead.
    private const int ListCap = 500;

    public static void MapReferenceEndpoints(this WebApplication app)
    {
        var vendors = app.MapGroup("/api/vendors").RequireAuthorization(Capabilities.ReadWork);
        vendors.MapGet("/", async (OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.Vendors.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.Name, x.Email, x.Phone, x.IsActive }).Take(ListCap).ToListAsync(ct)));
        vendors.MapGet("/{id:guid}", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var vendor = await store.Vendors.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.Id, x.Name, x.Email, x.Phone, x.IsActive }).SingleOrDefaultAsync(ct);
            return vendor is null ? Results.NotFound() : Results.Ok(vendor);
        });

        var employees = app.MapGroup("/api/employees").RequireAuthorization(Capabilities.ReadWork);
        employees.MapGet("/", async (OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.Employees.AsNoTracking().OrderBy(x => x.DisplayName).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.DisplayName, x.Email, x.Phone, x.IsActive }).Take(ListCap).ToListAsync(ct)));

        var properties = app.MapGroup("/api/properties").RequireAuthorization(Capabilities.ReadWork);
        properties.MapGet("/", async (OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.Properties.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.PortfolioId, x.Name, x.TimeZoneId }).Take(ListCap).ToListAsync(ct)));
        properties.MapGet("/{id:guid}", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var property = await store.Properties.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.Id, x.PortfolioId, x.Name, x.TimeZoneId }).SingleOrDefaultAsync(ct);
            if (property is null) return Results.NotFound();

            var buildings = await store.Buildings.AsNoTracking().Where(x => x.PropertyId == id)
                .OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.Name }).Take(ListCap).ToListAsync(ct);
            var spaces = await store.Spaces.AsNoTracking().Where(x => x.PropertyId == id)
                .OrderBy(x => x.Code).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.BuildingId, x.Code }).Take(ListCap).ToListAsync(ct);
            return Results.Ok(new { property, buildings, spaces });
        });
    }
}
