namespace PropFlow.Domain.Timeline;

using PropFlow.Domain.Work;

public sealed class TimelineEntry : TenantEntity
{
    private TimelineEntry(Guid organizationId, Guid id) : base(organizationId, id) { }

    public Guid? WorkId { get; private set; }
    public Guid? ActorId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string EventType { get; private set; } = "";
    public string Changes { get; private set; } = "{}";
    public string? RelatedObjectType { get; private set; }
    public Guid? RelatedObjectId { get; private set; }
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }

    public static TimelineEntry Create(Guid organizationId, Guid workId, Guid actorId,
        DateTimeOffset occurredAt, string eventType, string changes) => new(organizationId, Guid.NewGuid())
    {
        WorkId = workId, ActorId = actorId, OccurredAt = occurredAt.ToUniversalTime(),
        EventType = eventType, Changes = changes
    };
    public static TimelineEntry Record(Guid organizationId, Guid? actorId, DateTimeOffset occurredAt,
        string eventType, string? relatedObjectType, Guid? relatedObjectId, string? oldValue, string? newValue,
        Guid? workId = null, string changes = "{}")
    {
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization is required.", nameof(organizationId));
        if (string.IsNullOrWhiteSpace(eventType) || eventType.Trim().Length > 100) throw new ArgumentException("Event type is required.", nameof(eventType));
        if (actorId == Guid.Empty || workId == Guid.Empty || relatedObjectId == Guid.Empty) throw new ArgumentException("Identifiers cannot be empty.");
        if (relatedObjectId is not null && string.IsNullOrWhiteSpace(relatedObjectType)) throw new ArgumentException("Related object type is required.", nameof(relatedObjectType));
        if (relatedObjectType?.Trim().Length > 100) throw new ArgumentException("Related object type is too long.", nameof(relatedObjectType));
        if (oldValue?.Length > 4000 || newValue?.Length > 4000) throw new ArgumentException("Timeline values are too long.");
        return new TimelineEntry(organizationId, Guid.NewGuid())
        {
            WorkId = workId, ActorId = actorId, OccurredAt = occurredAt.ToUniversalTime(), EventType = eventType.Trim(),
            RelatedObjectType = string.IsNullOrWhiteSpace(relatedObjectType) ? null : relatedObjectType.Trim(),
            RelatedObjectId = relatedObjectId, OldValue = oldValue, NewValue = newValue, Changes = changes
        };
    }
    public Guid? PreviousVendorId { get; private set; }
    public Guid? VendorId { get; private set; }
    public static TimelineEntry From(VendorAssigned change) => new(change.OrganizationId, change.EventId) { WorkId = change.WorkId, ActorId = change.ActorId, OccurredAt = change.OccurredAt, EventType = nameof(VendorAssigned), PreviousVendorId = change.PreviousVendorId, VendorId = change.VendorId, RelatedObjectType = "WorkItem", RelatedObjectId = change.WorkId, OldValue = change.PreviousVendorId?.ToString(), NewValue = change.VendorId.ToString() };
}
