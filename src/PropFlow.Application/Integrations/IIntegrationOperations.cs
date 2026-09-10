using PropFlow.Domain.Integrations;

namespace PropFlow.Application.Integrations;

public enum IntegrationWriteOutcome { Created, Updated, DuplicateSource, UnknownSource, NotFound }
public enum SyncOutcome { Completed, Failed, NotFound, Disabled }

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
    int FailedRecords);

// Result of a single sync run against a connection's adapter.
public sealed record SyncReport(
    SyncOutcome Outcome,
    int Seen,
    int Added,
    int Updated,
    int Failed,
    string? Error)
{
    public static SyncReport NotFound { get; } = new(SyncOutcome.NotFound, 0, 0, 0, 0, null);
    public static SyncReport Disabled { get; } = new(SyncOutcome.Disabled, 0, 0, 0, 0, null);
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
