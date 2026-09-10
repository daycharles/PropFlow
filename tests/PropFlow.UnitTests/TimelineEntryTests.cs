using System.Text.Json;
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

    [Fact]
    public void A_vendor_assignment_carries_a_populated_changes_blob()
    {
        var vendor = Guid.NewGuid();
        var previous = Guid.NewGuid();

        var entry = TimelineEntry.From(new VendorAssigned(Guid.NewGuid(), Org, Actor, When, Work, previous, vendor));

        Assert.NotEqual("{}", entry.Changes);
        var changes = JsonSerializer.Deserialize<JsonElement>(entry.Changes);
        Assert.Equal(previous.ToString(), changes.GetProperty("oldValue").GetString());
        Assert.Equal(vendor.ToString(), changes.GetProperty("newValue").GetString());
    }

    [Fact]
    public void A_work_event_entry_takes_its_id_from_the_domain_event()
    {
        var eventId = Guid.NewGuid();
        var e = new VendorAssigned(eventId, Org, Actor, When, Work, null, Guid.NewGuid());

        // Re-deriving the entry from the same event yields the same primary key, so a replayed
        // event cannot append a second row.
        Assert.Equal(eventId, TimelineEntry.From(e).Id);
        Assert.Equal(TimelineEntry.From(e).Id, TimelineEntry.From(e).Id);
    }

    [Fact]
    public void Record_generates_an_id_when_no_event_supplies_one()
    {
        var a = Valid();
        var b = Valid();

        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Record_requires_an_organization()
        => Assert.Throws<ArgumentException>(() => Valid(organizationId: Guid.Empty));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Record_requires_an_event_type(string eventType)
        => Assert.Throws<ArgumentException>(() => Valid(eventType: eventType));

    [Fact]
    public void Record_rejects_an_over_long_event_type()
        => Assert.Throws<ArgumentException>(() => Valid(eventType: new string('e', 101)));

    [Fact]
    public void Record_rejects_empty_identifiers()
    {
        Assert.Throws<ArgumentException>(() => Valid(actorId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Valid(workId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Valid(relatedObjectId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Valid(id: Guid.Empty));
    }

    [Fact]
    public void Record_requires_a_related_object_type_whenever_a_related_id_is_given()
        => Assert.Throws<ArgumentException>(() => Valid(relatedObjectType: null));

    [Fact]
    public void Record_rejects_an_over_long_related_object_type()
        => Assert.Throws<ArgumentException>(() => Valid(relatedObjectType: new string('t', 101)));

    [Fact]
    public void Record_rejects_over_long_values()
    {
        Assert.Throws<ArgumentException>(() => Valid(oldValue: new string('o', 4001)));
        Assert.Throws<ArgumentException>(() => Valid(newValue: new string('n', 4001)));
    }

    /// <summary>A call that passes every guard, so each test can invalidate exactly one thing.</summary>
    private static TimelineEntry Valid(
        Guid? organizationId = null, Guid? actorId = null, DateTimeOffset? occurredAt = null,
        string eventType = "SomethingHappened", string? relatedObjectType = "WorkItem",
        Guid? relatedObjectId = null, string? oldValue = "before", string? newValue = "after",
        Guid? workId = null, string changes = "{}", Guid? id = null)
        => TimelineEntry.Record(
            organizationId ?? Org, actorId ?? Actor, occurredAt ?? When, eventType,
            relatedObjectType, relatedObjectId ?? Work, oldValue, newValue,
            workId ?? Work, changes, id);
}
