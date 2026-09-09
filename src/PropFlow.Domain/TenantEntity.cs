namespace PropFlow.Domain;

public abstract class TenantEntity
{
    protected TenantEntity(Guid organizationId, Guid id)
    {
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization is required.", nameof(organizationId));
        if (id == Guid.Empty) throw new ArgumentException("Entity ID is required.", nameof(id));
        OrganizationId = organizationId;
        Id = id;
    }

    public Guid OrganizationId { get; private set; }
    public Guid Id { get; private set; }
}
