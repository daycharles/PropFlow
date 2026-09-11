using Microsoft.EntityFrameworkCore;
using PropFlow.Application;

namespace PropFlow.Infrastructure.Identity;

// PF-S01.04: reads/writes RoleCapabilityOverride rows and layers them onto Capabilities.ForRole
// via RoleCapabilityMatrix.Effective. An organization with no override rows sees exactly today's
// fixed behavior - this is additive, not a replacement of the default matrix.
public sealed class RoleCapabilityService(IdentityStore store)
{
    public async Task<IReadOnlyList<string>> EffectiveCapabilitiesAsync(
        Guid organizationId, string role, Guid? employeeId, Guid? vendorId, CancellationToken ct)
    {
        var overrides = await store.RoleCapabilityOverrides.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Role == role)
            .ToListAsync(ct);
        return RoleCapabilityMatrix.Effective(
            Capabilities.ForRole(role, employeeId, vendorId),
            overrides.Where(x => !x.IsRevocation).Select(x => x.Capability),
            overrides.Where(x => x.IsRevocation).Select(x => x.Capability));
    }

    public Task<List<RoleCapabilityOverride>> ListOverridesAsync(Guid organizationId, CancellationToken ct) =>
        store.RoleCapabilityOverrides.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Role).ThenBy(x => x.Capability)
            .ToListAsync(ct);

    public enum SetOutcome { Applied, WouldLockOutMembership }

    // Replaces every override row for (organizationId, role) with the given grants/revocations.
    // Refuses only when the result would leave no *currently occupied* role in this organization
    // able to manage members - not a check on every possible role, which would forbid an
    // org from ever running with a single-role structure that never touched ManageMembers.
    public async Task<SetOutcome> SetOverridesAsync(
        Guid organizationId, string role, IReadOnlyCollection<string> grants, IReadOnlyCollection<string> revocations, CancellationToken ct)
    {
        var occupiedRoles = await store.Memberships.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.IsActive)
            .Select(x => x.Role).Distinct().ToListAsync(ct);

        var wouldRetainAManager = false;
        foreach (var occupiedRole in occupiedRoles)
        {
            var isTheRoleBeingChanged = occupiedRole == role;
            var effective = isTheRoleBeingChanged
                ? RoleCapabilityMatrix.Effective(Capabilities.ForRole(occupiedRole), grants, revocations)
                : await EffectiveCapabilitiesAsync(organizationId, occupiedRole, null, null, ct);
            if (effective.Contains(Capabilities.ManageMembers)) { wouldRetainAManager = true; break; }
        }
        // An organization with no active members at all (mid-setup) has nothing to lock itself
        // out of yet; only refuse when removing management from a role that occupied members
        // actually rely on.
        if (occupiedRoles.Count > 0 && !wouldRetainAManager) return SetOutcome.WouldLockOutMembership;

        var existing = await store.RoleCapabilityOverrides
            .Where(x => x.OrganizationId == organizationId && x.Role == role).ToListAsync(ct);
        store.RoleCapabilityOverrides.RemoveRange(existing);
        foreach (var capability in grants)
            store.RoleCapabilityOverrides.Add(new RoleCapabilityOverride { OrganizationId = organizationId, Role = role, Capability = capability, IsRevocation = false });
        foreach (var capability in revocations)
            store.RoleCapabilityOverrides.Add(new RoleCapabilityOverride { OrganizationId = organizationId, Role = role, Capability = capability, IsRevocation = true });
        await store.SaveChangesAsync(ct);
        return SetOutcome.Applied;
    }
}
