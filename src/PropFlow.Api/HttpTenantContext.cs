using PropFlow.Application;

namespace PropFlow.Api;

public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private Guid? organizationId;
    public Guid OrganizationId => organizationId ??= TenantAccess.Resolve(
        accessor.HttpContext?.User ?? throw new TenantAccessException());
}
