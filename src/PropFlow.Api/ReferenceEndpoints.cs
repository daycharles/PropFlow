using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Properties;
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

        var portfolios = app.MapGroup("/api/portfolios").RequireAuthorization(Capabilities.ReadWork);
        portfolios.MapGet("/", async (string? q, OperationsStore store, CancellationToken ct) =>
        {
            var like = Contains(q);
            return Results.Ok(await store.Portfolios.AsNoTracking()
                .Where(x => !x.IsArchived && (like == null || EF.Functions.ILike(x.Name, like, "\\")))
                .OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.Name, x.IsArchived }).Take(ListCap).ToListAsync(ct));
        });
        portfolios.MapPost("/", async (PortfolioRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return Results.Problem(statusCode: 400, title: "Name is required");
            var portfolio = new Portfolio(store.OrganizationId, Guid.NewGuid(), request.Name);
            store.Portfolios.Add(portfolio);
            await store.SaveChangesAsync(ct);
            return Results.Created($"/api/portfolios/{portfolio.Id}", new { portfolio.Id, portfolio.Name });
        }).RequireAuthorization(Capabilities.ManageProperties);
        portfolios.MapPost("/{id:guid}/archive", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var portfolio = await store.Portfolios.SingleOrDefaultAsync(x => x.Id == id, ct); if (portfolio is null) return Results.NotFound(); portfolio.Archive(); await store.SaveChangesAsync(ct); return Results.NoContent();
        }).RequireAuthorization(Capabilities.ManageProperties);
        portfolios.MapPost("/{id:guid}/restore", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var portfolio = await store.Portfolios.SingleOrDefaultAsync(x => x.Id == id, ct); if (portfolio is null) return Results.NotFound(); portfolio.Restore(); await store.SaveChangesAsync(ct); return Results.NoContent();
        }).RequireAuthorization(Capabilities.ManageProperties);
        portfolios.MapPut("/{id:guid}", async (Guid id, PortfolioRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var portfolio = await store.Portfolios.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (portfolio is null) return Results.NotFound();
            portfolio.Rename(request.Name);
            await store.SaveChangesAsync(ct);
            return Results.Ok(new { portfolio.Id, portfolio.Name });
        }).RequireAuthorization(Capabilities.ManageProperties);

        var properties = app.MapGroup("/api/properties").RequireAuthorization(Capabilities.ReadWork);
        properties.MapGet("/", async (string? q, OperationsStore store, CancellationToken ct) =>
        {
            var like = Contains(q);
            return Results.Ok(await store.Properties.AsNoTracking()
                .Where(x => !x.IsArchived && (like == null || EF.Functions.ILike(x.Name, like, "\\")))
                .OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.PortfolioId, x.Name, x.TimeZoneId, x.IsArchived }).Take(ListCap).ToListAsync(ct));
        });
        properties.MapGet("/{id:guid}", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var property = await store.Properties.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.Id, x.PortfolioId, x.Name, x.TimeZoneId, x.IsArchived }).SingleOrDefaultAsync(ct);
            if (property is null) return Results.NotFound();

            var buildings = await store.Buildings.AsNoTracking().Where(x => x.PropertyId == id && !x.IsArchived)
                .OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.Name, x.IsArchived }).Take(ListCap).ToListAsync(ct);
            var spaces = await store.Spaces.AsNoTracking().Where(x => x.PropertyId == id && !x.IsArchived)
                .OrderBy(x => x.Code).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.BuildingId, x.Code, x.IsArchived, IsOccupied = store.Occupancies.Any(occupancy => occupancy.SpaceId == x.Id && occupancy.MovedOutOn == null) }).Take(ListCap).ToListAsync(ct);
            var contacts = await store.PropertyContacts.AsNoTracking().Where(x => x.PropertyId == id).OrderBy(x => x.FullName).ThenBy(x => x.Id).Select(x => new { x.Id, x.PropertyId, x.FullName, x.Role, x.Email, x.Phone }).Take(ListCap).ToListAsync(ct);
            var documents = await store.PropertyDocuments.AsNoTracking().Where(x => x.PropertyId == id).OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.PropertyId, x.Title, x.DocumentUrl, x.DocumentType, x.CreatedAt }).Take(ListCap).ToListAsync(ct);
            var amenities = await store.PropertyAmenities.AsNoTracking().Where(x => x.PropertyId == id && !x.IsArchived).OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new { x.Id, x.PropertyId, x.Name, x.Details }).Take(ListCap).ToListAsync(ct);
            return Results.Ok(new { property, buildings, spaces, contacts, documents, amenities });
        });
        properties.MapPost("/", async (PropertyRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (await store.Portfolios.AllAsync(x => x.Id != request.PortfolioId, ct)) return Results.Problem(statusCode: 400, title: "Portfolio was not found");
            var property = new Property(store.OrganizationId, Guid.NewGuid(), request.PortfolioId, request.Name, request.TimeZoneId);
            store.Properties.Add(property);
            await store.SaveChangesAsync(ct);
            return Results.Created($"/api/properties/{property.Id}", new { property.Id, property.PortfolioId, property.Name, property.TimeZoneId });
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapGet("/{propertyId:guid}/documents", async (Guid propertyId, OperationsStore store, CancellationToken ct) => Results.Ok(await store.PropertyDocuments.AsNoTracking().Where(x => x.PropertyId == propertyId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct)));
        properties.MapPost("/{propertyId:guid}/documents", async (Guid propertyId, PropertyDocumentRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.Properties.AnyAsync(x => x.Id == propertyId, ct)) return Results.NotFound();
            try { var document = new PropertyDocument(store.OrganizationId, Guid.NewGuid(), propertyId, request.Title, request.DocumentUrl, request.DocumentType); store.PropertyDocuments.Add(document); await store.SaveChangesAsync(ct); return Results.Created($"/api/properties/{propertyId}/documents/{document.Id}", document); }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapGet("/{propertyId:guid}/contacts", async (Guid propertyId, OperationsStore store, CancellationToken ct) => Results.Ok(await store.PropertyContacts.AsNoTracking().Where(x => x.PropertyId == propertyId).OrderBy(x => x.FullName).ToListAsync(ct)));
        properties.MapPost("/{propertyId:guid}/contacts", async (Guid propertyId, PropertyContactRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.Properties.AnyAsync(x => x.Id == propertyId, ct)) return Results.NotFound();
            try { var contact = new PropertyContact(store.OrganizationId, Guid.NewGuid(), propertyId, request.FullName, request.Role, request.Email, request.Phone); store.PropertyContacts.Add(contact); await store.SaveChangesAsync(ct); return Results.Created($"/api/properties/{propertyId}/contacts/{contact.Id}", contact); }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapGet("/{propertyId:guid}/amenities", async (Guid propertyId, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.PropertyAmenities.AsNoTracking().Where(x => x.PropertyId == propertyId && !x.IsArchived).OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct)));
        properties.MapPost("/{propertyId:guid}/amenities", async (Guid propertyId, PropertyAmenityRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.Properties.AnyAsync(x => x.Id == propertyId, ct)) return Results.NotFound();
            try
            {
                var amenity = new PropertyAmenity(store.OrganizationId, Guid.NewGuid(), propertyId, request.Name, request.Details);
                store.PropertyAmenities.Add(amenity);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/properties/{propertyId}/amenities/{amenity.Id}", amenity);
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapPut("/{propertyId:guid}/amenities/{amenityId:guid}", async (Guid propertyId, Guid amenityId, PropertyAmenityRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var amenity = await store.PropertyAmenities.SingleOrDefaultAsync(x => x.Id == amenityId && x.PropertyId == propertyId, ct);
            if (amenity is null) return Results.NotFound();
            try { amenity.Update(request.Name, request.Details); await store.SaveChangesAsync(ct); return Results.Ok(amenity); }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapPost("/{propertyId:guid}/amenities/{amenityId:guid}/archive", async (Guid propertyId, Guid amenityId, OperationsStore store, CancellationToken ct) =>
        {
            var amenity = await store.PropertyAmenities.SingleOrDefaultAsync(x => x.Id == amenityId && x.PropertyId == propertyId, ct);
            if (amenity is null) return Results.NotFound();
            amenity.Archive(); await store.SaveChangesAsync(ct); return Results.NoContent();
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapPut("/{id:guid}", async (Guid id, PropertyRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var property = await store.Properties.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (property is null) return Results.NotFound();
            if (await store.Portfolios.AllAsync(x => x.Id != request.PortfolioId, ct)) return Results.Problem(statusCode: 400, title: "Portfolio was not found");
            property.Update(request.Name, request.TimeZoneId);
            await store.SaveChangesAsync(ct);
            return Results.Ok(new { property.Id, property.PortfolioId, property.Name, property.TimeZoneId });
        }).RequireAuthorization(Capabilities.ManageProperties);

        properties.MapPost("/{propertyId:guid}/buildings", async (Guid propertyId, NameRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.Properties.AnyAsync(x => x.Id == propertyId, ct)) return Results.NotFound();
            var building = new Building(store.OrganizationId, Guid.NewGuid(), propertyId, request.Name);
            store.Buildings.Add(building);
            await store.SaveChangesAsync(ct);
            return Results.Created($"/api/properties/{propertyId}/buildings/{building.Id}", new { building.Id, building.PropertyId, building.Name });
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapPost("/{id:guid}/archive", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var property = await store.Properties.SingleOrDefaultAsync(x => x.Id == id, ct); if (property is null) return Results.NotFound(); property.Archive(); await store.SaveChangesAsync(ct); return Results.NoContent();
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapPost("/{id:guid}/restore", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var property = await store.Properties.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == store.OrganizationId, ct); if (property is null) return Results.NotFound(); property.Restore(); await store.SaveChangesAsync(ct); return Results.NoContent();
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapPost("/{propertyId:guid}/buildings/{buildingId:guid}/archive", async (Guid propertyId, Guid buildingId, OperationsStore store, CancellationToken ct) =>
        {
            var building = await store.Buildings.SingleOrDefaultAsync(x => x.Id == buildingId && x.PropertyId == propertyId, ct); if (building is null) return Results.NotFound(); building.Archive(); await store.SaveChangesAsync(ct); return Results.NoContent();
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapPost("/{propertyId:guid}/buildings/{buildingId:guid}/restore", async (Guid propertyId, Guid buildingId, OperationsStore store, CancellationToken ct) =>
        {
            var building = await store.Buildings.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == buildingId && x.PropertyId == propertyId && x.OrganizationId == store.OrganizationId, ct); if (building is null) return Results.NotFound(); building.Restore(); await store.SaveChangesAsync(ct); return Results.NoContent();
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapPut("/{propertyId:guid}/buildings/{buildingId:guid}", async (Guid propertyId, Guid buildingId, NameRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var building = await store.Buildings.SingleOrDefaultAsync(x => x.Id == buildingId && x.PropertyId == propertyId, ct);
            if (building is null) return Results.NotFound();
            building.Rename(request.Name);
            await store.SaveChangesAsync(ct);
            return Results.Ok(new { building.Id, building.PropertyId, building.Name });
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapPost("/{propertyId:guid}/spaces", async (Guid propertyId, SpaceRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.Properties.AnyAsync(x => x.Id == propertyId, ct)) return Results.NotFound();
            if (request.BuildingId is { } buildingId && !await store.Buildings.AnyAsync(x => x.Id == buildingId && x.PropertyId == propertyId, ct))
                return Results.Problem(statusCode: 400, title: "Building was not found in this property");
            var space = new Space(store.OrganizationId, Guid.NewGuid(), propertyId, request.BuildingId, request.Code);
            store.Spaces.Add(space);
            await store.SaveChangesAsync(ct);
            return Results.Created($"/api/properties/{propertyId}/spaces/{space.Id}", new { space.Id, space.PropertyId, space.BuildingId, space.Code });
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapPut("/{propertyId:guid}/spaces/{spaceId:guid}", async (Guid propertyId, Guid spaceId, SpaceRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var space = await store.Spaces.SingleOrDefaultAsync(x => x.Id == spaceId && x.PropertyId == propertyId, ct);
            if (space is null) return Results.NotFound();
            if (request.BuildingId is { } buildingId && !await store.Buildings.AnyAsync(x => x.Id == buildingId && x.PropertyId == propertyId, ct))
                return Results.Problem(statusCode: 400, title: "Building was not found in this property");
            space.Update(propertyId, request.BuildingId, request.Code);
            await store.SaveChangesAsync(ct);
            return Results.Ok(new { space.Id, space.PropertyId, space.BuildingId, space.Code });
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapPost("/{propertyId:guid}/spaces/{spaceId:guid}/archive", async (Guid propertyId, Guid spaceId, OperationsStore store, CancellationToken ct) =>
        {
            var space = await store.Spaces.SingleOrDefaultAsync(x => x.Id == spaceId && x.PropertyId == propertyId, ct); if (space is null) return Results.NotFound(); space.Archive(); await store.SaveChangesAsync(ct); return Results.NoContent();
        }).RequireAuthorization(Capabilities.ManageProperties);
        properties.MapPost("/{propertyId:guid}/spaces/{spaceId:guid}/restore", async (Guid propertyId, Guid spaceId, OperationsStore store, CancellationToken ct) =>
        {
            var space = await store.Spaces.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == spaceId && x.PropertyId == propertyId && x.OrganizationId == store.OrganizationId, ct); if (space is null) return Results.NotFound(); space.Restore(); await store.SaveChangesAsync(ct); return Results.NoContent();
        }).RequireAuthorization(Capabilities.ManageProperties);
    }
}

public sealed record PortfolioRequest(string Name);
public sealed record PropertyRequest(Guid PortfolioId, string Name, string TimeZoneId);
public sealed record NameRequest(string Name);
public sealed record SpaceRequest(Guid? BuildingId, string Code);
public sealed record PropertyContactRequest(string FullName, string Role, string? Email, string? Phone);
public sealed record PropertyDocumentRequest(string Title, string DocumentUrl, string? DocumentType);
public sealed record PropertyAmenityRequest(string Name, string? Details);
