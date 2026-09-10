using PropFlow.Domain.Attention;
using PropFlow.Domain.Work;

namespace PropFlow.Application.Attention;

/// <summary>
/// One thing that needs a human's attention: a work item paired with the rule it tripped. A
/// work item that trips several rules produces several items.
/// </summary>
public sealed record AttentionItem(
    Guid WorkId,
    string Title,
    Guid PropertyId,
    string? PropertyName,
    WorkStatus Status,
    WorkPriority Priority,
    DateTimeOffset? DueDate,
    AttentionReason Reason,
    AttentionSeverity Severity,
    string Detail);

/// <summary>
/// The attention queue: every finding, most urgent first, plus the per-severity counts of
/// distinct work items (a work item flagged Critical and Warning counts once in each).
/// </summary>
public sealed record AttentionQueue(
    IReadOnlyList<AttentionItem> Items,
    int CriticalCount,
    int WarningCount,
    int InformationalCount);

public interface IAttentionQueue
{
    Task<AttentionQueue> BuildAsync(CancellationToken cancellationToken);
}
