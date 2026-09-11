using System.Security.Claims;
using PropFlow.Application;
using PropFlow.Infrastructure.Identity;

namespace PropFlow.Api;

// PF-S01.03: create/list organization invitations (Identity.ManageMembers) and accept one
// (anonymous — the acceptor has no session yet).
public static class InvitationEndpoints
{
    public static void MapInvitationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/organizations/current/invitations").RequireAuthorization(Capabilities.ManageMembers);
        group.MapGet("/", async (ClaimsPrincipal user, InvitationService invitations, CancellationToken ct) =>
            Results.Ok(await invitations.ListPendingAsync(TenantAccess.Resolve(user), ct)));
        group.MapPost("/", async (InvitationRequest request, ClaimsPrincipal user, InvitationService invitations, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254)
                return Results.Problem(statusCode: 400, title: "A valid email is required");
            if (string.IsNullOrWhiteSpace(request.Role))
                return Results.Problem(statusCode: 400, title: "A role is required");
            var invitedBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var invitation = await invitations.CreateAsync(TenantAccess.Resolve(user), request.Email, request.Role, invitedBy, ct);
            // The token is only ever returned here, at creation - it is not re-readable from the
            // pending-invitations list, matching how a password or API key is shown exactly once.
            return Results.Created($"/api/organizations/current/invitations/{invitation.Id}", new
            {
                invitation.Id,
                invitation.Email,
                invitation.Role,
                invitation.ExpiresAt,
                invitation.Token
            });
        });

        app.MapPost("/api/invitations/{token}/accept", async (string token, AcceptInvitationRequest request, InvitationService invitations, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024)
                return Results.Problem(statusCode: 400, title: "A password is required");
            var (outcome, errors) = await invitations.AcceptAsync(token, request.Password, ct);
            return outcome switch
            {
                InvitationService.AcceptOutcome.Accepted => Results.NoContent(),
                InvitationService.AcceptOutcome.NotFound => Results.Problem(statusCode: 404, title: "Invitation not found"),
                InvitationService.AcceptOutcome.Expired => Results.Problem(statusCode: 410, title: "Invitation has expired"),
                InvitationService.AcceptOutcome.AlreadyAccepted => Results.Problem(statusCode: 409, title: "Invitation was already accepted"),
                InvitationService.AcceptOutcome.WeakPassword => Results.Problem(statusCode: 400, title: "Password does not meet requirements", extensions: new Dictionary<string, object?> { ["errors"] = errors }),
                _ => Results.Problem(statusCode: 500, title: "Unexpected outcome")
            };
        }).AllowAnonymous().RequireRateLimiting("login");
    }
}

public sealed record InvitationRequest(string Email, string Role);
public sealed record AcceptInvitationRequest(string Password);
