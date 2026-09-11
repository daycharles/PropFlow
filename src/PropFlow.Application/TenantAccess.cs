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
    // PF-S01.06: identifies the UserSession row backing this cookie, so a specific device/session
    // can be revoked without bumping SecurityStamp (which would sign every session out at once).
    public const string SessionClaim = "session_id";

    public static Guid Resolve(ClaimsPrincipal principal)
    {
        var identities = principal.Identities.Where(identity => identity.IsAuthenticated).ToArray();
        var values = identities.SelectMany(identity => identity.FindAll(OrganizationClaim)).ToArray();
        if (values.Length != 1 || !Guid.TryParse(values[0].Value, out var id) || id == Guid.Empty)
            throw new TenantAccessException();
        return id;
    }
}
