using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using PropFlow.Api;
using PropFlow.Application;
using PropFlow.Application.Assets;
using PropFlow.Application.Attachments;
using PropFlow.Application.Attention;
using PropFlow.Application.Billing;
using PropFlow.Application.Automation;
using PropFlow.Application.Communications;
using PropFlow.Application.Integrations;
using PropFlow.Application.Search;
using PropFlow.Application.Work;
using PropFlow.Infrastructure.Billing;
using PropFlow.Infrastructure.Communications;
using PropFlow.Infrastructure.Attachments;
using PropFlow.Infrastructure.Identity;
using PropFlow.Infrastructure.Integrations;
using PropFlow.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Database to the restricted PostgreSQL runtime account.");
DeploymentConfiguration.ValidatePostgresTls(connection, builder.Environment);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    DeploymentConfiguration.ConfigureForwardedHeaders(options, builder.Configuration, builder.Environment));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddSingleton<IAttachmentStorage, LocalAttachmentStorage>();
builder.Services.AddSingleton<AttachmentRetentionSweep>();
// The retention sweep is off under integration tests, which drive AttachmentRetentionSweep directly.
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<AttachmentRetentionService>();

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
builder.Services.AddDbContext<IntegrationStore>(options => options.UseNpgsql(connection,
    postgres => postgres.MigrationsHistoryTable("__IntegrationsMigrations", "integrations")));
builder.Services.AddScoped<MembershipAccess>();
builder.Services.AddScoped<InvitationService>();
builder.Services.AddScoped<RoleCapabilityService>();
builder.Services.AddScoped<TeamService>();
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddScoped<IdentityAuditLog>();
builder.Services.AddScoped<MembershipManagementService>();
builder.Services.AddScoped<SessionAuthentication>();
builder.Services.AddScoped<IWorkOperations, EfWorkOperations>();
builder.Services.AddScoped<IResidentMessenger, EfResidentMessenger>();
builder.Services.AddScoped<IAutomationEngine, EfAutomationEngine>();
builder.Services.AddScoped<IGlobalSearch, EfGlobalSearch>();
builder.Services.AddScoped<IRepeatRepairDetector, EfRepeatRepairDetector>();
builder.Services.AddScoped<IAttentionQueue, EfAttentionQueue>();
builder.Services.AddScoped<IOutbox, EfOutbox>();
builder.Services.AddSingleton<ITemplateRenderer, TemplateRenderer>();
builder.Services.AddSingleton<IIntegrationAdapter, MockIntegrationAdapter>();
builder.Services.AddSingleton<IIntegrationCatalog, IntegrationCatalog>();
builder.Services.AddScoped<IIntegrationOperations, EfIntegrationOperations>();
builder.Services.AddSingleton<IPaymentGateway, ConfiguredPaymentGateway>();
builder.Services.AddScoped<IPreventiveMaintenancePlanSource, EfPreventiveMaintenancePlanSource>();
builder.Services.AddScoped<IPreventiveWorkOccurrenceSink, EfPreventiveWorkOccurrenceSink>();
builder.Services.AddScoped<PreventiveMaintenanceService>();
builder.Services.AddScoped<IPreventiveMaintenanceGenerator>(sp => sp.GetRequiredService<PreventiveMaintenanceService>());
builder.Services.AddHttpClient();
var communicationsOptions = builder.Configuration.GetSection(CommunicationsOptions.SectionName).Get<CommunicationsOptions>() ?? new CommunicationsOptions();
communicationsOptions.Validate(builder.Environment.IsProduction() || builder.Environment.IsStaging());
builder.Services.AddSingleton(communicationsOptions);
builder.Services.AddSingleton<ISentMessageLog, InMemorySentMessageLog>();
if (communicationsOptions.SmsProvider.Equals("Twilio", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IMessageSender, TwilioMessageSender>();
else
    builder.Services.AddSingleton<IMessageSender, MockSmsSender>();
if (communicationsOptions.EmailProvider.Equals("SendGrid", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IMessageSender, SendGridMessageSender>();
else
    builder.Services.AddSingleton<IMessageSender, MockEmailSender>();
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
    // 10/min/IP is the brute-force control for the internet-facing deployment. Under the
    // Development posture (local runs and the CI e2e job, which logs in once per spec and
    // retries) that ceiling is just an obstacle, so it is lifted there; "Testing" (the
    // integration suite, which asserts the ceiling) and Production keep the strict value.
    var loginAttemptsPerMinute = builder.Environment.IsDevelopment() ? 200 : 10;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddHostedService<RuntimeDatabaseGuard>();
builder.Services.AddHealthChecks().AddCheck<DatabaseReadiness>("database");

var app = builder.Build();
app.UseExceptionHandler();
app.UseForwardedHeaders();
app.UseMiddleware<RequestObservabilityMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHsts();
// The API only ever returns JSON. Lock everything else down on every response.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-Frame-Options"] = "DENY";
    headers["Cross-Origin-Resource-Policy"] = "same-origin";
    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!context.Request.Path.StartsWithSegments("/api/communications/provider-callback") &&
            // FS-S08: the payment processor cannot hold a CSRF token either; the callback is
            // HMAC-signed instead (BillingEndpoints.cs, MapPaymentCallback).
            !context.Request.Path.StartsWithSegments("/api/billing/payment-callback") &&
            !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
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
app.MapGet("/health/metrics", () => Results.Ok(PropFlowObservability.Snapshot()));
app.MapHealthChecks("/health/ready");
app.MapOpenApi().RequireAuthorization();
app.MapSessionEndpoints();
app.MapSessionManagementEndpoints();
app.MapWorkEndpoints();
app.MapReferenceEndpoints();
app.MapMarketingEndpoints();
app.MapLeasingEndpoints();
app.MapBillingEndpoints();
app.MapResidentPortalEndpoints();
app.MapAccountingEndpoints();
app.MapAnnouncementEndpoints();
app.MapResidentAnnouncementEndpoints();
app.MapCommunicationEndpoints();
app.MapCategoryEndpoints();
app.MapCustomFieldEndpoints();
app.MapResidentEndpoints();
app.MapAssetEndpoints();
app.MapComplianceEndpoints();
app.MapAttachmentEndpoints();
app.MapSearchEndpoints();
app.MapAttentionEndpoints();
app.MapSavedViewEndpoints();
app.MapIntegrationEndpoints();
app.MapAutomationEndpoints();
app.MapProviderCallbackEndpoints();
app.MapInvitationEndpoints();
app.MapRoleCapabilityEndpoints();
app.MapTeamEndpoints();
app.MapMembershipManagementEndpoints();
app.MapAuditEndpoints();
app.Run();

public partial class Program { }
