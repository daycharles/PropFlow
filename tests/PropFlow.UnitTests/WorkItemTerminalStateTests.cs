using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class WorkItemTerminalStateTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static WorkItem Completed()
    {
        var work = new WorkItem(Org, Guid.NewGuid(), "Fix sink", Guid.NewGuid(), Guid.NewGuid());
        work.Publish(Now);
        work.AssignVendor(Guid.NewGuid());
        work.ChangeStatus(WorkStatus.InProgress, Now);
        work.ChangeStatus(WorkStatus.Completed, Now);
        return work;
    }

    [Fact]
    public void Completed_work_refuses_edits()
    {
        Assert.Throws<InvalidOperationException>(() => Completed().Edit("New title", null, null, WorkPriority.High));
    }

    [Fact]
    public void Completed_work_refuses_scheduling_so_it_cannot_be_reopened()
    {
        var work = Completed();
        Assert.Throws<InvalidOperationException>(() => work.Schedule(Now.AddDays(1), null));
        Assert.Equal(WorkStatus.Completed, work.Status);
    }

    [Fact]
    public void Cancelled_work_refuses_edits_and_scheduling()
    {
        var work = new WorkItem(Org, Guid.NewGuid(), "Fix sink", Guid.NewGuid(), Guid.NewGuid());
        work.Publish(Now);
        work.ChangeStatus(WorkStatus.Cancelled, Now);

        Assert.Throws<InvalidOperationException>(() => work.Edit("x", null, null, WorkPriority.Normal));
        Assert.Throws<InvalidOperationException>(() => work.Schedule(Now, null));
    }

    [Fact]
    public void An_open_work_item_still_edits_and_schedules()
    {
        var work = new WorkItem(Org, Guid.NewGuid(), "Fix sink", Guid.NewGuid(), Guid.NewGuid());
        work.Publish(Now);
        work.AssignVendor(Guid.NewGuid());

        work.Edit("Fix the kitchen sink", "under the cabinet", null, WorkPriority.High);
        work.Schedule(Now.AddDays(1), Now.AddDays(1).AddHours(2));

        Assert.Equal("Fix the kitchen sink", work.Title);
        Assert.Equal(WorkStatus.Scheduled, work.Status);
    }
}
