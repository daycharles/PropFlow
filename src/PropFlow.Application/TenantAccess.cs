using System.Security.Claims;

namespace PropFlow.Application;

public sealed class TenantAccessException : Exception
{
    public TenantAccessException() : base("An authenticated organization context is required.") { }
}

public static class TenantAccess
{
    public const string OrganizationClaim = "organization_id";
    public const string CapabilityClaim = "capability";
    public const string AssignVendor = "Work.AssignVendor";

    public static Guid Resolve(ClaimsPrincipal principal)
    {
        var identities = principal.Identities.Where(identity => identity.IsAuthenticated).ToArray();
        var values = identities.SelectMany(identity => identity.FindAll(OrganizationClaim)).ToArray();
        if (values.Length != 1 || !Guid.TryParse(values[0].Value, out var id) || id == Guid.Empty)
            throw new TenantAccessException();
        return id;
    }
}
