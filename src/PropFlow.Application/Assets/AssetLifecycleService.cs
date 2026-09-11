using PropFlow.Domain.Assets;

namespace PropFlow.Application.Assets;

public sealed record AssetLifecycleRollup(decimal TotalCost, decimal MaintenanceCost, decimal RepairCost,
    decimal CapitalImprovementCost, decimal ReplacementCost, int CostCount);

public sealed record ReplacementCandidate(Guid AssetId, DateOnly? ReplacementDueOn, decimal? ReplacementCostEstimate,
    decimal TotalLifecycleCost, bool IsDue, bool IsWarrantyExpiring, bool IsEndOfLife);

public static class AssetLifecycleService
{
    public static AssetLifecycleRollup RollUp(IEnumerable<AssetLifecycleCost> costs)
    {
        var rows = costs.ToList();
        decimal Sum(AssetCostType type) => rows.Where(x => x.Type == type).Sum(x => x.Amount);
        var maintenance = Sum(AssetCostType.Maintenance);
        var repair = Sum(AssetCostType.Repair);
        var capital = Sum(AssetCostType.CapitalImprovement);
        var replacement = Sum(AssetCostType.Replacement);
        return new AssetLifecycleRollup(maintenance + repair + capital + replacement, maintenance, repair,
            capital, replacement, rows.Count);
    }

    public static ReplacementCandidate PlanReplacement(Asset asset, DateOnly asOf, int warrantyAlertDays,
        IEnumerable<AssetLifecycleCost> costs)
    {
        if (warrantyAlertDays < 0) throw new ArgumentOutOfRangeException(nameof(warrantyAlertDays));
        var rollup = RollUp(costs);
        return new ReplacementCandidate(asset.Id, asset.ReplacementDueOn(), asset.ReplacementCostEstimate,
            rollup.TotalCost, asset.IsReplacementDue(asOf), asset.IsWarrantyExpiringWithin(asOf, warrantyAlertDays),
            asset.Condition == AssetCondition.EndOfLife);
    }
}
