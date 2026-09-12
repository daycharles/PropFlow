using PropFlow.Domain.Integrations;

namespace PropFlow.Application.Integrations;

// The administration surface behind a connection: the conflict queue, the mapping configuration,
// the run history, and manual retirement. Separate from IIntegrationOperations, which owns the
// connection lifecycle and the sync itself — these are the screens an operator works in between
// syncs, not the sync path.
//
// Outcomes are enums, never exceptions (.claude/rules/architecture.md): the two domain calls that
// would throw — resolving a closed conflict, and promoting a profile that still has errors — are
// pre-checked here and reported as outcomes so the endpoint can answer 409 rather than letting an
// InvalidOperationException escape into the global handler's 500 bucket.

public enum ConflictWriteOutcome { Updated, NotFound, AlreadyClosed }
public enum MappingWriteOutcome { Created, Updated, NotFound, NotApplicable }
public enum MappingPromotionOutcome { Promoted, NotFound, HasErrors }
public enum RecordRetireOutcome { Retired, NotFound, AlreadyRetired }

// A conflict as the queue shows it. A flat projection rather than the entity, so the HTTP contract
// does not move every time the aggregate does.
public sealed record ConflictView(
    Guid Id,
    Guid ConnectionId,
    IntegrationEntityKind Kind,
    string ExternalId,
    ConflictReason Reason,
    string Field,
    string? ObservedValue,
    string? CurrentValue,
    string? Detail,
    ConflictStatus Status,
    Guid FirstSeenInRunId,
    Guid LastSeenInRunId,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int ObservationCount,
    Guid? ResolvedByUserId,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote,
    // Whether the connection's most recent COMPLETED run re-detected this divergence.
    //
    // The reconciler re-observes a conflict it still sees and simply stops observing one it does
    // not; it never closes a row on its own, because "I no longer detect it" and "a human decided"
    // are different facts and only the second belongs in ResolvedByUserId. So a divergence that has
    // actually been fixed leaves its row sitting Open, and after fix → promote → sync the queue
    // would otherwise still look full.
    //
    // False here means "the last run did not see this any more" — the signal a UI needs to offer
    // "close the ones that look fixed", and the signal an acceptance test needs to prove the fix
    // worked. Always true when the connection has never completed a run.
    bool SeenInLatestRun);

// OpenCount is the whole connection's open total, not the page's — it is the badge the health
// screen shows, and paging through the queue must not change it. StaleCount is the subset of those
// the latest completed run no longer detects.
public sealed record ConflictsPage(
    IReadOnlyList<ConflictView> Items,
    int TotalCount,
    int OpenCount,
    int StaleCount,
    int Page,
    int PageSize);

public sealed record ConflictFilter(
    ConflictStatus? Status = null,
    IntegrationEntityKind? Kind = null,
    ConflictReason? Reason = null);

public sealed record MappingRuleView(Guid Id, MappingSourceField SourceField, string SourceValue, string TargetValue);

// Issues come from MappingProfile.Validate(), which is pure — so the mapping panel can show an
// operator exactly what is wrong without touching the source system, and CanAutoApply is the same
// answer the promote endpoint will give.
public sealed record MappingProfileView(
    Guid Id,
    Guid ConnectionId,
    IntegrationEntityKind Kind,
    MappingMode Mode,
    Guid? TargetPortfolioId,
    Guid? DefaultCreatorId,
    string? DefaultTimeZoneId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<MappingRuleView> Rules,
    IReadOnlyList<MappingIssue> Issues,
    bool CanAutoApply,
    IReadOnlyList<MappingSourceField> AvailableSourceFields);

public sealed record SyncRunView(
    Guid Id,
    Guid ConnectionId,
    SyncTrigger Trigger,
    int AttemptNumber,
    SyncRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset HeartbeatAt,
    DateTimeOffset? CompletedAt,
    int Seen,
    int Added,
    int Updated,
    int Failed,
    int Conflicted,
    string? Error,
    string? SnapshotHash);

public sealed record SyncRunsPage(IReadOnlyList<SyncRunView> Items, int TotalCount, int Page, int PageSize);

public sealed record SaveMappingProfileCommand(
    Guid? TargetPortfolioId,
    Guid? DefaultCreatorId,
    string? DefaultTimeZoneId);

public sealed record SaveMappingRuleCommand(MappingSourceField SourceField, string SourceValue, string TargetValue);

public interface IIntegrationAdministration
{
    // Null when the connection does not exist, so the endpoint answers 404 rather than an empty page.
    Task<ConflictsPage?> ConflictsAsync(Guid connectionId, ConflictFilter filter, int page, int pageSize,
        CancellationToken cancellationToken);

    // `ignore` picks Conflict.Ignore over Conflict.Resolve. Both are status transitions; there is
    // no delete path, and the runtime role has no DELETE on the table to make one with.
    Task<ConflictWriteOutcome> CloseConflictAsync(Guid connectionId, Guid conflictId, Guid actorId,
        string? note, bool ignore, CancellationToken cancellationToken);

    Task<IReadOnlyList<MappingProfileView>?> MappingProfilesAsync(Guid connectionId, CancellationToken cancellationToken);

    // Keyed on (connection, kind), which is the unique index, so this is an upsert rather than a
    // create that can collide.
    Task<(MappingWriteOutcome Outcome, Guid Id)> SaveMappingProfileAsync(Guid connectionId,
        IntegrationEntityKind kind, SaveMappingProfileCommand command, CancellationToken cancellationToken);

    Task<(MappingWriteOutcome Outcome, Guid Id)> AddMappingRuleAsync(Guid connectionId, IntegrationEntityKind kind,
        SaveMappingRuleCommand command, CancellationToken cancellationToken);

    Task<MappingWriteOutcome> RemoveMappingRuleAsync(Guid connectionId, IntegrationEntityKind kind, Guid ruleId,
        CancellationToken cancellationToken);

    // Issues are returned on refusal AND on success (empty or informational only), so the caller
    // always has the current picture.
    Task<(MappingPromotionOutcome Outcome, IReadOnlyList<MappingIssue> Issues)> PromoteAsync(Guid connectionId,
        IntegrationEntityKind kind, CancellationToken cancellationToken);

    Task<MappingWriteOutcome> RevertToReportOnlyAsync(Guid connectionId, IntegrationEntityKind kind,
        CancellationToken cancellationToken);

    Task<SyncRunsPage?> RunsAsync(Guid connectionId, int page, int pageSize, CancellationToken cancellationToken);

    Task<SyncRunView?> RunAsync(Guid connectionId, Guid runId, CancellationToken cancellationToken);

    // The operator-invoked retirement. There is no sync run to attribute it to, which is why
    // ExternalRecordLink carries RetiredByUserId alongside RetiredAt.
    Task<RecordRetireOutcome> RetireRecordAsync(Guid connectionId, Guid recordId, Guid actorId,
        CancellationToken cancellationToken);
}
