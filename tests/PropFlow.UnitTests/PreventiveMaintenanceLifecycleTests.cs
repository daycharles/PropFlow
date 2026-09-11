using PropFlow.Application.Assets;
using PropFlow.Domain.Assets;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class PreventiveMaintenanceLifecycleTests
{
    [Fact]
    public void Plan_returns_next_due_and_all_deterministic_occurrences()
    {
        var plan = new PreventiveMaintenancePlan(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Boiler service",
            MaintenanceRecurrence.Quarterly, new DateOnly(2026, 1, 31));

        Assert.Equal(new DateOnly(2026, 7, 31), plan.NextDueOn(new DateOnly(2026, 6, 1)));
        var occurrences = plan.DueOccurrencesThrough(new DateOnly(2026, 10, 31));
        Assert.Equal(4, occurrences.Count);
        Assert.Equal("pm:" + plan.Id.ToString("N") + ":3", occurrences[3].OccurrenceKey);
    }

    [Fact]
    public async Task Generator_is_idempotent_when_sink_deduplicates_occurrence_keys()
    {
        var plan = new PreventiveMaintenancePlan(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Filter change",
            MaintenanceRecurrence.Monthly, new DateOnly(2026, 1, 1));
        var sink = new InMemorySink();
        var service = new PreventiveMaintenanceService(new Plans(plan), sink);

        Assert.Equal(3, await service.GenerateThroughAsync(new DateOnly(2026, 3, 1), CancellationToken.None));
        Assert.Equal(0, await service.GenerateThroughAsync(new DateOnly(2026, 3, 1), CancellationToken.None));
        Assert.Equal(3, sink.Keys.Count);
    }

    [Fact]
    public void Lifecycle_rollup_and_replacement_plan_include_warranty_and_end_of_life_signals()
    {
        var asset = new Asset(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, AssetKind.Hvac, "Unit 1");
        asset.SetLifecycle(new DateOnly(2020, 6, 1), new DateOnly(2026, 7, 1), 5);
        asset.RecordCondition(AssetCondition.EndOfLife);
        asset.SetReplacementCost(12000.125m);
        var costs = new[]
        {
            new AssetLifecycleCost(asset.OrganizationId, Guid.NewGuid(), asset.Id, AssetCostType.Repair, 100.126m, new DateOnly(2025, 1, 1), "Repair"),
            new AssetLifecycleCost(asset.OrganizationId, Guid.NewGuid(), asset.Id, AssetCostType.Maintenance, 50m, new DateOnly(2025, 2, 1), "Service")
        };

        var candidate = AssetLifecycleService.PlanReplacement(asset, new DateOnly(2026, 6, 15), 30, costs);
        Assert.Equal(150.13m, candidate.TotalLifecycleCost);
        Assert.True(candidate.IsWarrantyExpiring);
        Assert.True(candidate.IsEndOfLife);
        Assert.True(candidate.IsDue);
        Assert.Equal(12000.12m, candidate.ReplacementCostEstimate);
    }

    private sealed class Plans(PreventiveMaintenancePlan plan) : IPreventiveMaintenancePlanSource
    {
        public Task<IReadOnlyList<PreventiveMaintenancePlan>> ActivePlansAsync(DateOnly through, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PreventiveMaintenancePlan>>([plan]);
    }

    private sealed class InMemorySink : IPreventiveWorkOccurrenceSink
    {
        public HashSet<string> Keys { get; } = [];
        public Task<bool> TryCreateAsync(PreventiveWorkCandidate candidate, CancellationToken cancellationToken) => Task.FromResult(Keys.Add(candidate.OccurrenceKey));
    }
}
