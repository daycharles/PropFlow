namespace PropFlow.Domain.Work;

public sealed class WorkItem : TenantEntity
{
    public WorkItem(Guid organizationId, Guid id, string title) : base(organizationId, id)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            throw new ArgumentException("Title must contain 1 to 200 characters.", nameof(title));
        Title = title.Trim();
    }

    public string Title { get; }
    public Guid? VendorId { get; private set; }

    // Application services must resolve and authorize the vendor within the active tenant.
    public VendorAssigned? AssignVendor(Guid vendorId, Guid actorId, DateTimeOffset occurredAt)
    {
        if (vendorId == Guid.Empty) throw new ArgumentException("Vendor is required.", nameof(vendorId));
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        if (VendorId == vendorId) return null;

        var change = new VendorAssigned(Guid.NewGuid(), OrganizationId, actorId,
            occurredAt.ToUniversalTime(), Id, VendorId, vendorId);
        VendorId = vendorId;
        return change;
    }
}

public sealed record VendorAssigned(
    Guid EventId,
    Guid OrganizationId,
    Guid ActorId,
    DateTimeOffset OccurredAt,
    Guid WorkId,
    Guid? PreviousVendorId,
    Guid VendorId) : IDomainEvent;
