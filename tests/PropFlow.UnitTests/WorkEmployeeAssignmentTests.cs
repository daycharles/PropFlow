using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class WorkEmployeeAssignmentTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly DateTimeOffset Occurred = DateTimeOffset.Parse("2026-09-09T09:00:00-04:00");

    private static WorkItem Published()
    {
        var work = new WorkItem(Organization, Guid.NewGuid(), "Repair", Guid.NewGuid(), Guid.NewGuid());
        work.Publish(Occurred);
        return work;
    }

    [Fact]
    public void Assigning_an_employee_raises_an_event_and_moves_new_work_to_assigned()
    {
        var work = Published();
        var actor = Guid.NewGuid();
        var employee = Guid.NewGuid();

        var change = work.AssignEmployee(employee, actor, Occurred);

        Assert.NotNull(change);
        Assert.Equal(Organization, change.OrganizationId);
        Assert.Equal(actor, change.ActorId);
        Assert.Equal(work.Id, change.WorkId);
        Assert.Null(change.PreviousEmployeeId);
        Assert.Equal(employee, change.EmployeeId);
        Assert.Equal(TimeSpan.Zero, change.OccurredAt.Offset);
        Assert.Equal(employee, work.EmployeeId);
        Assert.Equal(WorkStatus.Assigned, work.Status);
    }

    [Fact]
    public void Reassigning_an_employee_carries_the_previous_employee_id()
    {
        var work = Published();
        var actor = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        work.AssignEmployee(first, actor, Occurred);
        var change = work.AssignEmployee(second, actor, Occurred);

        Assert.NotNull(change);
        Assert.Equal(first, change.PreviousEmployeeId);
        Assert.Equal(second, change.EmployeeId);
        Assert.Equal(second, work.EmployeeId);
    }

    [Fact]
    public void Assigning_the_same_employee_again_raises_no_event()
    {
        var work = Published();
        var actor = Guid.NewGuid();
        var employee = Guid.NewGuid();

        work.AssignEmployee(employee, actor, Occurred);

        Assert.Null(work.AssignEmployee(employee, actor, Occurred));
    }

    [Fact]
    public void Employee_assignment_requires_an_actor_and_an_employee()
    {
        var work = Published();

        Assert.Throws<ArgumentException>(() => work.AssignEmployee(Guid.NewGuid(), Guid.Empty, Occurred));
        Assert.Throws<ArgumentException>(() => work.AssignEmployee(Guid.Empty, Guid.NewGuid(), Occurred));
        Assert.Null(work.EmployeeId);
    }
}
