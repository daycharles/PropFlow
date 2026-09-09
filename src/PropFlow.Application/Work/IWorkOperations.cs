using PropFlow.Domain.Timeline;
using PropFlow.Domain.Work;

namespace PropFlow.Application.Work;

public enum AssignmentOutcome { Updated, Unchanged, NotFound }

public interface IWorkOperations
{
    Task<IReadOnlyList<WorkItem>> ListAsync(CancellationToken cancellationToken);
    Task<WorkItem?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<TimelineEntry>?> TimelineAsync(Guid id, CancellationToken cancellationToken);
    Task<AssignmentOutcome> AssignVendorAsync(Guid workId, Guid vendorId, Guid actorId, CancellationToken cancellationToken);
}
