using Microsoft.EntityFrameworkCore;

namespace PropFlow.Infrastructure.Identity;

// PF-S01.07: append-only identity/membership audit trail, separate from the Work module's
// TimelineEntry (tenant business data in OperationsStore) since this is control-plane data in
// IdentityStore. Writers call RecordAsync as part of the same unit of work as the change itself
// (it adds to the tracked context but does not call SaveChangesAsync - the caller's own save
// persists both together, so a record is never written for a change that failed to commit).
public sealed class IdentityAuditLog(IdentityStore store, TimeProvider clock)
{
    public static class EventTypes
    {
        public const string InvitationSent = "InvitationSent";
        public const string InvitationAccepted = "InvitationAccepted";
        public const string MembershipRoleChanged = "MembershipRoleChanged";
        public const string MembershipRemoved = "MembershipRemoved";
        public const string RoleCapabilitiesChanged = "RoleCapabilitiesChanged";
    }

    public void Record(Guid organizationId, string eventType, Guid? actorUserId, Guid? targetUserId,
        string? targetLabel, string? oldValue, string? newValue) =>
        store.AuditEntries.Add(new IdentityAuditEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            OccurredAt = clock.GetUtcNow(),
            EventType = eventType,
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            TargetLabel = targetLabel,
            OldValue = Truncate(oldValue),
            NewValue = Truncate(newValue),
        });

    public Task<List<IdentityAuditEntry>> ListAsync(Guid organizationId, CancellationToken ct, int limit = 200) =>
        store.AuditEntries.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.OccurredAt)
            .Take(limit)
            .ToListAsync(ct);

    private static string? Truncate(string? value) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= 4000 ? value : value[..4000];
}
