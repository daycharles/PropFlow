using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Configuration;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

// PF-S03.07. Personal settings, like SavedViewEndpoints - gated on plain Work.Read (any staff
// member manages their own notification preferences, no elevated capability needed) and always
// scoped to the caller's own UserId, never a request parameter. No delivery channel reads these
// yet; this is the preference model and its API only.
public static class NotificationPreferenceEndpoints
{
    public static void MapNotificationPreferenceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/settings/notification-preferences").RequireAuthorization(Capabilities.ReadWork);

        group.MapGet("/", async (ClaimsPrincipal user, OperationsStore store, CancellationToken ct) =>
        {
            var actor = Actor(user);
            var stored = await store.NotificationPreferences.AsNoTracking()
                .Where(x => x.UserId == actor).ToListAsync(ct);
            return Results.Ok(Enum.GetValues<NotificationEventType>()
                .Select(t => new NotificationPreferenceResponse(t, stored.SingleOrDefault(x => x.EventType == t)?.Enabled ?? true))
                .ToList());
        });

        group.MapPut("/{eventType}", async (NotificationEventType eventType, NotificationPreferenceRequest request,
            ClaimsPrincipal user, OperationsStore store, CancellationToken ct) =>
        {
            if (!Enum.IsDefined(eventType)) return Results.Problem(statusCode: 400, title: "Unknown event type.");
            var actor = Actor(user);
            var preference = await store.NotificationPreferences.SingleOrDefaultAsync(x => x.UserId == actor && x.EventType == eventType, ct);
            if (preference is null)
            {
                preference = NotificationPreference.Create(store.OrganizationId, Guid.NewGuid(), actor, eventType, request.Enabled);
                store.NotificationPreferences.Add(preference);
            }
            else
            {
                preference.SetEnabled(request.Enabled);
            }
            await store.SaveChangesAsync(ct);
            return Results.Ok(new NotificationPreferenceResponse(preference.EventType, preference.Enabled));
        });
    }

    private static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record NotificationPreferenceRequest(bool Enabled);
public sealed record NotificationPreferenceResponse(NotificationEventType EventType, bool Enabled);
