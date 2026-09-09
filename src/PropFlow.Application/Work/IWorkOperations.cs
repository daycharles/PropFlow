using PropFlow.Domain.Timeline;
using PropFlow.Domain.Work;

namespace PropFlow.Application.Work;

public enum AssignmentOutcome { Updated, Unchanged, NotFound, Conflict }
public enum WorkWriteOutcome { Updated, NotFound, Conflict }
public sealed record WorkListQuery(string? Search, Guid? CategoryId, WorkStatus? Status, WorkPriority? Priority, Guid? PropertyId, Guid? SpaceId, string? Sort, bool Descending, int Page, int PageSize);
public sealed record WorkListPage(IReadOnlyList<WorkItem> Items, int TotalCount, int Page, int PageSize);
public sealed record CreateWorkCommand(string Title, Guid PropertyId, Guid ActorId, string? Description = null, WorkType WorkType = WorkType.WorkOrder, Guid? CategoryId = null, WorkPriority Priority = WorkPriority.Normal, Guid? BuildingId = null, Guid? SpaceId = null, Guid? ResidentId = null, DateTimeOffset? DueDate = null, decimal? Cost = null, string? InternalNotes = null, string? ResidentVisibleNotes = null);
public sealed record UpdateWorkCommand(string Title, string? Description, Guid? CategoryId, WorkPriority Priority, Guid PropertyId, Guid? BuildingId, Guid? SpaceId, Guid? ResidentId, DateTimeOffset? DueDate, decimal? Cost, string? InternalNotes, string? ResidentVisibleNotes, WorkStatus? Status, DateTimeOffset? ScheduledStart, DateTimeOffset? ScheduledEnd, uint Version, Guid ActorId);
public sealed record BulkVendorAssignment(Guid WorkId, uint Version);

public interface IWorkOperations
{
    Task<WorkListPage> ListAsync(WorkListQuery query, CancellationToken cancellationToken);
    Task<WorkItem?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<uint?> VersionAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<TimelineEntry>?> TimelineAsync(Guid id, CancellationToken cancellationToken);
    Task<WorkItem> CreateAsync(CreateWorkCommand command, CancellationToken cancellationToken);
    Task<WorkWriteOutcome> UpdateAsync(Guid id, UpdateWorkCommand command, CancellationToken cancellationToken);
    Task<AssignmentOutcome> AssignVendorAsync(Guid workId, Guid vendorId, Guid actorId, uint? version, CancellationToken cancellationToken);
    Task<AssignmentOutcome> BulkAssignVendorAsync(IReadOnlyList<BulkVendorAssignment> assignments, Guid vendorId, Guid actorId, CancellationToken cancellationToken);
}
