namespace PropFlow.Application.Work;

/// <summary>
/// One entry in a work item's history as the API returns it. Work-history events
/// (`WorkCreated`, `VendorAssigned`, `StatusChanged`, …) and communication events
/// (`MessageQueued`, `MessageSent`, `MessageFailed`) share this shape;
/// <see cref="ResidentVisible"/> separates the internal half from what a resident would see.
/// </summary>
public sealed record TimelineItem(
    Guid Id,
    string EventType,
    DateTimeOffset OccurredAt,
    Guid? ActorId,
    string? OldValue,
    string? NewValue,
    string? RelatedObjectType,
    Guid? RelatedObjectId,
    string Changes,
    bool ResidentVisible);
