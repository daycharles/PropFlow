using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using PropFlow.Application;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-PropFlow";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(TenantAccess.AssignVendor, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(TenantAccess.CapabilityClaim, TenantAccess.AssignVendor));

var app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy", service = "PropFlow.Api" }));
app.MapGet("/api/session", (HttpContext context) =>
    Results.Ok(new { organizationId = TenantAccess.Resolve(context.User) }))
    .RequireAuthorization();
app.Run();

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var forbidden = exception is TenantAccessException;
        if (!forbidden) logger.LogError(exception, "Request failed. TraceId: {TraceId}", context.TraceIdentifier);
        await Results.Problem(
            statusCode: forbidden ? 403 : 500,
            title: forbidden ? "Organization access denied" : "An unexpected error occurred",
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier })
            .ExecuteAsync(context);
        return true;
    }
}
