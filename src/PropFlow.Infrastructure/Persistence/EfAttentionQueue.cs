using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Attention;
using PropFlow.Domain.Assets;
using PropFlow.Domain.Attention;
using PropFlow.Domain.Work;

namespace PropFlow.Infrastructure.Persistence;

// Builds the attention queue in three tenant-scoped queries — the open work rows, the last
// timeline activity per work, and the assets currently over the repeat-repair threshold — then
// runs the pure AttentionRules over each row. Every query rides the EF tenant filter and RLS.
public sealed class EfAttentionQueue(OperationsStore store, TimeProvider clock) : IAttentionQueue
{
    public async Task<AttentionQueue> BuildAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var policy = await store.RepeatRepairPolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var threshold = policy?.RepairThreshold ?? RepeatRepairPolicy.DefaultThreshold;
        var windowStart = now.AddDays(-(policy?.WindowDays ?? RepeatRepairPolicy.DefaultWindowDays));
        // The queue flags any asset over the raw repair count; the policy's category-similarity
        // option only narrows the per-asset assessment surface, not this sweep.
        var repeatRepairAssets = (await store.WorkItems.AsNoTracking()
            .Where(w => w.AssetId != null && w.CreatedAt >= windowStart)
            .GroupBy(w => w.AssetId!.Value)
            .Select(g => new { AssetId = g.Key, Count = g.Count() })
            .Where(x => x.Count >= threshold)
            .Select(x => x.AssetId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var lastActivity = (await store.Timeline.AsNoTracking()
            .Where(t => t.WorkId != null)
            .GroupBy(t => t.WorkId!.Value)
            .Select(g => new { WorkId = g.Key, At = g.Max(t => t.OccurredAt) })
            .ToListAsync(cancellationToken)).ToDictionary(x => x.WorkId, x => x.At);

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

            foreach (var finding in AttentionRules.Evaluate(snapshot, now))
                items.Add(new AttentionItem(row.Id, row.Title, row.PropertyId, row.PropertyName,
                    row.Status, row.Priority, row.DueDate, finding.Reason, finding.Severity, finding.Detail));
        }

        var ordered = items
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.DueDate ?? DateTimeOffset.MaxValue)
            .ThenBy(i => i.WorkId)
            .ThenBy(i => i.Reason)
            .ToList();

        int Distinct(AttentionSeverity severity) =>
            ordered.Where(i => i.Severity == severity).Select(i => i.WorkId).Distinct().Count();

        return new AttentionQueue(ordered,
            Distinct(AttentionSeverity.Critical),
            Distinct(AttentionSeverity.Warning),
            Distinct(AttentionSeverity.Informational));
    }
}
