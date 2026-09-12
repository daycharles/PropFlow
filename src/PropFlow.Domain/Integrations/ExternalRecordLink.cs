namespace PropFlow.Domain.Integrations;

// One external record, as seen through a connection, and the PropFlow row it reconciles into.
//
// TWO HASHES, AND WHY THEY ARE DIFFERENT
//
//   ContentHash    - the content of the record as LAST SEEN from the source system.
//   ReconciledHash - the content that was last SUCCESSFULLY WRITTEN into PropFlow.
//
// They are equal when the PropFlow row is up to date, and different whenever the source has moved
// on or a reconciliation did not finish. NeedsReconciliation is exactly that comparison, and it is
// the whole replay-safety mechanism. Collapsing the two into one field looks like a simplification
// and is not: a single hash must be written either before the domain write - and then a crash
// loses the change forever - or after it, which is what this pair already is.
//
// WHY A CRASH BETWEEN THE TWO WRITES IS SAFE
//
// Writes are ordered DOMAIN-FIRST, HASH-LAST. The reconciler writes the Property / Space /
// Occupancy / WorkItem / Asset through OperationsStore, commits, and only then calls Reconcile()
// and commits that through IntegrationStore. Those are two DbContexts on two connections, and
// this codebase has no distributed transaction anywhere (verified 2026-09-12: every
// BeginTransactionAsync in src/, tests/ and tools/ is on a single context's Database, or on one
// raw Npgsql connection in DatabaseProvisioner; there is no TransactionScope and no enlistment),
// so the pair cannot be made atomic without introducing one.
//
// It does not need to be. A crash after the domain write and before the hash write leaves
// ReconciledHash stale, so the next run sees NeedsReconciliation, re-decides "Update", and
// re-applies the SAME mapped values to the SAME InternalId. The second application is a no-op on
// the data and the run converges. The failure mode of the opposite order - hash first - is a
// permanently lost change that no later run can detect.
//
// This is deliberately the same argument the repo already accepted for post-commit outbox
// dispatch (docs/followups.md), with one improvement: there, a lost dispatch is gone; here the
// replay path actually exists and runs on every sync. Do not "fix" this into a transaction that
// spans both contexts, and do not move Reconcile() ahead of the domain write.
public sealed class ExternalRecordLink : TenantEntity
{
    // 256, not 200: SyntheticExternalId.ForResident appends a suffix to a source id, and a source
    // id that already used the full width would otherwise become unstorable.
    public const int ExternalIdMaxLength = 256;
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

    // The content of the external record as last seen. Set by Observe.
    public string? ContentHash { get; private set; }

    // The content that was last successfully reconciled into PropFlow. Set by Reconcile, and
    // deliberately NOT by Observe - see the two-hashes note above.
    public string? ReconciledHash { get; private set; }

    // The PropFlow row this external record maps to, and the reconciliation idempotency key: a
    // replayed run updates that row rather than creating a second one. Null until the first
    // successful reconciliation.
    public Guid? InternalId { get; private set; }

    public SyncState SyncState { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }

    // The sync run that last touched this link, so the sync-health view can answer "what did run
    // X do" without a separate per-record journal.
    public Guid? LastRunId { get; private set; }
    public DateTimeOffset? LastReconciledAt { get; private set; }
    public string? LastError { get; private set; }

    // When the link was retired, and by whom if a person did it rather than the sweep. Both stay
    // set after a later Observe brings the link back, so "this was retired once" survives.
    public DateTimeOffset? RetiredAt { get; private set; }
    public Guid? RetiredByUserId { get; private set; }

    // True when the PropFlow side is behind the source side, including on a first sighting. The
    // reconciler's create-or-update decision reads this and nothing else.
    public bool NeedsReconciliation => ReconciledHash is null || ReconciledHash != ContentHash;

    // A pull saw this record cleanly. Returns true when the content changed since last time
    // (or this is the first sighting) so the caller can count "updated".
    public bool Observe(string contentHash, DateTimeOffset now, Guid? runId = null)
    {
        var hash = IntegrationConnection.RequireSingleLine(contentHash, nameof(contentHash), ContentHashMaxLength);
        var changed = ContentHash != hash;
        ContentHash = hash;
        SyncState = SyncState.Synced;
        LastSeenAt = now;
        LastError = null;
        RecordRun(runId);
        return changed;
    }

    public void MarkFailed(string error, DateTimeOffset now, Guid? runId = null)
    {
        SyncState = SyncState.Failed;
        LastSeenAt = now;
        LastError = IntegrationConnection.RequireSingleLine(error, nameof(error), ErrorMaxLength);
        RecordRun(runId);
    }

    // Reconciliation raised a divergence a human has to settle. The link keeps its InternalId and
    // its ReconciledHash: the PropFlow row is still whatever the last good run left it as, and the
    // Conflict row carries what the source now says.
    public void MarkConflicted(string reason, DateTimeOffset now, Guid? runId = null)
    {
        SyncState = SyncState.Conflicted;
        LastSeenAt = now;
        LastError = IntegrationConnection.RequireSingleLine(reason, nameof(reason), ErrorMaxLength);
        RecordRun(runId);
    }

    /// <summary>
    /// The PropFlow row was successfully created or updated from this record. Called AFTER the
    /// domain write commits, never before - see the header note.
    /// </summary>
    public void Reconcile(Guid internalId, string reconciledHash, Guid runId, DateTimeOffset now)
    {
        if (internalId == Guid.Empty) throw new ArgumentException("Internal id is required.", nameof(internalId));
        if (runId == Guid.Empty) throw new ArgumentException("Run is required.", nameof(runId));
        if (InternalId is { } existing && existing != internalId)
            throw new InvalidOperationException(
                "This external record is already linked to a different PropFlow row; rebinding would orphan the first.");

        InternalId = internalId;
        ReconciledHash = IntegrationConnection.RequireSingleLine(
            reconciledHash, nameof(reconciledHash), ContentHashMaxLength);
        LastRunId = runId;
        LastReconciledAt = now;
        LastSeenAt ??= now;
        SyncState = SyncState.Synced;
        LastError = null;
    }

    /// <summary>
    /// The source system stopped reporting this record: PF-S19.05's retirement sweep calls this
    /// with the run that noticed. The link is retired, NOT deleted, and the PropFlow row it points
    /// at is left alone - an upstream disappearance is not authority to delete a tenant's work
    /// order. The sweep raises a conflict alongside this so a human decides what the PropFlow row
    /// should become. A later run that sees the record again calls Observe, which puts the link
    /// back to Synced with its InternalId intact.
    /// </summary>
    public void Retire(Guid runId, DateTimeOffset now)
    {
        if (runId == Guid.Empty) throw new ArgumentException("Run is required.", nameof(runId));
        RetireCore(now);
        LastRunId = runId;
    }

    /// <summary>
    /// An operator retired the link by hand through the API (PF-S19.08). There is no run to
    /// attribute it to, so the actor is recorded instead; LastRunId is left pointing at whichever
    /// run last touched the record, which is the honest answer.
    /// </summary>
    public void RetireManually(Guid actorId, DateTimeOffset now)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        RetireCore(now);
        RetiredByUserId = actorId;
    }

    private void RetireCore(DateTimeOffset now)
    {
        SyncState = SyncState.Retired;
        RetiredAt = now.ToUniversalTime();
        LastSeenAt ??= now;
    }

    private void RecordRun(Guid? runId)
    {
        if (runId is { } run && run != Guid.Empty) LastRunId = run;
    }
}
