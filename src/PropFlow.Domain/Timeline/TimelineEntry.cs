namespace PropFlow.Domain.Timeline;

using System.Text.Json;
using PropFlow.Domain.Work;

/// <summary>
/// One append-only entry in a work item's history. Every entry has the same shape — an
/// <see cref="EventType"/>, an optional <see cref="OldValue"/>/<see cref="NewValue"/> pair, a
/// <see cref="Changes"/> JSON blob, and an optional related-object reference. There are no
/// event-specific columns.
/// </summary>
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

    public static TimelineEntry From(VendorAssigned e) => WorkEvent(
        e.OrganizationId, e.ActorId, e.OccurredAt, nameof(VendorAssigned), e.WorkId,
        e.PreviousVendorId?.ToString(), e.VendorId.ToString());

    public static TimelineEntry From(EmployeeAssigned e) => WorkEvent(
        e.OrganizationId, e.ActorId, e.OccurredAt, nameof(EmployeeAssigned), e.WorkId,
        e.PreviousEmployeeId?.ToString(), e.EmployeeId.ToString());

    public static TimelineEntry From(WorkReopened e) => WorkEvent(
        e.OrganizationId, e.ActorId, e.OccurredAt, nameof(WorkReopened), e.WorkId,
        e.PreviousStatus.ToString(), e.NewStatus.ToString());

    /// <summary>A change to a work item, related back to the work item itself.</summary>
    public static TimelineEntry WorkEvent(Guid organizationId, Guid actorId, DateTimeOffset occurredAt,
        string eventType, Guid workId, string? oldValue, string? newValue) => Record(
        organizationId, actorId, occurredAt, eventType, "WorkItem", workId, oldValue, newValue, workId,
        JsonSerializer.Serialize(new { oldValue, newValue }));

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
}
