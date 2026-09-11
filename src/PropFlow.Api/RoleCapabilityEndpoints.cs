using System.Security.Claims;
using PropFlow.Application;
using PropFlow.Infrastructure.Identity;

namespace PropFlow.Api;

// PF-S01.04: the role-matrix admin surface - view/edit each role's capabilities relative to the
// Capabilities.ForRole defaults. All routes require Identity.ManageMembers, same as invitations.
public static class RoleCapabilityEndpoints
{
    public static void MapRoleCapabilityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/organizations/current/roles").RequireAuthorization(Capabilities.ManageMembers);

        group.MapGet("/", async (ITenantContext tenant, RoleCapabilityService roleCapabilities, CancellationToken ct) =>
        {
            var overrides = (await roleCapabilities.ListOverridesAsync(tenant.OrganizationId, ct)).ToLookup(x => x.Role);
            var rows = new List<object>();
            foreach (var role in Roles.All)
            {
                var forRole = overrides[role].ToArray();
                rows.Add(new
                {
                    Role = role,
                    Defaults = Capabilities.ForRole(role),
                    Grants = forRole.Where(x => !x.IsRevocation).Select(x => x.Capability).ToArray(),
                    Revocations = forRole.Where(x => x.IsRevocation).Select(x => x.Capability).ToArray(),
                    Effective = await roleCapabilities.EffectiveCapabilitiesAsync(tenant.OrganizationId, role, null, null, ct)
                });
            }
            return Results.Ok(rows);
        });

        group.MapPut("/{role}/capabilities", async (string role, RoleCapabilityRequest request,
            ITenantContext tenant, ClaimsPrincipal user, RoleCapabilityService roleCapabilities, CancellationToken ct) =>
        {
            if (!Roles.All.Contains(role)) return Results.Problem(statusCode: 404, title: "Unknown role");
            var actorUserId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var outcome = await roleCapabilities.SetOverridesAsync(
                tenant.OrganizationId, role, request.Grants ?? [], request.Revocations ?? [], actorUserId, ct);
            return outcome switch
            {
                RoleCapabilityService.SetOutcome.Applied => Results.Ok(
                    await roleCapabilities.EffectiveCapabilitiesAsync(tenant.OrganizationId, role, null, null, ct)),
                RoleCapabilityService.SetOutcome.WouldLockOutMembership => Results.Problem(statusCode: 409,
                    title: "This change would leave no active member of the organization able to manage members"),
                _ => Results.Problem(statusCode: 500, title: "Unexpected outcome")
            };
        });
    }
}

public sealed record RoleCapabilityRequest(string[]? Grants, string[]? Revocations);
