using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Work;
using PropFlow.Domain.Timeline;
using PropFlow.Domain.Work;

namespace PropFlow.Infrastructure.Persistence;

public sealed class EfWorkOperations(OperationsStore store, TimeProvider clock) : IWorkOperations
{
    public async Task<IReadOnlyList<WorkItem>> ListAsync(CancellationToken cancellationToken) =>
        await store.WorkItems.AsNoTracking().OrderBy(x => x.Title).ThenBy(x => x.Id).Take(100).ToListAsync(cancellationToken);

    public Task<WorkItem?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        store.WorkItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TimelineEntry>?> TimelineAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await store.WorkItems.AnyAsync(x => x.Id == id, cancellationToken)) return null;
        return await store.Timeline.AsNoTracking().Where(x => x.WorkId == id)
            .OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }

    public async Task<AssignmentOutcome> AssignVendorAsync(Guid workId, Guid vendorId, Guid actorId, CancellationToken cancellationToken)
    {
        var work = await store.WorkItems.SingleOrDefaultAsync(x => x.Id == workId, cancellationToken);
        if (work is null || !await store.Vendors.AnyAsync(x => x.Id == vendorId, cancellationToken))
            return AssignmentOutcome.NotFound;
        var change = work.AssignVendor(vendorId, actorId, clock.GetUtcNow());
        if (change is null) return AssignmentOutcome.Unchanged;
        store.Timeline.Add(TimelineEntry.From(change));
        // A single SaveChanges transaction commits the assignment and its history atomically.
        await store.SaveChangesAsync(cancellationToken);
        return AssignmentOutcome.Updated;
    }
}
