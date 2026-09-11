using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Configuration;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/categories").RequireAuthorization(Capabilities.ReadWork);
        // PF-S03.03: appliesTo defaults to WorkItem, matching every caller's behavior before this
        // parameter existed — an explicit value only matters once a second entity type is added.
        group.MapGet("/", async (OperationsStore store, CancellationToken ct, ConfigurationEntityType appliesTo = ConfigurationEntityType.WorkItem) =>
            Results.Ok(await store.Categories.AsNoTracking().Where(x => x.AppliesTo == appliesTo)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct)));
        group.MapPost("/", async (CategoryRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return Results.Problem(statusCode: 400, title: "Name is required");
            try
            {
                var category = new WorkCategory(store.OrganizationId, Guid.NewGuid(), request.Name, request.SortOrder, request.AppliesTo);
                store.Categories.Add(category); await store.SaveChangesAsync(ct); return Results.Created($"/api/categories/{category.Id}", category);
            }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
            catch (DbUpdateException) { return Results.Problem(statusCode: 409, title: "A category with this name already exists for this entity type"); }
        }).RequireAuthorization(Capabilities.ManageCategories);
        group.MapPut("/{id:guid}", async (Guid id, CategoryRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var category = await store.Categories.SingleOrDefaultAsync(x => x.Id == id, ct); if (category is null) return Results.NotFound();
            try { category.Rename(request.Name); category.Reorder(request.SortOrder); await store.SaveChangesAsync(ct); return Results.Ok(category); }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        }).RequireAuthorization(Capabilities.ManageCategories);
        group.MapPost("/{id:guid}/archive", async (Guid id, OperationsStore store, CancellationToken ct) =>
        { var category = await store.Categories.SingleOrDefaultAsync(x => x.Id == id, ct); if (category is null) return Results.NotFound(); category.Archive(); await store.SaveChangesAsync(ct); return Results.NoContent(); }).RequireAuthorization(Capabilities.ManageCategories);
    }
}
// AppliesTo is immutable once created (like CustomFieldDefinition's) — retagging is a
// delete-and-recreate, so it is not part of the PUT/rename shape.
public sealed record CategoryRequest(string Name, int SortOrder, ConfigurationEntityType AppliesTo = ConfigurationEntityType.WorkItem);
