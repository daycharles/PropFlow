using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PropFlow.Api;
using PropFlow.Application;
using PropFlow.Application.Communications;
using PropFlow.Application.Work;
using PropFlow.Infrastructure.Communications;
using PropFlow.Infrastructure.Identity;
using PropFlow.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Database to the restricted PostgreSQL runtime account.");
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

// Data Protection secures the auth and antiforgery cookies. In a real deployment the key ring
// must persist and be shared across instances (and encrypted at rest); Development/Testing keep
// the ephemeral default.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("PropFlow");
var keyPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(keyPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
    var certPath = builder.Configuration["DataProtection:CertificatePath"];
    if (!string.IsNullOrWhiteSpace(certPath))
        dataProtection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(
            certPath, builder.Configuration["DataProtection:CertificatePassword"] ?? ""));
}
else if (builder.Environment.IsProduction() || builder.Environment.IsStaging())
{
    throw new InvalidOperationException(
        "Set DataProtection:KeyPath to a persistent directory shared by every instance. " +
        "Without it, auth and antiforgery tokens do not survive a restart or a second instance.");
}
builder.Services.AddDbContext<IdentityStore>(options => options.UseNpgsql(connection,
    postgres => postgres.MigrationsHistoryTable("__IdentityMigrations", "identity")));
builder.Services.AddDbContext<OperationsStore>(options => options.UseNpgsql(connection,
    postgres => postgres.MigrationsHistoryTable("__OperationsMigrations", "operations")));
builder.Services.AddDbContext<CommunicationsStore>(options => options.UseNpgsql(connection,
    postgres => postgres.MigrationsHistoryTable("__CommunicationsMigrations", "communications")));
builder.Services.AddScoped<MembershipAccess>();
builder.Services.AddScoped<SessionAuthentication>();
builder.Services.AddScoped<IWorkOperations, EfWorkOperations>();
builder.Services.AddScoped<IOutbox, EfOutbox>();
builder.Services.AddSingleton<ISentMessageLog, InMemorySentMessageLog>();
builder.Services.AddSingleton<IMessageSender, MockSmsSender>();
builder.Services.AddSingleton<IMessageSender, MockEmailSender>();
builder.Services.AddSingleton(builder.Configuration.GetSection(CommunicationsOptions.SectionName)
    .Get<CommunicationsOptions>() ?? new CommunicationsOptions());
builder.Services.AddSingleton<OutboxRelay>();
// The background poller is off under integration tests, which drive OutboxRelay directly.
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<IdentityStore>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "__Host-PropFlow";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    options.Events.OnValidatePrincipal = context => context.HttpContext.RequestServices
        .GetRequiredService<SessionAuthentication>().ValidateCookieAsync(context);
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-PropFlow-CSRF";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddAuthorization(options =>
{
    foreach (var capability in Capabilities.All)
        options.AddPolicy(capability, policy => policy.RequireAuthenticatedUser()
            .RequireClaim(TenantAccess.CapabilityClaim, capability));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddHostedService<RuntimeDatabaseGuard>();
builder.Services.AddHealthChecks().AddCheck<DatabaseReadiness>("database");

var app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method))
        {
            try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            {
                await Results.Problem(statusCode: 400, title: "Invalid or missing CSRF token").ExecuteAsync(context);
                return;
            }
        }
    }
    await next(context);
});
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy", service = "PropFlow.Api" }));
app.MapHealthChecks("/health/ready");
app.MapOpenApi().RequireAuthorization();
app.MapSessionEndpoints();
app.MapWorkEndpoints();
app.MapCommunicationEndpoints();
app.MapCategoryEndpoints();
app.MapResidentEndpoints();
app.Run();

public partial class Program { }
