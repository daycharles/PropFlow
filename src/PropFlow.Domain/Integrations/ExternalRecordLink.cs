namespace PropFlow.Domain.Integrations;

// One external record, as seen through a connection: its source id, a content hash so a
// re-pull can tell "unchanged" from "changed", and the sync state. The internal PropFlow row
// it maps to is intentionally NOT here yet — PF-6.10 tracks the external side only;
// reconciliation into Properties/Assets/WorkItems is a later task.
public sealed class ExternalRecordLink : TenantEntity
{
    public const int ExternalIdMaxLength = 200;
    public const int ContentHashMaxLength = 64;
    public const int ErrorMaxLength = 1000;

    // EF materialization.
    private ExternalRecordLink(Guid organizationId, Guid id) : base(organizationId, id) { }

    public ExternalRecordLink(Guid organizationId, Guid id, Guid connectionId,
        IntegrationEntityKind kind, string externalId) : base(organizationId, id)
    {
        if (connectionId == Guid.Empty) throw new ArgumentException("Connection is required.", nameof(connectionId));
        ConnectionId = connectionId;
        Kind = kind;
        ExternalId = IntegrationConnection.RequireSingleLine(externalId, nameof(externalId), ExternalIdMaxLength);
        SyncState = SyncState.Pending;
    }

    public Guid ConnectionId { get; private set; }
    public IntegrationEntityKind Kind { get; private set; }
    public string ExternalId { get; private set; } = "";
    public string? ContentHash { get; private set; }
    public SyncState SyncState { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }
    public string? LastError { get; private set; }

    // A pull saw this record cleanly. Returns true when the content changed since last time
    // (or this is the first sighting) so the caller can count "updated".
    public bool Observe(string contentHash, DateTimeOffset now)
    {
        var hash = IntegrationConnection.RequireSingleLine(contentHash, nameof(contentHash), ContentHashMaxLength);
        var changed = ContentHash != hash;
        ContentHash = hash;
        SyncState = SyncState.Synced;
        LastSeenAt = now;
        LastError = null;
        return changed;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        SyncState = SyncState.Failed;
        LastSeenAt = now;
        LastError = IntegrationConnection.RequireSingleLine(error, nameof(error), ErrorMaxLength);
    }
}
