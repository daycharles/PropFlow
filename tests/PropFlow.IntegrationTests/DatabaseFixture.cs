using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using PropFlow.Domain.Assets;
using PropFlow.Domain.Communications;
using PropFlow.Domain.People;
using PropFlow.Domain.Properties;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Identity;
using PropFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PropFlow.IntegrationTests;

[CollectionDefinition("PostgreSQL")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17.11")
        .WithDatabase("propflow_tests").WithUsername("postgres").WithPassword(Guid.NewGuid().ToString("N")).Build();
    public string AdminConnection => database.GetConnectionString();
    public string RuntimeConnection { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await DatabaseProvisioner.MigrateAsync(AdminConnection);
        var password = Guid.NewGuid().ToString("N");
        await DatabaseProvisioner.ConfigureRuntimeAsync(AdminConnection, password);
        RuntimeConnection = new NpgsqlConnectionStringBuilder(AdminConnection)
        {
            Username = "propflow_app", Password = password
        }.ConnectionString;
    }
    public Task DisposeAsync() => database.DisposeAsync().AsTask();

    public async Task<Scenario> CreateScenarioAsync()
    {
        var scenario = new Scenario(this);
        await scenario.SeedAsync();
        return scenario;
    }
}

public sealed class ApplicationFactory(string connection) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", connection);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
    }
}

public sealed class Scenario : IAsyncDisposable
{
    private readonly DatabaseFixture fixture;
    public ApplicationFactory Factory { get; }
    public HttpClient Client { get; }
    public Guid OrganizationA { get; } = Guid.NewGuid();
    public Guid OrganizationB { get; } = Guid.NewGuid();
    public Guid AdminA { get; } = Guid.NewGuid();
    public Guid AdminB { get; } = Guid.NewGuid();
    public Guid ReaderA { get; } = Guid.NewGuid();
    public Guid WorkA { get; } = Guid.NewGuid();
    public Guid WorkB { get; } = Guid.NewGuid();
    public Guid VendorA { get; } = Guid.NewGuid();
    public Guid VendorB { get; } = Guid.NewGuid();
    public Guid PortfolioA { get; } = Guid.NewGuid();
    public Guid PortfolioB { get; } = Guid.NewGuid();
    public Guid PropertyA { get; } = Guid.NewGuid();
    public Guid PropertyB { get; } = Guid.NewGuid();
    public Guid SpaceA { get; } = Guid.NewGuid();
    public Guid SpaceB { get; } = Guid.NewGuid();
    public Guid ResidentA { get; } = Guid.NewGuid();
    public Guid ResidentB { get; } = Guid.NewGuid();
    public Guid AssetA { get; } = Guid.NewGuid();
    public Guid AssetB { get; } = Guid.NewGuid();
    public string Password { get; } = $"A!a9{Guid.NewGuid():N}";
    public string SlugA { get; } = $"org-a-{Guid.NewGuid():N}"[..20];
    public string SlugB { get; } = $"org-b-{Guid.NewGuid():N}"[..20];
    public string EmailA => $"{AdminA:N}@example.test";
    public string EmailB => $"{AdminB:N}@example.test";
    public string ReaderEmail => $"{ReaderA:N}@example.test";

    private string SlugFor(Guid organization) =>
        organization == OrganizationA ? SlugA
        : organization == OrganizationB ? SlugB
        : $"missing-{organization:N}"[..20];

    public Scenario(DatabaseFixture fixture)
    {
        this.fixture = fixture;
        Factory = new ApplicationFactory(fixture.RuntimeConnection);
        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
    }

    public async Task SeedAsync()
    {
        await using var identity = Identity();
        identity.Organizations.AddRange(
            new Organization { Id = OrganizationA, Name = "Harbor Management", Slug = SlugA },
            new Organization { Id = OrganizationB, Name = "Other Management", Slug = SlugB });
        AddUser(identity, AdminA, EmailA, OrganizationA, "Organization Admin");
        AddUser(identity, AdminB, EmailB, OrganizationB, "Organization Admin");
        AddUser(identity, ReaderA, ReaderEmail, OrganizationA, "Read Only");
        await identity.SaveChangesAsync();
        var moveIn = new DateOnly(2026, 1, 15);
        await using var a = AdminStore(OrganizationA);
        a.Vendors.Add(new Vendor(OrganizationA, VendorA, "Tidewater Pest Services"));
        a.Portfolios.Add(new Portfolio(OrganizationA, PortfolioA, "Norfolk Residential"));
        a.Properties.Add(new Property(OrganizationA, PropertyA, PortfolioA, "Harbor Point Apartments", "America/New_York"));
        a.Spaces.Add(new Space(OrganizationA, SpaceA, PropertyA, null, "101"));
        var residentA = new Resident(OrganizationA, ResidentA, "Dana Reyes", "dana@example.test", "+15550100101");
        residentA.SetConsent(MessageChannel.Sms, granted: true, moveIn.ToDateTime(TimeOnly.MinValue));
        a.Residents.Add(residentA);
        a.Occupancies.Add(new Occupancy(OrganizationA, Guid.NewGuid(), ResidentA, SpaceA, moveIn));
        var assetA = new Asset(OrganizationA, AssetA, PropertyA, SpaceA, AssetKind.Hvac, "Rooftop HVAC 1");
        assetA.SetLifecycle(new DateOnly(2018, 6, 1), new DateOnly(2028, 6, 1), 15);
        assetA.RecordCondition(AssetCondition.Good);
        a.Assets.Add(assetA);
        a.WorkItems.Add(new WorkItem(OrganizationA, WorkA, "Pest control", PropertyA, AdminA));
        await a.SaveChangesAsync();
        await using var b = AdminStore(OrganizationB);
        b.Vendors.Add(new Vendor(OrganizationB, VendorB, "Other Vendor"));
        b.Portfolios.Add(new Portfolio(OrganizationB, PortfolioB, "Other Portfolio"));
        b.Properties.Add(new Property(OrganizationB, PropertyB, PortfolioB, "Other Property", "America/Chicago"));
        b.Spaces.Add(new Space(OrganizationB, SpaceB, PropertyB, null, "B-1"));
        b.Residents.Add(new Resident(OrganizationB, ResidentB, "Sam Okafor", "sam@example.test", "+15550200202"));
        b.Occupancies.Add(new Occupancy(OrganizationB, Guid.NewGuid(), ResidentB, SpaceB, moveIn));
        b.Assets.Add(new Asset(OrganizationB, AssetB, PropertyB, null, AssetKind.WaterHeater, "Basement water heater"));
        b.WorkItems.Add(new WorkItem(OrganizationB, WorkB, "Private work", PropertyB, AdminB));
        await b.SaveChangesAsync();
    }

    private void AddUser(IdentityStore store, Guid id, string email, Guid organization, string role)
    {
        var user = new ApplicationUser
        {
            Id = id, UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true
        };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Password);
        store.Users.Add(user);
        store.Memberships.Add(new OrganizationMembership { OrganizationId = organization, UserId = id, Role = role });
    }

    public IdentityStore Identity() => DatabaseProvisioner.CreateIdentityStore(fixture.AdminConnection);
    public OperationsStore AdminStore(Guid organization) => DatabaseProvisioner.CreateOperationsStore(fixture.AdminConnection, organization);
    public OperationsStore Store(Guid organization) => DatabaseProvisioner.CreateOperationsStore(fixture.RuntimeConnection, organization);
    public PropFlow.Infrastructure.Communications.CommunicationsStore Comms(Guid organization) =>
        DatabaseProvisioner.CreateCommunicationsStore(fixture.RuntimeConnection, organization);
    public PropFlow.Infrastructure.Communications.CommunicationsStore CommsAsAdmin(Guid organization) =>
        DatabaseProvisioner.CreateCommunicationsStore(fixture.AdminConnection, organization);

    public async Task<string> RefreshCsrfAsync()
    {
        var json = await Client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var token = json.GetProperty("token").GetString()!;
        Client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        return token;
    }
    public async Task<HttpResponseMessage> AttemptLoginAsync(string email, Guid organization, string? password = null)
    {
        await RefreshCsrfAsync();
        return await Client.PostAsJsonAsync("/api/auth/login",
            new { organizationSlug = SlugFor(organization), email, password = password ?? Password });
    }
    public async Task LoginAsync(bool reader = false)
    {
        using var response = await AttemptLoginAsync(reader ? ReaderEmail : EmailA, OrganizationA);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await RefreshCsrfAsync();
    }
    public Task<HttpResponseMessage> AssignAsync(Guid work, Guid vendor) =>
        Client.PostAsJsonAsync($"/api/work/{work}/vendor", new { vendorId = vendor });

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
    }
}
