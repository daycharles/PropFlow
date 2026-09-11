using PropFlow.Application;
using PropFlow.Infrastructure.Identity;

namespace PropFlow.Api;

// PF-S01.07: read-only view of the identity/membership audit trail (invites sent/accepted, role
// changes, removals, role-capability overrides). Identity.ManageMembers, same gate as the other
// membership-administration surfaces - this is a companion to the Work module's own timeline
// (GET /api/work/{id}/timeline), not a replacement for it.
public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/api/organizations/current/audit", async (ITenantContext tenant, IdentityAuditLog audit, CancellationToken ct) =>
            Results.Ok((await audit.ListAsync(tenant.OrganizationId, ct)).Select(entry => new
            {
                entry.Id,
                entry.OccurredAt,
                entry.EventType,
                entry.ActorUserId,
                entry.TargetUserId,
                entry.TargetLabel,
                entry.OldValue,
                entry.NewValue
            }))).RequireAuthorization(Capabilities.ManageMembers);
    }
}
