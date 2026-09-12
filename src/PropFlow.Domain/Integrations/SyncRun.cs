namespace PropFlow.Domain.Integrations;

public enum SyncRunStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2
}

// What started the run. The scheduler (PF-S19.06) needs to tell an operator's "Sync now" from its
// own tick and from an outage retry, both in the sync-health view and when deciding whether to
// back off.
public enum SyncTrigger
{
    Manual = 0,
    Scheduled = 1,
    Retry = 2
}

// The five counters a run reports. Passed as one value so a caller cannot silently shift Added
// into Updated by getting the argument order wrong.
public sealed record SyncCounts(int Seen, int Added, int Updated, int Failed, int Conflicted)
{
    public static SyncCounts Zero { get; } = new(0, 0, 0, 0, 0);

    public SyncCounts Validated()
    {
        if (Seen < 0 || Added < 0 || Updated < 0 || Failed < 0 || Conflicted < 0)
            throw new ArgumentOutOfRangeException(nameof(Seen), "Sync counters cannot be negative.");
        return this;
    }
}

// One attempt to pull and reconcile a connection: who started it, when, what it saw, and what it
// left behind.
//
// BEGIN INSERTS A RUNNING ROW AND IS SAVED BEFORE THE ADAPTER IS CONTACTED. This is the same
// claim-then-commit shape as OutboxProcessor.cs:38-48, and it is here for the same reason: two
// dispatchers must not both sync one connection. The caller inserts this row, saves, and only
// then calls PullAsync. The loser of the race gets a DbUpdateConcurrencyException on that first
// save, detaches the entity, and skips the connection; the winner proceeds. That requires a
// concurrency token on the row - PF-S19.04 adds the xmin shadow property the other contexts use
// (OperationsStore.cs:40, IntegrationStore.cs:33) - plus the partial unique index that makes a
// second Running row for the same connection impossible.
//
// Moving the save after the pull to "avoid" the exception removes the claim and makes concurrent
// runs possible. Do not. The exception is the mechanism, not an error.
//
// A run left Running by a crashed worker is reclaimable after StaleRunTimeout. IsReclaimable is
// pure, mirrors OutboxMessage.IsClaimable (OutboxMessage.cs:85-94), and uses HeartbeatAt rather
// than StartedAt so a long but progressing run is not reclaimed out from under itself.
public sealed class SyncRun : TenantEntity
{
    public const int ErrorMaxLength = 1000;
    public const int SnapshotHashMaxLength = ExternalRecordLink.ContentHashMaxLength;

    // The message a reclaimed run carries, so the sync-health view can tell a crash from a
    // provider failure without a second column.
    public const string ReclaimedError = "The run was reclaimed after exceeding the stale-run timeout.";

    // EF materialization.
    private SyncRun(Guid organizationId, Guid id) : base(organizationId, id) { }

    private SyncRun(Guid organizationId, Guid id, Guid connectionId, SyncTrigger trigger,
        DateTimeOffset startedAt, int attemptNumber) : base(organizationId, id)
    {
        if (connectionId == Guid.Empty) throw new ArgumentException("Connection is required.", nameof(connectionId));
        if (!Enum.IsDefined(trigger)) throw new ArgumentOutOfRangeException(nameof(trigger));
        if (attemptNumber < 1) throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempts start at 1.");
        ConnectionId = connectionId;
        Trigger = trigger;
        AttemptNumber = attemptNumber;
        Status = SyncRunStatus.Running;
        StartedAt = startedAt.ToUniversalTime();
        HeartbeatAt = StartedAt;
    }

    /// <summary>
    /// The Running row a caller inserts and saves BEFORE contacting the adapter. See the header.
    /// </summary>
    public static SyncRun Begin(Guid organizationId, Guid id, Guid connectionId, SyncTrigger trigger,
        DateTimeOffset startedAt, int attemptNumber = 1) =>
        new(organizationId, id, connectionId, trigger, startedAt, attemptNumber);

    public Guid ConnectionId { get; private set; }
    public SyncTrigger Trigger { get; private set; }
    public int AttemptNumber { get; private set; } = 1;
    public SyncRunStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }

    // Last sign of life. Equals StartedAt until the run reports progress; the reclaim predicate
    // reads this, not StartedAt.
    public DateTimeOffset HeartbeatAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public int Seen { get; private set; }
    public int Added { get; private set; }
    public int Updated { get; private set; }
    public int Failed { get; private set; }
    public int Conflicted { get; private set; }

    public string? Error { get; private set; }

    // CanonicalHash.Of(snapshot) over the whole IntegrationSnapshot. IntegrationSnapshot is itself
    // a record (CanonicalRecords.cs:55) and CanonicalHash.Of hashes the compiler-generated
    // ToString(), which includes every property including the nested record lists - so an
    // unchanged source system produces an identical hash and a run can be skipped or recognised as
    // a replay. Null on a run that never got a snapshot.
    public string? SnapshotHash { get; private set; }

    public SyncCounts Counts => new(Seen, Added, Updated, Failed, Conflicted);
    public bool IsFinished => Status is SyncRunStatus.Completed or SyncRunStatus.Failed;

    public void Heartbeat(DateTimeOffset now)
    {
        RequireRunning();
        var utc = now.ToUniversalTime();
        if (utc > HeartbeatAt) HeartbeatAt = utc;
    }

    public void Complete(SyncCounts counts, string? snapshotHash, DateTimeOffset completedAt)
    {
        RequireRunning();
        ApplyCounts(counts);
        SnapshotHash = string.IsNullOrWhiteSpace(snapshotHash)
            ? null
            : IntegrationConnection.RequireSingleLine(snapshotHash, nameof(snapshotHash), SnapshotHashMaxLength);
        Status = SyncRunStatus.Completed;
        Error = null;
        Finish(completedAt);
    }

    // A run can fail having already done work - the counters are kept, not zeroed, so a partial
    // run's effect is visible in the health view.
    public void Fail(string error, SyncCounts counts, DateTimeOffset completedAt)
    {
        RequireRunning();
        ApplyCounts(counts);
        Status = SyncRunStatus.Failed;
        Error = IntegrationConnection.RequireSingleLine(error, nameof(error), ErrorMaxLength);
        Finish(completedAt);
    }

    /// <summary>
    /// Pure. True when this run is still Running and has shown no sign of life for at least
    /// staleRunTimeout, so another dispatcher may write it off. The boundary is inclusive, exactly
    /// as OutboxMessage.IsClaimable is.
    /// </summary>
    public bool IsReclaimable(DateTimeOffset now, TimeSpan staleRunTimeout)
    {
        if (staleRunTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(staleRunTimeout), "The stale-run timeout cannot be negative.");
        return Status == SyncRunStatus.Running && HeartbeatAt <= now.ToUniversalTime() - staleRunTimeout;
    }

    public void Reclaim(DateTimeOffset now, TimeSpan staleRunTimeout)
    {
        if (!IsReclaimable(now, staleRunTimeout))
            throw new InvalidOperationException("The run is not reclaimable.");
        Fail(ReclaimedError, Counts, now);
    }

    private void ApplyCounts(SyncCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        counts.Validated();
        Seen = counts.Seen;
        Added = counts.Added;
        Updated = counts.Updated;
        Failed = counts.Failed;
        Conflicted = counts.Conflicted;
    }

    private void Finish(DateTimeOffset completedAt)
    {
        var utc = completedAt.ToUniversalTime();
        if (utc < StartedAt) throw new ArgumentException("A run cannot finish before it started.", nameof(completedAt));
        CompletedAt = utc;
        if (utc > HeartbeatAt) HeartbeatAt = utc;
    }

    private void RequireRunning()
    {
        if (Status != SyncRunStatus.Running)
            throw new InvalidOperationException($"The run is already {Status}.");
    }
}
