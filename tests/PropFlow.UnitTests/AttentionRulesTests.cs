using PropFlow.Domain.Attention;
using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class AttentionRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static WorkAttentionSnapshot Work(
        WorkStatus status = WorkStatus.New,
        WorkPriority priority = WorkPriority.Normal,
        WorkType type = WorkType.WorkOrder,
        bool vendor = false,
        bool employee = false,
        bool resident = false,
        bool repeatRepair = false,
        double ageHours = 1,
        double lastActivityHoursAgo = 1,
        DateTimeOffset? dueDate = null) =>
        new(status, priority, type, vendor, employee, resident, repeatRepair,
            Now.AddHours(-ageHours), Now.AddHours(-lastActivityHoursAgo), dueDate);

    private static IEnumerable<AttentionReason> Reasons(WorkAttentionSnapshot work) =>
        AttentionRules.Evaluate(work, Now).Select(f => f.Reason);

    [Fact]
    public void Terminal_and_draft_work_never_appears()
    {
        foreach (var status in new[] { WorkStatus.Completed, WorkStatus.Cancelled, WorkStatus.Draft })
            Assert.Empty(AttentionRules.Evaluate(
                Work(status: status, priority: WorkPriority.Critical, ageHours: 999, dueDate: Now.AddDays(-10)), Now));
    }

    [Fact]
    public void A_critical_work_item_with_no_assignee_is_an_unassigned_emergency()
    {
        var findings = AttentionRules.Evaluate(Work(priority: WorkPriority.Critical, ageHours: 1), Now);
        var emergency = Assert.Single(findings, f => f.Reason == AttentionReason.UnassignedEmergency);
        Assert.Equal(AttentionSeverity.Critical, emergency.Severity);
    }

    [Fact]
    public void A_critical_work_item_with_a_vendor_is_not_an_unassigned_emergency()
    {
        Assert.DoesNotContain(AttentionReason.UnassignedEmergency,
            Reasons(Work(priority: WorkPriority.Critical, vendor: true)));
    }

    [Theory]
    [InlineData(WorkPriority.Critical, 3, false)]
    [InlineData(WorkPriority.Critical, 5, true)]
    [InlineData(WorkPriority.Normal, 71, false)]
    [InlineData(WorkPriority.Normal, 73, true)]
    public void Sla_breach_fires_once_past_the_first_response_budget(WorkPriority priority, double ageHours, bool expected)
    {
        var hit = Reasons(Work(priority: priority, ageHours: ageHours)).Contains(AttentionReason.SlaBreach);
        Assert.Equal(expected, hit);
    }

    [Fact]
    public void Sla_breach_is_critical_for_high_priority_and_a_warning_for_normal()
    {
        Assert.Equal(AttentionSeverity.Critical,
            AttentionRules.Evaluate(Work(priority: WorkPriority.High, ageHours: 48), Now)
                .Single(f => f.Reason == AttentionReason.SlaBreach).Severity);
        Assert.Equal(AttentionSeverity.Warning,
            AttentionRules.Evaluate(Work(priority: WorkPriority.Normal, ageHours: 100), Now)
                .Single(f => f.Reason == AttentionReason.SlaBreach).Severity);
    }

    [Fact]
    public void Sla_breach_only_applies_while_the_work_is_still_new()
    {
        Assert.DoesNotContain(AttentionReason.SlaBreach,
            Reasons(Work(status: WorkStatus.Assigned, priority: WorkPriority.Critical, ageHours: 999)));
    }

    [Fact]
    public void Overdue_work_is_flagged_unless_completed()
    {
        Assert.Contains(AttentionReason.Overdue,
            Reasons(Work(status: WorkStatus.InProgress, dueDate: Now.AddDays(-2))));
        Assert.DoesNotContain(AttentionReason.Overdue,
            Reasons(Work(status: WorkStatus.InProgress, dueDate: Now.AddDays(2))));
    }

    [Fact]
    public void Overdue_is_critical_when_the_work_itself_is_critical()
    {
        Assert.Equal(AttentionSeverity.Critical,
            AttentionRules.Evaluate(Work(status: WorkStatus.InProgress, priority: WorkPriority.Critical, dueDate: Now.AddDays(-1)), Now)
                .Single(f => f.Reason == AttentionReason.Overdue).Severity);
    }

    [Fact]
    public void On_hold_with_a_vendor_and_no_recent_activity_waits_on_the_vendor()
    {
        Assert.Contains(AttentionReason.WaitingOnVendor,
            Reasons(Work(status: WorkStatus.OnHold, vendor: true, lastActivityHoursAgo: 24 * 9)));
        // Fresh activity clears it.
        Assert.DoesNotContain(AttentionReason.WaitingOnVendor,
            Reasons(Work(status: WorkStatus.OnHold, vendor: true, lastActivityHoursAgo: 24)));
    }

    [Fact]
    public void On_hold_for_a_resident_is_informational()
    {
        var findings = AttentionRules.Evaluate(
            Work(status: WorkStatus.OnHold, resident: true, lastActivityHoursAgo: 24 * 10), Now);
        Assert.Equal(AttentionSeverity.Informational,
            findings.Single(f => f.Reason == AttentionReason.WaitingOnResident).Severity);
    }

    [Fact]
    public void A_repeat_repair_asset_flags_its_open_work()
    {
        Assert.Contains(AttentionReason.RepeatRepair,
            Reasons(Work(status: WorkStatus.Assigned, repeatRepair: true)));
    }

    [Fact]
    public void A_unit_turn_task_due_within_the_lead_time_and_not_started_is_at_risk()
    {
        Assert.Contains(AttentionReason.UnitTurnAtRisk,
            Reasons(Work(type: WorkType.UnitTurnTask, status: WorkStatus.Assigned, dueDate: Now.AddDays(3))));
        // Started work is not "at risk".
        Assert.DoesNotContain(AttentionReason.UnitTurnAtRisk,
            Reasons(Work(type: WorkType.UnitTurnTask, status: WorkStatus.InProgress, dueDate: Now.AddDays(3))));
        // Plenty of lead time.
        Assert.DoesNotContain(AttentionReason.UnitTurnAtRisk,
            Reasons(Work(type: WorkType.UnitTurnTask, status: WorkStatus.New, dueDate: Now.AddDays(20))));
    }

    [Fact]
    public void A_past_due_unit_turn_task_that_has_not_started_is_critical()
    {
        Assert.Equal(AttentionSeverity.Critical,
            AttentionRules.Evaluate(Work(type: WorkType.UnitTurnTask, status: WorkStatus.New, dueDate: Now.AddDays(-1)), Now)
                .Single(f => f.Reason == AttentionReason.UnitTurnAtRisk).Severity);
    }

    [Fact]
    public void One_work_item_can_trip_several_rules()
    {
        var reasons = Reasons(Work(
            status: WorkStatus.New, priority: WorkPriority.Critical, ageHours: 999, dueDate: Now.AddDays(-3))).ToList();
        Assert.Contains(AttentionReason.UnassignedEmergency, reasons);
        Assert.Contains(AttentionReason.SlaBreach, reasons);
        Assert.Contains(AttentionReason.Overdue, reasons);
    }
}
