using PropFlow.Domain.Automation;
using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class AutomationRuleTests
{
    [Fact]
    public void Rule_round_trips_the_closed_v1_when_if_then_shape()
    {
        var category = Guid.NewGuid();
        var template = Guid.NewGuid();
        var rule = new AutomationRule(Guid.NewGuid(), Guid.NewGuid(), "Emergency acknowledgement",
            AutomationTrigger.WorkCreated,
            [new(AutomationConditionKind.CategoryEquals, CategoryId: category)],
            [new(AutomationActionKind.SetPriority, Priority: WorkPriority.Critical),
             new(AutomationActionKind.SendResidentMessage, TemplateId: template)], DateTimeOffset.UtcNow);

        Assert.Equal("Emergency acknowledgement", rule.Name);
        Assert.Equal(category, Assert.Single(rule.ReadConditions()).CategoryId);
        Assert.Equal(2, rule.ReadActions().Count);
        rule.Disable();
        Assert.False(rule.IsEnabled);
    }

    [Fact]
    public void Rule_rejects_open_ended_or_incomplete_actions()
    {
        Assert.Throws<ArgumentException>(() => new AutomationRule(Guid.NewGuid(), Guid.NewGuid(), "No action",
            AutomationTrigger.WorkCreated, [], [], DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new AutomationRule(Guid.NewGuid(), Guid.NewGuid(), "No template",
            AutomationTrigger.WorkStatusChanged, [], [new(AutomationActionKind.SendResidentMessage)], DateTimeOffset.UtcNow));
    }

    private static AutomationRule Rule(IReadOnlyList<AutomationCondition> conditions) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "r", AutomationTrigger.WorkCreated, conditions,
            [new(AutomationActionKind.SetPriority, Priority: WorkPriority.High)], DateTimeOffset.UtcNow);

    [Fact]
    public void An_empty_condition_set_matches_anything()
    {
        Assert.True(Rule([]).Matches(WorkStatus.Completed, categoryId: null));
    }

    [Fact]
    public void Conditions_are_anded()
    {
        var category = Guid.NewGuid();
        var rule = Rule([
            new(AutomationConditionKind.WorkStatusEquals, Status: WorkStatus.New),
            new(AutomationConditionKind.CategoryEquals, CategoryId: category),
        ]);

        Assert.True(rule.Matches(WorkStatus.New, category));
        Assert.False(rule.Matches(WorkStatus.New, Guid.NewGuid()));
        Assert.False(rule.Matches(WorkStatus.Assigned, category));
    }

    [Fact]
    public void Category_equals_distinguishes_a_missing_category()
    {
        var category = Guid.NewGuid();
        var rule = Rule([new(AutomationConditionKind.CategoryEquals, CategoryId: category)]);
        Assert.True(rule.Matches(WorkStatus.New, category));
        Assert.False(rule.Matches(WorkStatus.New, null));
    }
}
