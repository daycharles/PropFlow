using PropFlow.Domain.Automation;

namespace PropFlow.Application.Automation;

/// <summary>What one evaluator run did — matched a condition set, applied it, or failed.</summary>
public sealed record AutomationRunSummary(int RulesMatched, int RulesApplied, int RulesFailed)
{
    public static AutomationRunSummary Empty { get; } = new(0, 0, 0);
}

/// <summary>
/// Evaluates the current tenant's enabled automation rules for one work-event occurrence and
/// executes the matching ones. Called after the triggering work change has committed;
/// <paramref name="occurrenceId"/> is the id of that event's timeline entry, so a repeated run
/// for the same occurrence is a no-op.
/// </summary>
public interface IAutomationEngine
{
    Task<AutomationRunSummary> RunAsync(AutomationTrigger trigger, Guid workId, Guid occurrenceId, CancellationToken cancellationToken);
}
