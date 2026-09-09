using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/categories").RequireAuthorization(Capabilities.ReadWork);
        group.MapGet("/", async (OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.Categories.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct)));
        group.MapPost("/", async (CategoryRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return Results.Problem(statusCode: 400, title: "Name is required");
            var category = new WorkCategory(store.OrganizationId, Guid.NewGuid(), request.Name, request.SortOrder);
            store.Categories.Add(category); await store.SaveChangesAsync(ct); return Results.Created($"/api/categories/{category.Id}", category);
        }).RequireAuthorization(Capabilities.ManageCategories);
        group.MapPut("/{id:guid}", async (Guid id, CategoryRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var category = await store.Categories.SingleOrDefaultAsync(x => x.Id == id, ct); if (category is null) return Results.NotFound();
            category.Rename(request.Name); category.Reorder(request.SortOrder); await store.SaveChangesAsync(ct); return Results.Ok(category);
        }).RequireAuthorization(Capabilities.ManageCategories);
        group.MapPost("/{id:guid}/archive", async (Guid id, OperationsStore store, CancellationToken ct) =>
        { var category = await store.Categories.SingleOrDefaultAsync(x => x.Id == id, ct); if (category is null) return Results.NotFound(); category.Archive(); await store.SaveChangesAsync(ct); return Results.NoContent(); }).RequireAuthorization(Capabilities.ManageCategories);
    }
}
public sealed record CategoryRequest(string Name, int SortOrder);
