using PropFlow.Domain.Integrations;

namespace PropFlow.Application.Integrations;

public enum IntegrationWriteOutcome { Created, Updated, DuplicateSource, UnknownSource, NotFound }
public enum SyncOutcome { Completed, Failed, NotFound, Disabled, AlreadyRunning }

// Health snapshot for one connection — everything the Integration Health screen (PF-6.11) shows.
public sealed record IntegrationConnectionHealth(
    Guid Id,
    string SourceSystem,
    string DisplayName,
    bool IsEnabled,
    DateTimeOffset? LastAttemptedAt,
    DateTimeOffset? LastSucceededAt,
    int ConsecutiveFailures,
    string? LastError,
    int TrackedRecords,
    int FailedRecords,
    // The unresolved-conflict badge PF-6.11 asked for and never got (docs/followups.md). Without
    // it the health list cannot tell a connection syncing cleanly from one that has raised the same
    // unmapped-status conflict on every run for a week: both show a recent LastSucceededAt and zero
    // failures, because a conflict is deliberately not a failure.
    int OpenConflicts = 0);

// Result of a single sync run against a connection's adapter.
//
// The counters are about PROPFLOW ROWS, not about external record links, and that changed in
// PF-S19.05. Before it a sync only tracked external ids, so "added" meant "external ids we had not
// seen before" and `Failed` was hard-coded 0 because there was no mapping step that could fail
// (docs/followups.md recorded both). Now that a sync actually reconciles:
//
//   Seen       - canonical records in the snapshot.
//   Added      - PropFlow rows created.
//   Updated    - PropFlow rows updated.
//   Failed     - records whose mapped values a domain invariant refused.
//   Conflicted - records that raised a conflict, upstream disappearances included.
//   Retired    - links the source stopped reporting. The PropFlow rows are left alone.
//
// A connection with no mapping profile reports Added 0 and a non-zero Conflicted on its first
// sync. That is the intended connect → sync → review → promote → sync flow, not a failure.
// RunId identifies the SyncRun row carrying timings and the snapshot hash.
public sealed record SyncReport(
    SyncOutcome Outcome,
    int Seen,
    int Added,
    int Updated,
    int Failed,
    string? Error,
    int Conflicted = 0,
    int Retired = 0,
    Guid? RunId = null)
{
    public static SyncReport NotFound { get; } = new(SyncOutcome.NotFound, 0, 0, 0, 0, null);
    public static SyncReport Disabled { get; } = new(SyncOutcome.Disabled, 0, 0, 0, 0, null);

    // Another dispatcher already holds this connection's run claim.
    public static SyncReport AlreadyRunning { get; } = new(SyncOutcome.AlreadyRunning, 0, 0, 0, 0, null);
}

public sealed record CreateConnectionCommand(string SourceSystem, string DisplayName);

// One page of a connection's tracked external records, newest sighting first.
public sealed record RecordsPage(IReadOnlyList<ExternalRecordLink> Items, int TotalCount, int Page, int PageSize);

public interface IIntegrationOperations
{
    Task<IReadOnlyList<IntegrationConnectionHealth>> ListAsync(CancellationToken cancellationToken);
    Task<IntegrationConnectionHealth?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<(IntegrationWriteOutcome Outcome, Guid Id)> CreateAsync(CreateConnectionCommand command, CancellationToken cancellationToken);
    Task<IntegrationWriteOutcome> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken);
    Task<SyncReport> SyncAsync(Guid id, CancellationToken cancellationToken);

    // A page of the external records a connection is tracking, newest sighting first — feeds the
    // per-connection detail view and lets tests assert what a pull recorded. Null when the
    // connection does not exist.
    Task<RecordsPage?> RecordsAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken);
}
