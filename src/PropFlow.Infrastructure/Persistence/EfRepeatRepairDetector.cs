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
        if (!await store.Assets.AsNoTracking().AnyAsync(x => x.Id == assetId, cancellationToken)) return null;

        var policy = await store.RepeatRepairPolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var threshold = policy?.RepairThreshold ?? RepeatRepairPolicy.DefaultThreshold;
        var windowDays = policy?.WindowDays ?? RepeatRepairPolicy.DefaultWindowDays;
        var matchByCategory = policy?.MatchByCategory ?? false;

        var since = clock.GetUtcNow().AddDays(-windowDays);
        var window = store.WorkItems.AsNoTracking().Where(w => w.AssetId == assetId && w.CreatedAt >= since);
        if (matchByCategory && categoryId is { } category)
            window = window.Where(w => w.CategoryId == category);

        var rollup = await window
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Cost = g.Sum(w => (decimal?)w.Cost) })
            .SingleOrDefaultAsync(cancellationToken);
        var count = rollup?.Count ?? 0;

        return new RepeatRepairAssessment(threshold, windowDays, matchByCategory, count, since,
            rollup?.Cost ?? 0m, count >= threshold);
    }
}
