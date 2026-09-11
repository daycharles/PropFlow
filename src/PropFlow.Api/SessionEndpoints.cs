using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using PropFlow.Application;
using PropFlow.Infrastructure.Identity;

namespace PropFlow.Api;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken })).AllowAnonymous();
        app.MapPost("/api/auth/login", async (LoginRequest request, SessionAuthentication authentication, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 ||
                string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024 || string.IsNullOrWhiteSpace(request.OrganizationSlug))
                return Results.Problem(statusCode: 400, title: "Organization slug, email, and password are required");
            return await authentication.LoginAsync(request.Email.Trim(), request.Password, request.OrganizationSlug, ct)
                ? Results.NoContent() : Results.Problem(statusCode: 401, title: "Unable to sign in with those credentials and organization");
        }).AllowAnonymous().RequireRateLimiting("login");
        app.MapPost("/api/auth/password-recovery/request", async (PasswordRecoveryRequest request, SessionAuthentication authentication, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 ||
                string.IsNullOrWhiteSpace(request.OrganizationSlug) || request.OrganizationSlug.Length > 63)
                return Results.Problem(statusCode: 400, title: "Organization slug and email are required");
            await authentication.RequestPasswordResetAsync(request.Email.Trim(), request.OrganizationSlug, ct);
            return Results.Accepted();
        }).AllowAnonymous().RequireRateLimiting("login");
        app.MapPost("/api/auth/password-recovery/reset", async (PasswordResetRequest request, SessionAuthentication authentication, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.OrganizationSlug) ||
                string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrEmpty(request.NewPassword))
                return Results.Problem(statusCode: 400, title: "Organization slug, email, token, and new password are required");
            return await authentication.ResetPasswordAsync(request.Email.Trim(), request.OrganizationSlug, request.Token, request.NewPassword, ct)
                ? Results.NoContent() : Results.Problem(statusCode: 400, title: "The password reset token is invalid or expired");
        }).AllowAnonymous().RequireRateLimiting("login");
        app.MapPost("/api/auth/logout", async (ClaimsPrincipal user, SessionAuthentication authentication) =>
        {
            await authentication.LogoutAsync(user);
            return Results.NoContent();
        }).RequireAuthorization();
        app.MapGet("/api/session", (ClaimsPrincipal user) => Results.Ok(new
        {
            userId = user.FindFirstValue(ClaimTypes.NameIdentifier),
            organizationId = TenantAccess.Resolve(user),
            role = user.FindFirstValue("organization_role"),
            capabilities = user.FindAll(TenantAccess.CapabilityClaim).Select(x => x.Value).ToArray()
        })).RequireAuthorization();
    }
}
public sealed record LoginRequest(string OrganizationSlug, string Email, string Password);
public sealed record PasswordRecoveryRequest(string OrganizationSlug, string Email);
public sealed record PasswordResetRequest(string OrganizationSlug, string Email, string Token, string NewPassword);
