using PropFlow.Domain.Timeline;
using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class TimelineEntryTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Work = Guid.NewGuid();
    private static readonly DateTimeOffset When = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_work_event_factory_produces_the_same_shape()
    {
        var vendor = Guid.NewGuid();
        var employee = Guid.NewGuid();

        var v = TimelineEntry.From(new VendorAssigned(Guid.NewGuid(), Org, Actor, When, Work, null, vendor));
        var e = TimelineEntry.From(new EmployeeAssigned(Guid.NewGuid(), Org, Actor, When, Work, employee, Guid.NewGuid()));
        var r = TimelineEntry.From(new WorkReopened(Guid.NewGuid(), Org, Actor, When, Work, WorkStatus.Completed, WorkStatus.Assigned));

        foreach (var entry in new[] { v, e, r })
        {
            Assert.Equal(Org, entry.OrganizationId);
            Assert.Equal(Actor, entry.ActorId);
            Assert.Equal(Work, entry.WorkId);
            Assert.Equal(When, entry.OccurredAt);
            Assert.Equal("WorkItem", entry.RelatedObjectType);
            Assert.Equal(Work, entry.RelatedObjectId);
            Assert.Contains("oldValue", entry.Changes);
            Assert.Contains("newValue", entry.Changes);
        }

        Assert.Equal(nameof(VendorAssigned), v.EventType);
        Assert.Null(v.OldValue);
        Assert.Equal(vendor.ToString(), v.NewValue);

        Assert.Equal(nameof(EmployeeAssigned), e.EventType);
        Assert.Equal(employee.ToString(), e.OldValue);

        Assert.Equal(nameof(WorkReopened), r.EventType);
        Assert.Equal("Completed", r.OldValue);
        Assert.Equal("Assigned", r.NewValue);
    }
}
