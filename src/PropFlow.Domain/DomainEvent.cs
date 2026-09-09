namespace PropFlow.Domain;

public interface IDomainEvent
{
    Guid EventId { get; }
    Guid OrganizationId { get; }
    Guid ActorId { get; }
    DateTimeOffset OccurredAt { get; }
}
