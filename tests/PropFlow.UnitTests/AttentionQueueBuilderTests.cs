using PropFlow.Application.Attention;
using PropFlow.Domain.Attention;
using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class AttentionQueueBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static AttentionItem Item(
        string title,
        AttentionFinding[] findings,
        WorkPriority priority = WorkPriority.Normal,
        DateTimeOffset? dueDate = null) =>
        new(Guid.NewGuid(), title, Guid.NewGuid(), "Harbor Point Apartments",
            WorkStatus.New, priority, dueDate, findings);

    private static AttentionFinding Finding(AttentionReason reason, AttentionSeverity severity) =>
        new(reason, severity, $"{reason} at {severity}.");

    /// <summary>
    /// The defect this pins (D3, 2026-09-10): the /attention cards read one number and the
    /// filtered list read another, because the counts were of distinct work items while the rows
    /// were one per tripped rule. Both sides now use <see cref="AttentionItem.HasSeverity"/>.
    /// </summary>
    [Fact]
    public void Each_severity_count_equals_the_rows_that_severity_filter_shows()
    {
        var queue = AttentionQueueBuilder.Build([
            // The seeded gas-odor shape: one work item tripping three rules, two of them Critical.
            Item("Gas odor investigation", [
                Finding(AttentionReason.UnassignedEmergency, AttentionSeverity.Critical),
                Finding(AttentionReason.Overdue, AttentionSeverity.Critical),
                Finding(AttentionReason.RepeatRepair, AttentionSeverity.Warning),
            ], WorkPriority.Critical, Now.AddDays(-2)),
            Item("No heat in 4B", [Finding(AttentionReason.SlaBreach, AttentionSeverity.Critical)], WorkPriority.High),
            Item("Repaint hallway", [Finding(AttentionReason.Overdue, AttentionSeverity.Warning)], dueDate: Now.AddDays(-1)),
            Item("Awaiting part", [
                Finding(AttentionReason.WaitingOnVendor, AttentionSeverity.Warning),
                Finding(AttentionReason.WaitingOnResident, AttentionSeverity.Informational),
            ]),
        ]);

        foreach (var severity in Enum.GetValues<AttentionSeverity>())
        {
            var count = severity switch
            {
                AttentionSeverity.Critical => queue.CriticalCount,
                AttentionSeverity.Warning => queue.WarningCount,
                _ => queue.InformationalCount,
            };
            var shown = queue.Items.Count(i => i.HasSeverity(severity));
            Assert.Equal(shown, count);
        }

        // And the numbers are the ones a property manager would act on: work items, not rule hits.
        Assert.Equal(4, queue.Items.Count);
        Assert.Equal(2, queue.CriticalCount);
        Assert.Equal(3, queue.WarningCount);
        Assert.Equal(1, queue.InformationalCount);
    }

    [Fact]
    public void A_work_item_appears_once_however_many_rules_it_trips()
    {
        var queue = AttentionQueueBuilder.Build([
            Item("Gas odor investigation", [
                Finding(AttentionReason.UnassignedEmergency, AttentionSeverity.Critical),
                Finding(AttentionReason.SlaBreach, AttentionSeverity.Critical),
                Finding(AttentionReason.Overdue, AttentionSeverity.Critical),
            ], WorkPriority.Critical, Now.AddDays(-3)),
        ]);

        var item = Assert.Single(queue.Items);
        Assert.Equal(3, item.Findings.Count);
        Assert.Equal(1, queue.CriticalCount);
    }

    [Fact]
    public void An_item_reads_at_its_most_urgent_severity_and_its_findings_lead_with_it()
    {
        var queue = AttentionQueueBuilder.Build([
            Item("Awaiting part", [
                Finding(AttentionReason.WaitingOnResident, AttentionSeverity.Informational),
                Finding(AttentionReason.RepeatRepair, AttentionSeverity.Warning),
            ]),
        ]);

        var item = Assert.Single(queue.Items);
        Assert.Equal(AttentionSeverity.Warning, item.Severity);
        Assert.Equal(AttentionReason.RepeatRepair, item.Findings[0].Reason);
    }

    [Fact]
    public void Items_are_ordered_most_urgent_then_soonest_due()
    {
        var queue = AttentionQueueBuilder.Build([
            Item("Repaint hallway", [Finding(AttentionReason.Overdue, AttentionSeverity.Warning)], dueDate: Now.AddDays(-1)),
            Item("Waiting on the resident", [Finding(AttentionReason.WaitingOnResident, AttentionSeverity.Informational)]),
            Item("Burst pipe", [Finding(AttentionReason.UnassignedEmergency, AttentionSeverity.Critical)], WorkPriority.Critical),
            Item("Gas odor investigation", [Finding(AttentionReason.Overdue, AttentionSeverity.Critical)], WorkPriority.Critical, Now.AddDays(-2)),
        ]);

        Assert.Equal(
            ["Gas odor investigation", "Burst pipe", "Repaint hallway", "Waiting on the resident"],
            queue.Items.Select(i => i.Title));
    }

    [Fact]
    public void A_work_item_that_tripped_nothing_is_not_in_the_queue()
    {
        var queue = AttentionQueueBuilder.Build([Item("Routine filter change", [])]);

        Assert.Empty(queue.Items);
        Assert.Equal(0, queue.CriticalCount);
        Assert.Equal(0, queue.WarningCount);
        Assert.Equal(0, queue.InformationalCount);
    }
}
