using PropFlow.Domain.People;
using PropFlow.Domain.Properties;
using PropFlow.Domain.Timeline;
using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class M3DomainTests
{
    private static readonly Guid Organization = Guid.NewGuid();

    [Fact]
    public void Property_hierarchy_requires_parent_ids_and_an_iana_time_zone()
    {
        var portfolio = new Portfolio(Organization, Guid.NewGuid(), "  Northeast  ");
        var property = new Property(Organization, Guid.NewGuid(), portfolio.Id, "  Main Street  ", "America/New_York");
        var building = new Building(Organization, Guid.NewGuid(), property.Id, "A");
        var space = new Space(Organization, Guid.NewGuid(), property.Id, building.Id, "101");

        Assert.Equal("Northeast", portfolio.Name);
        Assert.Equal(property.Id, building.PropertyId);
        Assert.Equal(building.Id, space.BuildingId);
        Assert.Throws<ArgumentException>(() => new Property(Organization, Guid.NewGuid(), portfolio.Id, "Bad", "Eastern"));
    }

    [Fact]
    public void Work_item_captures_extended_work_details()
    {
        var work = new WorkItem(Organization, Guid.NewGuid(), "Repair", Guid.NewGuid(), Guid.NewGuid());
        var due = DateTimeOffset.Parse("2026-09-12T09:00:00-04:00");

        work.SetLocation(work.PropertyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        work.SetDueDate(due);
        work.SetCost(123.45m);
        work.SetNotes("  technician note  ", "  Resident update  ");

        Assert.Equal(due, work.DueDate);
        Assert.Equal(123.45m, work.Cost);
        Assert.Equal("technician note", work.InternalNotes);
        Assert.Equal("Resident update", work.ResidentVisibleNotes);
        Assert.Throws<ArgumentOutOfRangeException>(() => work.SetCost(-1));
    }

    [Fact]
    public void Timeline_can_record_a_non_work_related_change()
    {
        var propertyId = Guid.NewGuid();
        var entry = TimelineEntry.Record(Organization, null, DateTimeOffset.Parse("2026-09-09T09:00:00-04:00"),
            "PropertyRenamed", "Property", propertyId, "Old name", "New name");

        Assert.Null(entry.WorkId);
        Assert.Null(entry.ActorId);
        Assert.Equal(propertyId, entry.RelatedObjectId);
        Assert.Equal("Old name", entry.OldValue);
        Assert.Equal("New name", entry.NewValue);
        Assert.Equal(TimeSpan.Zero, entry.OccurredAt.Offset);
    }

    [Fact]
    public void Vendor_and_employee_support_contact_lifecycle()
    {
        var vendor = new Vendor(Organization, Guid.NewGuid(), "Plumber");
        var employee = new Employee(Organization, Guid.NewGuid(), "Pat", null, null);

        vendor.UpdateContact("vendor@example.test", "555", "Plumbing", "Emergency");
        vendor.Deactivate();
        employee.UpdateContact(" Pat Smith ", "pat@example.test", "555");
        employee.Deactivate();

        Assert.Equal("Plumbing", vendor.Trade);
        Assert.Equal("Emergency", vendor.Category);
        Assert.False(vendor.IsActive);
        Assert.Equal("Pat Smith", employee.DisplayName);
        Assert.False(employee.IsActive);
    }
}
