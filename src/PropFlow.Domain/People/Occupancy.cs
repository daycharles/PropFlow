namespace PropFlow.Domain.People;

// A resident's tenancy of one space over a date range. A resident may have several over time;
// the current one is the row with no move-out date.
public sealed class Occupancy : TenantEntity
{
    // EF materialization only.
    private Occupancy(Guid organizationId, Guid id) : base(organizationId, id) { }

    public Occupancy(Guid organizationId, Guid id, Guid residentId, Guid spaceId, DateOnly movedInOn)
        : base(organizationId, id)
    {
        ResidentId = residentId != Guid.Empty ? residentId : throw new ArgumentException("Resident is required.", nameof(residentId));
        SpaceId = spaceId != Guid.Empty ? spaceId : throw new ArgumentException("Space is required.", nameof(spaceId));
        MovedInOn = movedInOn;
    }

    public Guid ResidentId { get; private set; }
    public Guid SpaceId { get; private set; }
    public DateOnly MovedInOn { get; private set; }
    public DateOnly? MovedOutOn { get; private set; }

    public bool IsCurrent => MovedOutOn is null;

    public void EndOn(DateOnly movedOutOn)
    {
        if (movedOutOn < MovedInOn)
            throw new ArgumentException("Move-out cannot precede move-in.", nameof(movedOutOn));
        MovedOutOn = movedOutOn;
    }
}
