using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class InspectionWorkflowTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void Template_versions_are_positive_and_checklist_is_bounded()
    {
        var template = new InspectionTemplate(Org, Guid.NewGuid(), "Move-out", 1, ["Walls", "Floors"], Actor, DateTimeOffset.UtcNow);
        Assert.Equal(1, template.Version); Assert.Equal(["Walls", "Floors"], template.Checklist);
        Assert.Throws<ArgumentException>(() => new InspectionTemplate(Org, Guid.NewGuid(), "x", 1, [], Actor, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Inspection_requires_completion_before_approval()
    {
        var item = new Inspection(Org, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InspectionKind.MoveOut, Actor, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => item.Approve(DateTimeOffset.UtcNow));
        item.Start(); item.Complete(DateTimeOffset.UtcNow); item.Approve(DateTimeOffset.UtcNow); Assert.Equal(InspectionStatus.Approved, item.Status);
    }

    [Fact]
    public void Finding_lifecycle_is_terminal_after_resolution()
    {
        var finding = new InspectionFinding(Org, Guid.NewGuid(), Guid.NewGuid(), "Kitchen", "Leaking tap", FindingSeverity.Major);
        finding.Resolve(DateTimeOffset.UtcNow); Assert.Equal(FindingStatus.Resolved, finding.Status);
        Assert.Throws<InvalidOperationException>(() => finding.Waive());
    }

    [Fact]
    public void Turn_cannot_be_ready_until_started_and_tasks_are_completed_by_workflow()
    {
        var turn = new UnitTurn(Org, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), Actor, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => turn.MarkReady(DateTimeOffset.UtcNow)); turn.Start(); turn.MarkReady(DateTimeOffset.UtcNow); Assert.Equal(TurnStatus.Ready, turn.Status);
    }

    [Fact]
    public void Turn_task_links_work_and_validates_status_values_from_api_domain()
    {
        var task = new UnitTurnTask(Org, Guid.NewGuid(), Guid.NewGuid(), "Paint walls", null, 1);
        task.LinkWork(Guid.NewGuid()); task.ChangeStatus(TurnTaskStatus.Completed);
        Assert.Equal(TurnTaskStatus.Completed, task.Status); Assert.NotEqual(Guid.Empty, task.WorkId);
    }
}
