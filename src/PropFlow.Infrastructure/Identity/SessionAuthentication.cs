using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using PropFlow.Application;
using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;
using PropFlow.Infrastructure.Communications;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Infrastructure.Identity;

public sealed class SessionAuthentication(UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn, MembershipAccess memberships, RoleCapabilityService roleCapabilities,
    UserSessionService sessions, IConfiguration configuration, TimeProvider clock)
{
    // A valid Identity password hash whose work factor VerifyHashedPassword reads from the hash
    // itself. Verifying against it for an unknown email spends the same time as a wrong-password
    // check, so a missing account is not distinguishable by response latency.
    private static readonly PasswordHasher<ApplicationUser> DecoyHasher = new();
    private static readonly string DecoyHash = new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), "decoy");

    public async Task RequestPasswordResetAsync(string email, string organizationSlug, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null) return;
        var membership = await memberships.FindActiveBySlugAsync(user.Id, organizationSlug.Trim().ToLowerInvariant(), ct);
        if (membership is null) return;

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var connection = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Database.");
        await using var communications = DatabaseProvisioner.CreateCommunicationsStore(connection, membership.OrganizationId);
        var outbox = new EfOutbox(communications, clock);
        await outbox.EnqueueAsync(new OutboxSubmission(
            MessageChannel.Email,
            user.Email!,
            "Reset your PropFlow password",
            $"A password reset was requested for your PropFlow account.\n\nOrganization: {organizationSlug.Trim()}\nReset token: {token}\n\nThis token expires according to the configured Identity token lifetime and can be used only once.",
            $"password-reset:{membership.OrganizationId}:{user.Id}:{Guid.NewGuid():N}"), ct);
    }

    public async Task<bool> ResetPasswordAsync(string email, string organizationSlug, string token, string newPassword, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null) return false;
        if (await memberships.FindActiveBySlugAsync(user.Id, organizationSlug.Trim().ToLowerInvariant(), ct) is null) return false;
        var result = await users.ResetPasswordAsync(user, token, newPassword);
        return result.Succeeded;
    }

    public async Task<bool> LoginAsync(string email, string password, Guid organizationId, CancellationToken ct,
        string? userAgent = null, string? ipAddress = null)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null) { DecoyHasher.VerifyHashedPassword(new ApplicationUser(), DecoyHash, password); return false; }
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!result.Succeeded) return false;
        var membership = await memberships.FindActiveAsync(user.Id, organizationId, ct);
        if (membership is null) return false;
        var session = await sessions.StartAsync(user.Id, membership.OrganizationId, userAgent, ipAddress, ct);
        await signIn.SignInWithClaimsAsync(user, isPersistent: false, await TenantClaimsAsync(membership, session.Id, ct));
        return true;
    }

    public async Task<bool> LoginAsync(string email, string password, string organizationSlug, CancellationToken ct,
        string? userAgent = null, string? ipAddress = null)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null) { DecoyHasher.VerifyHashedPassword(new ApplicationUser(), DecoyHash, password); return false; }
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!result.Succeeded) return false;
        var membership = await memberships.FindActiveBySlugAsync(user.Id, organizationSlug.Trim().ToLowerInvariant(), ct);
        if (membership is null) return false;
        var session = await sessions.StartAsync(user.Id, membership.OrganizationId, userAgent, ipAddress, ct);
        await signIn.SignInWithClaimsAsync(user, isPersistent: false, await TenantClaimsAsync(membership, session.Id, ct));
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
        // PF-S01.06: a cookie predating session tracking carries no session_id claim - treated as
        // still valid (SecurityStamp above is still the source of truth for those), but any cookie
        // that DOES carry one must resolve to a still-active UserSession row.
        var sessionClaim = principal!.FindFirstValue(TenantAccess.SessionClaim);
        Guid? sessionId = Guid.TryParse(sessionClaim, out var parsedSessionId) ? parsedSessionId : null;
        if (sessionId is not null && !await sessions.TouchAsync(sessionId.Value, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            return;
        }
        var membership = await memberships.FindActiveAsync(user.Id, organizationId, context.HttpContext.RequestAborted);
        if (membership is null) { context.RejectPrincipal(); return; }
        // Rebuild capabilities on every request so membership/role/override changes take effect
        // immediately, not just at next login.
        var refreshed = await signIn.CreateUserPrincipalAsync(user);
        ((ClaimsIdentity)refreshed.Identity!).AddClaims(await TenantClaimsAsync(membership, sessionId, context.HttpContext.RequestAborted));
        context.ReplacePrincipal(refreshed);
    }

    public async Task LogoutAsync(ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        if (user is not null && !(await users.UpdateSecurityStampAsync(user)).Succeeded)
            throw new InvalidOperationException("Session revocation failed.");
        await signIn.SignOutAsync();
    }

    private async Task<IEnumerable<Claim>> TenantClaimsAsync(OrganizationMembership membership, Guid? sessionId, CancellationToken ct)
    {
        var capabilities = await roleCapabilities.EffectiveCapabilitiesAsync(
            membership.OrganizationId, membership.Role, membership.EmployeeId, membership.VendorId, ct);
        var claims = new List<Claim>
        {
            new(TenantAccess.OrganizationClaim, membership.OrganizationId.ToString()),
            new("organization_role", membership.Role),
        };
        if (sessionId is Guid id) claims.Add(new Claim(TenantAccess.SessionClaim, id.ToString()));
        claims.AddRange(capabilities.Select(capability => new Claim(TenantAccess.CapabilityClaim, capability)));
        return claims;
    }
}
