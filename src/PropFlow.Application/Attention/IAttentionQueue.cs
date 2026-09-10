using PropFlow.Domain.Attention;
using PropFlow.Domain.Work;

namespace PropFlow.Application.Attention;

/// <summary>
/// One work item that needs a human's attention, carrying every rule it tripped. A work item
/// appears once no matter how many rules it trips — the queue is a list of work to act on, not
/// a list of rule hits, so the per-severity counts and the rows a severity filter shows are the
/// same set (see <see cref="AttentionQueueBuilder"/>).
/// </summary>
public sealed record AttentionItem(
    Guid WorkId,
    string Title,
    Guid PropertyId,
    string? PropertyName,
    WorkStatus Status,
    WorkPriority Priority,
    DateTimeOffset? DueDate,
    IReadOnlyList<AttentionFinding> Findings)
{
    /// <summary>The most urgent severity among <see cref="Findings"/>: how the row reads at a glance.</summary>
    public AttentionSeverity Severity => Findings.Max(f => f.Severity);

    /// <summary>
    /// Whether this work item has tripped a rule at <paramref name="severity"/>. This is the one
    /// predicate both the counts and the UI's severity filter use, so a work item flagged both
    /// Critical and Warning shows under each card and is counted in each.
    /// </summary>
    public bool HasSeverity(AttentionSeverity severity) => Findings.Any(f => f.Severity == severity);
}

/// <summary>
/// The attention queue: one entry per open work item that tripped a rule, most urgent first,
/// plus the per-severity counts of distinct work items.
/// </summary>
public sealed record AttentionQueue(
    IReadOnlyList<AttentionItem> Items,
    int CriticalCount,
    int WarningCount,
    int InformationalCount);

/// <summary>
/// Assembles the queue from the evaluated work items. Counts are derived from the very rows the
/// queue carries, using <see cref="AttentionItem.HasSeverity"/> — the same predicate the
/// <c>/attention</c> cards filter the list with — so a card can never disagree with the list
/// beneath it.
/// </summary>
public static class AttentionQueueBuilder
{
    public static AttentionQueue Build(IEnumerable<AttentionItem> items)
    {
        var ordered = items
            .Where(i => i.Findings.Count > 0)
            .Select(i => i with
            {
                Findings = i.Findings
                    .OrderByDescending(f => f.Severity)
                    .ThenBy(f => f.Reason)
                    .ToList(),
            })
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.DueDate ?? DateTimeOffset.MaxValue)
            .ThenBy(i => i.WorkId)
            .ToList();

        return new AttentionQueue(ordered,
            ordered.Count(i => i.HasSeverity(AttentionSeverity.Critical)),
            ordered.Count(i => i.HasSeverity(AttentionSeverity.Warning)),
            ordered.Count(i => i.HasSeverity(AttentionSeverity.Informational)));
    }
}

public interface IAttentionQueue
{
    Task<AttentionQueue> BuildAsync(CancellationToken cancellationToken);
}
