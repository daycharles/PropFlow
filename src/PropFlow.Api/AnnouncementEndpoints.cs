using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Infrastructure.Identity;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class AnnouncementEndpoints
{
    public static void MapAnnouncementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/announcements").RequireAuthorization(Capabilities.ReadWork);
        group.MapGet("/", async (OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.Announcements.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(200)
                .Select(x => new { x.Id, x.Title, x.Body, x.CreatedAt, x.ExpiresAt, x.Status }).ToListAsync(ct)));
        group.MapPost("/", async (AnnouncementRequest request, OperationsStore store, CancellationToken ct) =>
        {
            try
            {
                var announcement = new PropFlow.Domain.Communications.Announcement(store.OrganizationId, Guid.NewGuid(), request.Title, request.Body, request.ExpiresAt);
                store.Announcements.Add(announcement);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/announcements/{announcement.Id}", announcement);
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapPost("/{id:guid}/publish", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var announcement = await store.Announcements.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (announcement is null) return Results.NotFound();
            announcement.Publish(); await store.SaveChangesAsync(ct); return Results.Ok(announcement);
        }).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapPost("/{id:guid}/archive", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var announcement = await store.Announcements.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (announcement is null) return Results.NotFound();
            announcement.Archive(); await store.SaveChangesAsync(ct); return Results.NoContent();
        }).RequireAuthorization(Capabilities.ManageLeasing);
    }

    public static void MapResidentAnnouncementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/portal/announcements").RequireAuthorization(Capabilities.ResidentPortalRead);
        group.MapGet("/", async (OperationsStore store, TimeProvider clock, CancellationToken ct) =>
            Results.Ok(await store.Announcements.AsNoTracking()
                .Where(x => x.Status == PropFlow.Domain.Communications.AnnouncementStatus.Published &&
                            (x.ExpiresAt == null || x.ExpiresAt > clock.GetUtcNow()))
                .OrderByDescending(x => x.CreatedAt).Take(100)
                .Select(x => new { x.Id, x.Title, x.Body, x.CreatedAt, x.ExpiresAt }).ToListAsync(ct)));
    }
}

public sealed record AnnouncementRequest(string Title, string Body, DateTimeOffset? ExpiresAt);
