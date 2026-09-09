using PropFlow.Domain.Work;

namespace PropFlow.Domain.Timeline;

public sealed class TimelineEntry : TenantEntity
{
    private TimelineEntry(Guid organizationId, Guid id) : base(organizationId, id) { }

    public Guid WorkId { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string EventType { get; private set; } = "";
    public Guid? PreviousVendorId { get; private set; }
    public Guid VendorId { get; private set; }

    public static TimelineEntry From(VendorAssigned change) => new(change.OrganizationId, change.EventId)
    {
        WorkId = change.WorkId,
        ActorId = change.ActorId,
        OccurredAt = change.OccurredAt,
        EventType = nameof(VendorAssigned),
        PreviousVendorId = change.PreviousVendorId,
        VendorId = change.VendorId
    };
}
