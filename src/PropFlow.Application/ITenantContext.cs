namespace PropFlow.Application;

public interface ITenantContext
{
    Guid OrganizationId { get; }
}

// For explicitly scoped provisioning and background jobs. Never bind from an HTTP request body.
public sealed class FixedTenantContext : ITenantContext
{
    public FixedTenantContext(Guid organizationId)
    {
        if (organizationId == Guid.Empty) throw new TenantAccessException();
        OrganizationId = organizationId;
    }
    public Guid OrganizationId { get; }
}
