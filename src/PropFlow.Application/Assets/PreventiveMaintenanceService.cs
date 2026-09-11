using PropFlow.Domain.Assets;

namespace PropFlow.Application.Assets;

public sealed record PreventiveWorkCandidate(Guid PlanId, Guid AssetId, int Occurrence,
    DateOnly DueOn, string OccurrenceKey, string Title);

public interface IPreventiveMaintenancePlanSource
{
    Task<IReadOnlyList<PreventiveMaintenancePlan>> ActivePlansAsync(DateOnly through, CancellationToken cancellationToken);
}

public interface IPreventiveWorkOccurrenceSink
{
    Task<bool> TryCreateAsync(PreventiveWorkCandidate candidate, CancellationToken cancellationToken);
}

public interface IPreventiveMaintenanceGenerator
{
    Task<int> GenerateThroughAsync(DateOnly through, CancellationToken cancellationToken);
}

public sealed class PreventiveMaintenanceService(IPreventiveMaintenancePlanSource plans,
    IPreventiveWorkOccurrenceSink occurrences) : IPreventiveMaintenanceGenerator
{
    public async Task<int> GenerateThroughAsync(DateOnly through, CancellationToken cancellationToken)
    {
        if (through == default) throw new ArgumentException("A generation date is required.", nameof(through));
        var created = 0;
        foreach (var plan in await plans.ActivePlansAsync(through, cancellationToken))
        {
            foreach (var occurrence in plan.DueOccurrencesThrough(through))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = new PreventiveWorkCandidate(plan.Id, plan.AssetId, occurrence.Occurrence,
                    occurrence.DueOn, occurrence.OccurrenceKey, plan.Name);
                if (await occurrences.TryCreateAsync(candidate, cancellationToken)) created++;
            }
        }
        return created;
    }
}
