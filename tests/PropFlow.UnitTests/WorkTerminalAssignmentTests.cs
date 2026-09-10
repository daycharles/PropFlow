using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.UnitTests;

/// <summary>
/// Completed and cancelled work takes no new assignment. ChangeStatus has always refused to leave
/// those two statuses; assignment used to walk straight past them, so a select-all over an
/// unfiltered list could hand a vendor to work nobody is going to do.
/// </summary>
public sealed class WorkTerminalAssignmentTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly DateTimeOffset Occurred = DateTimeOffset.Parse("2026-09-09T09:00:00-04:00");

    private static WorkItem InStatus(WorkStatus status)
    {
        var work = new WorkItem(Organization, Guid.NewGuid(), "Repair", Guid.NewGuid(), Guid.NewGuid());
        work.Publish(Occurred);
        work.ChangeStatus(status, Occurred);
        return work;
    }

    [Theory]
    [InlineData(WorkStatus.Completed)]
    [InlineData(WorkStatus.Cancelled)]
    public void Terminal_work_refuses_a_vendor_assignment(WorkStatus status)
    {
        var work = InStatus(status);

        var error = Assert.Throws<InvalidOperationException>(
            () => work.AssignVendor(Guid.NewGuid(), Guid.NewGuid(), Occurred));

        Assert.Equal("Completed and cancelled work is terminal.", error.Message);
        Assert.Null(work.VendorId);
        Assert.Equal(status, work.Status);
    }

    [Theory]
    [InlineData(WorkStatus.Completed)]
    [InlineData(WorkStatus.Cancelled)]
    public void Terminal_work_refuses_an_employee_assignment(WorkStatus status)
    {
        var work = InStatus(status);

        Assert.Throws<InvalidOperationException>(
            () => work.AssignEmployee(Guid.NewGuid(), Guid.NewGuid(), Occurred));

        Assert.Null(work.EmployeeId);
        Assert.Equal(status, work.Status);
    }

    [Theory]
    [InlineData(WorkStatus.Completed)]
    [InlineData(WorkStatus.Cancelled)]
    public void Terminal_work_reports_itself_terminal(WorkStatus status) =>
        Assert.True(InStatus(status).IsTerminal);

    [Theory]
    [InlineData(WorkStatus.New)]
    [InlineData(WorkStatus.Assigned)]
    [InlineData(WorkStatus.InProgress)]
    [InlineData(WorkStatus.OnHold)]
    public void Live_work_still_accepts_an_assignment(WorkStatus status)
    {
        var work = new WorkItem(Organization, Guid.NewGuid(), "Repair", Guid.NewGuid(), Guid.NewGuid());
        work.Publish(Occurred);
        if (status != WorkStatus.New) work.ChangeStatus(status, Occurred);
        var vendor = Guid.NewGuid();

        var change = work.AssignVendor(vendor, Guid.NewGuid(), Occurred);

        Assert.False(work.IsTerminal);
        Assert.NotNull(change);
        Assert.Equal(vendor, work.VendorId);
    }

    [Fact]
    public void Re_assigning_the_same_vendor_to_terminal_work_is_a_no_op_rather_than_a_throw()
    {
        // The equality short-circuit runs before the guard, so a caller replaying an assignment
        // that already happened gets "nothing changed" instead of an error.
        var work = new WorkItem(Organization, Guid.NewGuid(), "Repair", Guid.NewGuid(), Guid.NewGuid());
        work.Publish(Occurred);
        var vendor = Guid.NewGuid();
        work.AssignVendor(vendor, Guid.NewGuid(), Occurred);
        work.ChangeStatus(WorkStatus.Completed, Occurred);

        Assert.Null(work.AssignVendor(vendor, Guid.NewGuid(), Occurred));
    }
}
