using System.Security.Claims;
using PropFlow.Application;
using PropFlow.Infrastructure.Identity;

namespace PropFlow.Api;

// PF-S01.06: self-service device/session list and single-session revoke, additive alongside the
// existing "sign out everywhere" POST /api/auth/logout (SessionAuthentication.LogoutAsync, which
// this does not change). Scoped to the calling user's own sessions in the current organization -
// not an admin console for revoking someone else's device.
public static class SessionManagementEndpoints
{
    public static void MapSessionManagementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sessions").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, UserSessionService sessions, CancellationToken ct) =>
        {
            var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var organizationId = TenantAccess.Resolve(principal);
            var currentSessionId = Guid.TryParse(principal.FindFirstValue(TenantAccess.SessionClaim), out var id) ? id : (Guid?)null;
            var active = await sessions.ListActiveAsync(userId, organizationId, ct);
            return Results.Ok(active.Select(session => new
            {
                session.Id,
                session.CreatedAt,
                session.LastSeenAt,
                session.UserAgent,
                session.IpAddress,
                IsCurrent = session.Id == currentSessionId
            }));
        });

        group.MapDelete("/{sessionId:guid}", async (Guid sessionId, ClaimsPrincipal principal,
            UserSessionService sessions, CancellationToken ct) =>
        {
            var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var organizationId = TenantAccess.Resolve(principal);
            var outcome = await sessions.RevokeAsync(userId, organizationId, sessionId, ct);
            return outcome switch
            {
                UserSessionService.RevokeOutcome.Revoked => Results.NoContent(),
                UserSessionService.RevokeOutcome.NotFound => Results.NotFound(),
                _ => Results.Problem(statusCode: 500, title: "Unexpected outcome")
            };
        });
    }
}
