using PropFlow.Domain.Work;

namespace PropFlow.Application.Work;

public enum AssignmentOutcome { Updated, Unchanged, NotFound, Conflict, NotAssignable }
public enum WorkWriteOutcome { Updated, NotFound, Conflict }
public enum BulkWorkAction { Status, Priority, Schedule, Note, Reopen }
public sealed record WorkListQuery(string? Search, Guid? CategoryId, WorkStatus? Status, WorkPriority? Priority, Guid? PropertyId, Guid? SpaceId, string? Sort, bool Descending, int Page, int PageSize, WorkAccessScope? AccessScope = null, WorkScopeSubject? ScopeSubject = null);
public sealed record WorkListPage(IReadOnlyList<WorkListItem> Items, int TotalCount, int Page, int PageSize);
public sealed record CreateWorkCommand(string Title, Guid PropertyId, Guid ActorId, string? Description = null, WorkType WorkType = WorkType.WorkOrder, Guid? CategoryId = null, WorkPriority Priority = WorkPriority.Normal, Guid? BuildingId = null, Guid? SpaceId = null, Guid? ResidentId = null, DateTimeOffset? DueDate = null, decimal? Cost = null, string? InternalNotes = null, string? ResidentVisibleNotes = null);
public sealed record UpdateWorkCommand(string Title, string? Description, Guid? CategoryId, WorkPriority Priority, Guid PropertyId, Guid? BuildingId, Guid? SpaceId, Guid? ResidentId, DateTimeOffset? DueDate, decimal? Cost, string? InternalNotes, string? ResidentVisibleNotes, WorkStatus? Status, DateTimeOffset? ScheduledStart, DateTimeOffset? ScheduledEnd, uint Version, Guid ActorId);
public sealed record BulkWorkItemRef(Guid WorkId, uint Version);
/// <summary>All-or-nothing bulk result: <paramref name="Changed"/> plus <paramref name="Unchanged"/> equals <paramref name="Total"/> whenever the batch committed.</summary>
public sealed record BulkAssignmentSummary(AssignmentOutcome Outcome, int Changed, int Unchanged, int Total);

/// <summary>
/// A bounded, all-or-nothing bulk edit. Exactly one of the action's parameters is read:
/// <see cref="BulkWorkAction.Status"/>→<see cref="Status"/>, <see cref="BulkWorkAction.Priority"/>→<see cref="Priority"/>,
/// <see cref="BulkWorkAction.Schedule"/>→<see cref="ScheduledStart"/>/<see cref="ScheduledEnd"/>,
/// <see cref="BulkWorkAction.Note"/>→<see cref="Note"/>/<see cref="NoteInternal"/>, <see cref="BulkWorkAction.Reopen"/>→none.
/// </summary>
public sealed record BulkWorkCommand(
    BulkWorkAction Action,
    IReadOnlyList<BulkWorkItemRef> Items,
    Guid ActorId,
    WorkStatus? Status = null,
    WorkPriority? Priority = null,
    DateTimeOffset? ScheduledStart = null,
    DateTimeOffset? ScheduledEnd = null,
    string? Note = null,
    bool NoteInternal = true);

public interface IWorkOperations
{
    Task<WorkListPage> ListAsync(WorkListQuery query, CancellationToken cancellationToken);
    Task<WorkItem?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<uint?> VersionAsync(Guid id, CancellationToken cancellationToken);
    /// <param name="residentVisibleOnly">Returns only entries deliberately marked safe for resident display.</param>
    Task<IReadOnlyList<TimelineItem>?> TimelineAsync(Guid id, bool residentVisibleOnly, CancellationToken cancellationToken);
    Task<WorkItem> CreateAsync(CreateWorkCommand command, CancellationToken cancellationToken);
    Task<WorkWriteOutcome> UpdateAsync(Guid id, UpdateWorkCommand command, CancellationToken cancellationToken);
    Task<AssignmentOutcome> AssignVendorAsync(Guid workId, Guid vendorId, Guid actorId, uint? version, CancellationToken cancellationToken);
    Task<AssignmentOutcome> AssignEmployeeAsync(Guid workId, Guid employeeId, Guid actorId, uint? version, CancellationToken cancellationToken);
    Task<BulkAssignmentSummary> BulkAssignVendorAsync(IReadOnlyList<BulkWorkItemRef> assignments, Guid vendorId, Guid actorId, CancellationToken cancellationToken);
    Task<BulkAssignmentSummary> BulkAssignEmployeeAsync(IReadOnlyList<BulkWorkItemRef> assignments, Guid employeeId, Guid actorId, CancellationToken cancellationToken);
    Task<BulkAssignmentSummary> BulkApplyAsync(BulkWorkCommand command, CancellationToken cancellationToken);
}
