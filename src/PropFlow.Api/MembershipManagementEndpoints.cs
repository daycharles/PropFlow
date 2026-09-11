using System.Security.Claims;
using PropFlow.Application;
using PropFlow.Infrastructure.Identity;

namespace PropFlow.Api;

// PF-S01.07: list active memberships, change a member's role, and remove (deactivate) a member.
// Identity.ManageMembers, same gate as invitations, the role matrix, and teams.
public static class MembershipManagementEndpoints
{
    public static void MapMembershipManagementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/organizations/current/members").RequireAuthorization(Capabilities.ManageMembers);

        group.MapGet("/", async (ITenantContext tenant, MembershipManagementService memberships, CancellationToken ct) =>
            Results.Ok((await memberships.ListActiveAsync(tenant.OrganizationId, ct))
                .Select(m => new { m.UserId, m.Role, m.EmployeeId, m.VendorId })));

        group.MapPut("/{userId:guid}/role", async (Guid userId, ChangeRoleRequest request, ITenantContext tenant,
            ClaimsPrincipal user, MembershipManagementService memberships, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Role)) return Results.Problem(statusCode: 400, title: "A role is required");
            var actorUserId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var outcome = await memberships.ChangeRoleAsync(tenant.OrganizationId, userId, request.Role, actorUserId, ct);
            return outcome switch
            {
                MembershipManagementService.ChangeRoleOutcome.Applied => Results.NoContent(),
                MembershipManagementService.ChangeRoleOutcome.NotFound => Results.NotFound(),
                MembershipManagementService.ChangeRoleOutcome.WouldLockOutMembership => Results.Problem(statusCode: 409,
                    title: "This change would leave no active member of the organization able to manage members"),
                _ => Results.Problem(statusCode: 500, title: "Unexpected outcome")
            };
        });

        group.MapDelete("/{userId:guid}", async (Guid userId, ITenantContext tenant,
            ClaimsPrincipal user, MembershipManagementService memberships, CancellationToken ct) =>
        {
            var actorUserId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var outcome = await memberships.RemoveAsync(tenant.OrganizationId, userId, actorUserId, ct);
            return outcome switch
            {
                MembershipManagementService.RemoveOutcome.Applied => Results.NoContent(),
                MembershipManagementService.RemoveOutcome.NotFound => Results.NotFound(),
                MembershipManagementService.RemoveOutcome.WouldLockOutMembership => Results.Problem(statusCode: 409,
                    title: "This would leave no active member of the organization able to manage members"),
                _ => Results.Problem(statusCode: 500, title: "Unexpected outcome")
            };
        });
    }
}

public sealed record ChangeRoleRequest(string Role);
