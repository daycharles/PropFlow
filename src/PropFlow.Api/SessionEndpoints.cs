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
