namespace PropFlow.Domain.Timeline;

using PropFlow.Domain.Work;

public sealed class TimelineEntry : TenantEntity
{
    private TimelineEntry(Guid organizationId, Guid id) : base(organizationId, id) { }

    public Guid WorkId { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string EventType { get; private set; } = "";
    public string Changes { get; private set; } = "{}";

    public static TimelineEntry Create(Guid organizationId, Guid workId, Guid actorId,
        DateTimeOffset occurredAt, string eventType, string changes) => new(organizationId, Guid.NewGuid())
    {
        WorkId = workId, ActorId = actorId, OccurredAt = occurredAt.ToUniversalTime(),
        EventType = eventType, Changes = changes
    };
    public Guid? PreviousVendorId { get; private set; }
    public Guid? VendorId { get; private set; }
    public static TimelineEntry From(VendorAssigned change) => new(change.OrganizationId, change.EventId) { WorkId = change.WorkId, ActorId = change.ActorId, OccurredAt = change.OccurredAt, EventType = nameof(VendorAssigned), PreviousVendorId = change.PreviousVendorId, VendorId = change.VendorId };
}
