using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class SavedViewEndpoints
{
    public static void MapSavedViewEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/saved-views").RequireAuthorization(Capabilities.ReadWork);
        group.MapGet("/", async (ClaimsPrincipal user, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.SavedViews.AsNoTracking().Where(x => x.UserId == Actor(user))
                .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).ToListAsync(ct)));
        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, OperationsStore store, CancellationToken ct) =>
            await Owned(store, id, Actor(user), ct) is { } view ? Results.Ok(view) : Results.NotFound());
        group.MapPost("/", async (SavedViewRequest request, ClaimsPrincipal user, OperationsStore store, CancellationToken ct) =>
        {
            try
            {
                var actor = Actor(user);
                if (request.IsDefault) await ClearDefaultAsync(store, actor, null, ct);
                var view = new SavedView(store.OrganizationId, Guid.NewGuid(), actor, request.Name, request.Filters.GetRawText(), request.Columns.GetRawText(), request.IsDefault);
                store.SavedViews.Add(view); await store.SaveChangesAsync(ct);
                return Results.Created($"/api/saved-views/{view.Id}", view);
            }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        });
        group.MapPut("/{id:guid}", async (Guid id, SavedViewRequest request, ClaimsPrincipal user, OperationsStore store, CancellationToken ct) =>
        {
            var actor = Actor(user); var view = await Owned(store, id, actor, ct); if (view is null) return Results.NotFound();
            try
            {
                if (request.IsDefault) await ClearDefaultAsync(store, actor, id, ct);
                view.Update(request.Name, request.Filters.GetRawText(), request.Columns.GetRawText(), request.IsDefault);
                await store.SaveChangesAsync(ct); return Results.Ok(view);
            }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        });
        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, OperationsStore store, CancellationToken ct) =>
        {
            var view = await Owned(store, id, Actor(user), ct); if (view is null) return Results.NotFound();
            store.SavedViews.Remove(view); await store.SaveChangesAsync(ct); return Results.NoContent();
        });
    }

    private static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Task<SavedView?> Owned(OperationsStore store, Guid id, Guid actor, CancellationToken ct) =>
        store.SavedViews.SingleOrDefaultAsync(x => x.Id == id && x.UserId == actor, ct);
    private static async Task ClearDefaultAsync(OperationsStore store, Guid actor, Guid? exceptId, CancellationToken ct)
    {
        var defaults = await store.SavedViews.Where(x => x.UserId == actor && x.IsDefault && (exceptId == null || x.Id != exceptId)).ToListAsync(ct);
        foreach (var view in defaults) view.Update(view.Name, view.Filters, view.Columns, false);
    }
}

public sealed record SavedViewRequest(string Name, JsonElement Filters, JsonElement Columns, bool IsDefault = false);
