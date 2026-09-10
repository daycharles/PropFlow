using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

/// <summary>
/// Read-only, organization-scoped lookup data used while creating and editing work.
/// </summary>
public static class ReferenceEndpoints
{
    // These lists stay a flat array (the web client consumes them that way) but are bounded and
    // take an optional `?q=` name filter so a large tenant can narrow instead of pulling 500.
    // Full offset paging waits until these get their own screens — see docs/followups.md.
    private const int ListCap = 500;

    private static string? Contains(string? q) =>
        string.IsNullOrWhiteSpace(q) ? null : "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    public static void MapReferenceEndpoints(this WebApplication app)
    {
        var vendors = app.MapGroup("/api/vendors").RequireAuthorization(Capabilities.ReadWork);
        vendors.MapGet("/", async (string? q, OperationsStore store, CancellationToken ct) =>
        {
            var like = Contains(q);
            return Results.Ok(await store.Vendors.AsNoTracking()
                .Where(x => like == null || EF.Functions.ILike(x.Name, like, "\\"))
                .OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.Name, x.Email, x.Phone, x.IsActive }).Take(ListCap).ToListAsync(ct));
        });
        vendors.MapGet("/{id:guid}", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var vendor = await store.Vendors.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.Id, x.Name, x.Email, x.Phone, x.IsActive }).SingleOrDefaultAsync(ct);
            return vendor is null ? Results.NotFound() : Results.Ok(vendor);
        });

        var employees = app.MapGroup("/api/employees").RequireAuthorization(Capabilities.ReadWork);
        employees.MapGet("/", async (string? q, OperationsStore store, CancellationToken ct) =>
        {
            var like = Contains(q);
            return Results.Ok(await store.Employees.AsNoTracking()
                .Where(x => like == null || EF.Functions.ILike(x.DisplayName, like, "\\"))
                .OrderBy(x => x.DisplayName).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.DisplayName, x.Email, x.Phone, x.IsActive }).Take(ListCap).ToListAsync(ct));
        });

        var properties = app.MapGroup("/api/properties").RequireAuthorization(Capabilities.ReadWork);
        properties.MapGet("/", async (string? q, OperationsStore store, CancellationToken ct) =>
        {
            var like = Contains(q);
            return Results.Ok(await store.Properties.AsNoTracking()
                .Where(x => like == null || EF.Functions.ILike(x.Name, like, "\\"))
                .OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.PortfolioId, x.Name, x.TimeZoneId }).Take(ListCap).ToListAsync(ct));
        });
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
