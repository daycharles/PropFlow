using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Assets;
using PropFlow.Domain.Assets;
using PropFlow.Domain.Timeline;
using PropFlow.Domain.Work;

namespace PropFlow.Infrastructure.Persistence;

public sealed class EfPreventiveMaintenancePlanSource(OperationsStore store) : IPreventiveMaintenancePlanSource
{
    public async Task<IReadOnlyList<PreventiveMaintenancePlan>> ActivePlansAsync(DateOnly through, CancellationToken ct) =>
        await store.PreventiveMaintenancePlans
            .Where(x => x.IsActive && x.FirstDueOn <= through)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
}

/// <summary>
/// Creates the generated work item and its idempotency record in one database transaction.
/// The unique occurrence key makes concurrent scheduler/API runs safe.
/// </summary>
public sealed class EfPreventiveWorkOccurrenceSink(OperationsStore store, TimeProvider clock) : IPreventiveWorkOccurrenceSink
{
    public async Task<bool> TryCreateAsync(PreventiveWorkCandidate candidate, CancellationToken ct)
    {
        if (await store.PreventiveMaintenanceOccurrences.AnyAsync(x => x.OccurrenceKey == candidate.OccurrenceKey, ct))
            return false;

        var asset = await store.Assets.SingleOrDefaultAsync(x => x.Id == candidate.AssetId, ct)
            ?? throw new KeyNotFoundException("Asset not found.");
        var now = clock.GetUtcNow();
        var workId = Guid.NewGuid();
        var actorId = store.OrganizationId; // system actor; no user FK is required by WorkItem
        var work = new WorkItem(store.OrganizationId, workId, candidate.Title, asset.PropertyId, actorId,
            WorkType.PreventiveMaintenance);
        work.SetAsset(asset.Id);
        work.SetDueDate(new DateTimeOffset(candidate.DueOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        work.Publish(now);
        var timeline = TimelineEntry.Record(store.OrganizationId, actorId, now, "PreventiveMaintenanceGenerated",
            "PreventiveMaintenanceOccurrence", workId, null, candidate.OccurrenceKey, workId,
            $"{{\"occurrenceKey\":\"{candidate.OccurrenceKey}\",\"dueOn\":\"{candidate.DueOn:yyyy-MM-dd}\"}}");
        var occurrence = new PreventiveMaintenanceOccurrence(store.OrganizationId, Guid.NewGuid(), candidate.PlanId,
            candidate.AssetId, candidate.OccurrenceKey, candidate.DueOn, workId, now);

        await using var transaction = await store.Database.BeginTransactionAsync(ct);
        try
        {
            store.WorkItems.Add(work);
            store.Timeline.Add(timeline);
            store.PreventiveMaintenanceOccurrences.Add(occurrence);
            await store.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            store.ChangeTracker.Clear();
            return false;
        }
    }
}
