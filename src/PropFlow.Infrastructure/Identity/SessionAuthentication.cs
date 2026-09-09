using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using PropFlow.Application;

namespace PropFlow.Infrastructure.Identity;

public sealed class SessionAuthentication(UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn, MembershipAccess memberships)
{
    // A valid Identity password hash whose work factor VerifyHashedPassword reads from the hash
    // itself. Verifying against it for an unknown email spends the same time as a wrong-password
    // check, so a missing account is not distinguishable by response latency.
    private static readonly PasswordHasher<ApplicationUser> DecoyHasher = new();
    private static readonly string DecoyHash = new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), "decoy");

    public async Task<bool> LoginAsync(string email, string password, Guid organizationId, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null) { DecoyHasher.VerifyHashedPassword(new ApplicationUser(), DecoyHash, password); return false; }
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!result.Succeeded) return false;
        var membership = await memberships.FindActiveAsync(user.Id, organizationId, ct);
        if (membership is null) return false;
        await signIn.SignInWithClaimsAsync(user, isPersistent: false, TenantClaims(membership));
        return true;
    }

    public async Task<bool> LoginAsync(string email, string password, string organizationSlug, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null) { DecoyHasher.VerifyHashedPassword(new ApplicationUser(), DecoyHash, password); return false; }
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!result.Succeeded) return false;
        var membership = await memberships.FindActiveBySlugAsync(user.Id, organizationSlug.Trim().ToLowerInvariant(), ct);
        if (membership is null) return false;
        await signIn.SignInWithClaimsAsync(user, isPersistent: false, TenantClaims(membership));
        return true;
    }

    public async Task ValidateCookieAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        Guid organizationId;
        try { organizationId = TenantAccess.Resolve(principal!); }
        catch (TenantAccessException) { context.RejectPrincipal(); return; }
        var user = await users.GetUserAsync(principal!);
        if (user is null || await users.IsLockedOutAsync(user) ||
            principal!.FindFirstValue(signIn.Options.ClaimsIdentity.SecurityStampClaimType) != await users.GetSecurityStampAsync(user))
        {
            context.RejectPrincipal();
            return;
        }
        var membership = await memberships.FindActiveAsync(user.Id, organizationId, context.HttpContext.RequestAborted);
        if (membership is null) { context.RejectPrincipal(); return; }
        // Rebuild capabilities on every request so membership/role changes take effect immediately.
        var refreshed = await signIn.CreateUserPrincipalAsync(user);
        ((ClaimsIdentity)refreshed.Identity!).AddClaims(TenantClaims(membership));
        context.ReplacePrincipal(refreshed);
    }

    public async Task LogoutAsync(ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        if (user is not null && !(await users.UpdateSecurityStampAsync(user)).Succeeded)
            throw new InvalidOperationException("Session revocation failed.");
        await signIn.SignOutAsync();
    }

    private static IEnumerable<Claim> TenantClaims(OrganizationMembership membership)
    {
        yield return new Claim(TenantAccess.OrganizationClaim, membership.OrganizationId.ToString());
        yield return new Claim("organization_role", membership.Role);
        foreach (var capability in Capabilities.ForRole(membership.Role))
            yield return new Claim(TenantAccess.CapabilityClaim, capability);
    }
}
