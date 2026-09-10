using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Assets;
using PropFlow.Application.Attention;
using PropFlow.Domain.Attention;
using PropFlow.Domain.Work;

namespace PropFlow.Infrastructure.Persistence;

// Builds the attention queue in three tenant-scoped queries — the open work rows, the last
// timeline activity for those rows, and the assets currently over the repeat-repair threshold —
// then runs the pure AttentionRules over each row. Every query rides the EF tenant filter and RLS.
public sealed class EfAttentionQueue(OperationsStore store, IRepeatRepairDetector repeatRepair, TimeProvider clock) : IAttentionQueue
{
    public async Task<AttentionQueue> BuildAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        // The window/threshold live in the repeat-repair detector; this is the tenant-wide sweep
        // (the policy's category-similarity option is not applied here).
        var repeatRepairAssets = await repeatRepair.RepeatRepairAssetIdsAsync(cancellationToken);

        var rows = await (
            from w in store.WorkItems.AsNoTracking()
            where w.Status != WorkStatus.Completed && w.Status != WorkStatus.Cancelled && w.Status != WorkStatus.Draft
            join p in store.Properties on w.PropertyId equals p.Id into properties
            from p in properties.DefaultIfEmpty()
            select new
            {
                w.Id,
                w.Title,
                w.PropertyId,
                PropertyName = p == null ? null : p.Name,
                w.Status,
                w.Priority,
                w.WorkType,
                w.DueDate,
                w.CreatedAt,
                w.VendorId,
                w.EmployeeId,
                w.ResidentId,
                w.AssetId,
            }).ToListAsync(cancellationToken);

        var openIds = rows.Select(r => r.Id).ToHashSet();
        var lastActivity = (await store.Timeline.AsNoTracking()
            .Where(t => t.WorkId != null && openIds.Contains(t.WorkId!.Value))
            .GroupBy(t => t.WorkId!.Value)
            .Select(g => new { WorkId = g.Key, At = g.Max(t => t.OccurredAt) })
            .ToListAsync(cancellationToken)).ToDictionary(x => x.WorkId, x => x.At);

        var items = new List<AttentionItem>();
        foreach (var row in rows)
        {
            var snapshot = new WorkAttentionSnapshot(
                row.Status,
                row.Priority,
                row.WorkType,
                row.VendorId is not null,
                row.EmployeeId is not null,
                row.ResidentId is not null,
                row.AssetId is { } assetId && repeatRepairAssets.Contains(assetId),
                row.CreatedAt,
                lastActivity.TryGetValue(row.Id, out var at) ? at : row.CreatedAt,
                row.DueDate);

            // One entry per work item, carrying every rule it tripped — the builder derives the
            // per-severity counts from these same rows, so the cards cannot outrun the list.
            var findings = AttentionRules.Evaluate(snapshot, now);
            if (findings.Count == 0) continue;

            items.Add(new AttentionItem(row.Id, row.Title, row.PropertyId, row.PropertyName,
                row.Status, row.Priority, row.DueDate, findings));
        }

        return AttentionQueueBuilder.Build(items);
    }
}
