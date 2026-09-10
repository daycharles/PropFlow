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

    [Fact]
    public void Reopen_is_the_one_door_out_of_a_terminal_state()
    {
        var actor = Guid.NewGuid();
        var completed = Completed();

        var reopened = completed.Reopen(actor, Now.AddDays(2));

        Assert.Equal(WorkStatus.Assigned, completed.Status); // it had a vendor
        Assert.Null(completed.CompletedAt);
        Assert.Equal(WorkStatus.Completed, reopened.PreviousStatus);
        Assert.Equal(WorkStatus.Assigned, reopened.NewStatus);
        // and now it is editable again
        completed.Edit("Reopened: fix sink properly", null, null, WorkPriority.High);
        Assert.Equal("Reopened: fix sink properly", completed.Title);
    }

    [Fact]
    public void Reopening_unassigned_cancelled_work_goes_back_to_New()
    {
        var work = new WorkItem(Org, Guid.NewGuid(), "Fix sink", Guid.NewGuid(), Guid.NewGuid());
        work.Publish(Now);
        work.ChangeStatus(WorkStatus.Cancelled, Now);

        work.Reopen(Guid.NewGuid(), Now);

        Assert.Equal(WorkStatus.New, work.Status);
    }

    [Fact]
    public void Reopen_rejects_non_terminal_work_and_an_empty_actor()
    {
        var open = new WorkItem(Org, Guid.NewGuid(), "Fix sink", Guid.NewGuid(), Guid.NewGuid());
        open.Publish(Now);
        Assert.Throws<InvalidOperationException>(() => open.Reopen(Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => Completed().Reopen(Guid.Empty, Now));
    }

    [Fact]
    public void SetPriority_changes_priority_but_not_on_terminal_work()
    {
        var open = new WorkItem(Org, Guid.NewGuid(), "Fix sink", Guid.NewGuid(), Guid.NewGuid());
        open.Publish(Now);
        open.SetPriority(WorkPriority.Critical);
        Assert.Equal(WorkPriority.Critical, open.Priority);

        Assert.Throws<InvalidOperationException>(() => Completed().SetPriority(WorkPriority.Low));
    }
}
