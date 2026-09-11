using Microsoft.EntityFrameworkCore;
using PropFlow.Application;

namespace PropFlow.Infrastructure.Identity;

// PF-S01.07: the write half of organization membership - MembershipAccess stays read-only.
// Changing a role or removing a member are the two membership changes the audit trail
// (docs/full-suite-scope.md) calls out that had no endpoint to produce them yet.
public sealed class MembershipManagementService(IdentityStore store, RoleCapabilityService roleCapabilities, IdentityAuditLog audit)
{
    public enum ChangeRoleOutcome { Applied, NotFound, WouldLockOutMembership }
    public enum RemoveOutcome { Applied, NotFound, WouldLockOutMembership }

    public Task<List<OrganizationMembership>> ListActiveAsync(Guid organizationId, CancellationToken ct) =>
        store.Memberships.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.IsActive)
            .OrderBy(x => x.Role)
            .ToListAsync(ct);

    public async Task<ChangeRoleOutcome> ChangeRoleAsync(Guid organizationId, Guid userId, string newRole, Guid actorUserId, CancellationToken ct)
    {
        var membership = await store.Memberships.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.UserId == userId && x.IsActive, ct);
        if (membership is null) return ChangeRoleOutcome.NotFound;
        var previousRole = membership.Role;
        if (previousRole == newRole) return ChangeRoleOutcome.Applied;

        if (!await RetainsAManagerAsync(organizationId, excludeUserId: userId,
                hypotheticalRole: newRole, membership.EmployeeId, membership.VendorId, ct))
            return ChangeRoleOutcome.WouldLockOutMembership;

        membership.Role = newRole;
        audit.Record(organizationId, IdentityAuditLog.EventTypes.MembershipRoleChanged, actorUserId, userId, null, previousRole, newRole);
        await store.SaveChangesAsync(ct);
        return ChangeRoleOutcome.Applied;
    }

    // A "removed" member is deactivated, not deleted - matching every other IsActive-flagged
    // record in this module (Organization, OrganizationMembership itself already had the flag,
    // Team). The membership row, and now the audit trail referencing it, survive.
    public async Task<RemoveOutcome> RemoveAsync(Guid organizationId, Guid userId, Guid actorUserId, CancellationToken ct)
    {
        var membership = await store.Memberships.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.UserId == userId && x.IsActive, ct);
        if (membership is null) return RemoveOutcome.NotFound;

        if (!await RetainsAManagerAsync(organizationId, excludeUserId: userId, hypotheticalRole: null, null, null, ct))
            return RemoveOutcome.WouldLockOutMembership;

        var previousRole = membership.Role;
        membership.IsActive = false;
        audit.Record(organizationId, IdentityAuditLog.EventTypes.MembershipRemoved, actorUserId, userId, null, previousRole, null);
        await store.SaveChangesAsync(ct);
        return RemoveOutcome.Applied;
    }

    // True if the organization would still be safely administrable after excluding excludeUserId's
    // current membership and substituting hypotheticalRole for it (null = removed entirely): either
    // no active memberships remain at all (nothing to lock out of), or at least one still carries
    // Capabilities.ManageMembers. Mirrors RoleCapabilityService.SetOverridesAsync's own guard.
    private async Task<bool> RetainsAManagerAsync(Guid organizationId, Guid excludeUserId, string? hypotheticalRole,
        Guid? hypotheticalEmployeeId, Guid? hypotheticalVendorId, CancellationToken ct)
    {
        var others = await store.Memberships.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.IsActive && x.UserId != excludeUserId)
            .ToListAsync(ct);

        var wouldRetainAManager = false;
        foreach (var other in others)
        {
            var effective = await roleCapabilities.EffectiveCapabilitiesAsync(organizationId, other.Role, other.EmployeeId, other.VendorId, ct);
            if (effective.Contains(Capabilities.ManageMembers)) { wouldRetainAManager = true; break; }
        }
        if (!wouldRetainAManager && hypotheticalRole is not null)
        {
            var effective = await roleCapabilities.EffectiveCapabilitiesAsync(organizationId, hypotheticalRole, hypotheticalEmployeeId, hypotheticalVendorId, ct);
            wouldRetainAManager = effective.Contains(Capabilities.ManageMembers);
        }
        var resultingMembershipCount = others.Count + (hypotheticalRole is not null ? 1 : 0);
        return resultingMembershipCount == 0 || wouldRetainAManager;
    }
}
