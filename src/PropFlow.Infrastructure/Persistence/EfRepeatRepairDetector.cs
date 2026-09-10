using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Assets;
using PropFlow.Domain.Assets;

namespace PropFlow.Infrastructure.Persistence;

public sealed class EfRepeatRepairDetector(OperationsStore store, TimeProvider clock) : IRepeatRepairDetector
{
    public async Task<RepeatRepairPolicyView> GetPolicyAsync(CancellationToken cancellationToken)
    {
        var policy = await store.RepeatRepairPolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return policy is null
            ? new RepeatRepairPolicyView(RepeatRepairPolicy.DefaultThreshold, RepeatRepairPolicy.DefaultWindowDays, false)
            : new RepeatRepairPolicyView(policy.RepairThreshold, policy.WindowDays, policy.MatchByCategory);
    }

    public async Task<RepeatRepairPolicyView> SetPolicyAsync(int repairThreshold, int windowDays, bool matchByCategory, CancellationToken cancellationToken)
    {
        var policy = await store.RepeatRepairPolicies.FirstOrDefaultAsync(cancellationToken);
        if (policy is null)
        {
            policy = new RepeatRepairPolicy(store.OrganizationId, Guid.NewGuid());
            store.RepeatRepairPolicies.Add(policy);
        }
        policy.Configure(repairThreshold, windowDays, matchByCategory);
        await store.SaveChangesAsync(cancellationToken);
        return new RepeatRepairPolicyView(policy.RepairThreshold, policy.WindowDays, policy.MatchByCategory);
    }

    public async Task<RepeatRepairAssessment?> AssessAsync(Guid assetId, Guid? categoryId, CancellationToken cancellationToken)
    {
        var asset = await store.Assets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assetId, cancellationToken);
        if (asset is null) return null;

        var (threshold, windowDays, matchByCategory) = await ResolvePolicyAsync(cancellationToken);

        var since = clock.GetUtcNow().AddDays(-windowDays);
        var window = store.WorkItems.AsNoTracking().Where(w => w.AssetId == assetId && w.CreatedAt >= since);
        if (matchByCategory && categoryId is { } category)
            window = window.Where(w => w.CategoryId == category);

        var rollup = await window
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Cost = g.Sum(w => (decimal?)w.Cost) })
            .SingleOrDefaultAsync(cancellationToken);
        var count = rollup?.Count ?? 0;
        var age = asset.AgeInYears(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.Date));

        return new RepeatRepairAssessment(threshold, windowDays, matchByCategory, count, since,
            rollup?.Cost ?? 0m, age, count >= threshold);
    }

    public async Task<IReadOnlySet<Guid>> RepeatRepairAssetIdsAsync(CancellationToken cancellationToken)
    {
        var (threshold, windowDays, _) = await ResolvePolicyAsync(cancellationToken);
        var since = clock.GetUtcNow().AddDays(-windowDays);
        var ids = await store.WorkItems.AsNoTracking()
            .Where(w => w.AssetId != null && w.CreatedAt >= since)
            .GroupBy(w => w.AssetId!.Value)
            .Where(g => g.Count() >= threshold)
            .Select(g => g.Key)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    private async Task<(int Threshold, int WindowDays, bool MatchByCategory)> ResolvePolicyAsync(CancellationToken cancellationToken)
    {
        var policy = await store.RepeatRepairPolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return (policy?.RepairThreshold ?? RepeatRepairPolicy.DefaultThreshold,
            policy?.WindowDays ?? RepeatRepairPolicy.DefaultWindowDays,
            policy?.MatchByCategory ?? false);
    }
}
