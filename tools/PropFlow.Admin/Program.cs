using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Domain.People;
using PropFlow.Domain.Properties;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Identity;
using PropFlow.Infrastructure.Persistence;

if (args.Length != 1 || args[0] is not ("migrate" or "configure-runtime" or "bootstrap" or "seed-demo"))
{
    Console.Error.WriteLine("Usage: PropFlow.Admin migrate | configure-runtime | bootstrap | seed-demo. See docs/local-development.md.");
    return 1;
}
try
{
    var admin = Required("ConnectionStrings__Admin");
    if (args[0] == "migrate")
    {
        await DatabaseProvisioner.MigrateAsync(admin);
        Console.WriteLine("Identity, operations, communications and integrations migrations applied.");
        return 0;
    }
    if (args[0] == "configure-runtime")
    {
        await DatabaseProvisioner.ConfigureRuntimeAsync(admin, Required("Runtime__Password"));
        Console.WriteLine("Restricted propflow_app role configured. No credentials are printed.");
        return 0;
    }
    if (args[0] == "seed-demo")
    {
        await DemoSeeder.SeedAsync(admin, Required("Demo__Password"));
        Console.WriteLine("Demo organizations and sample operations data are ready. Sign in as demo-admin@tidewater.example.test.");
        return 0;
    }
    var name = Required("Bootstrap__Organization").Trim();
    var email = Required("Bootstrap__Email").Trim();
    var password = Required("Bootstrap__Password");
    if (name.Length is < 1 or > 200) throw new ArgumentException("Organization name must contain 1 to 200 characters.");
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddDbContext<IdentityStore>(options => options.UseNpgsql(admin));
    services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
    }).AddEntityFrameworkStores<IdentityStore>();
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var store = scope.ServiceProvider.GetRequiredService<IdentityStore>();
    if (await users.FindByEmailAsync(email) is not null)
        throw new ArgumentException("That user already exists. Bootstrap will not change existing accounts or credentials.");
    await using var transaction = await store.Database.BeginTransactionAsync();
    var slug = OrganizationSlug.From(name);
    if (await store.Organizations.AnyAsync(x => x.Slug == slug))
        slug = $"{slug[..Math.Min(slug.Length, OrganizationSlug.MaxLength - 7)]}-{Guid.NewGuid():N}"[..OrganizationSlug.MaxLength].TrimEnd('-');
    var organization = new Organization { Id = Guid.NewGuid(), Name = name, Slug = slug };
    var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, LockoutEnabled = true };
    var result = await users.CreateAsync(user, password);
    if (!result.Succeeded) throw new ArgumentException(string.Join("; ", result.Errors.Select(x => x.Code)));
    store.Organizations.Add(organization);
    store.Memberships.Add(new OrganizationMembership
    {
        OrganizationId = organization.Id, UserId = user.Id, Role = "Organization Admin"
    });
    await store.SaveChangesAsync();
    await transaction.CommitAsync();
    Console.WriteLine($"Created organization {organization.Id} (slug '{organization.Slug}') and administrator {email}.");
    return 0;
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
catch (Exception)
{
    // Avoid printing database exceptions that can contain credentials or user information.
    Console.Error.WriteLine("Administrative operation failed. Check database access, migration order, and configuration.");
    return 1;
}
static string Required(string name) => Environment.GetEnvironmentVariable(name)
    is { Length: > 0 } value ? value : throw new ArgumentException($"Set {name}.");

internal static class DemoSeeder
{
    private const string PrimarySlug = "tidewater-demo";
    private const string SecondarySlug = "isolation-demo";

    public static async Task SeedAsync(string adminConnection, string password)
    {
        if (password.Length < 12) throw new ArgumentException("Demo__Password must be at least 12 characters.");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<IdentityStore>(options => options.UseNpgsql(adminConnection));
        services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddEntityFrameworkStores<IdentityStore>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityStore>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var primary = await EnsureOrganizationAsync(identity, users, "Tidewater Residential Management", PrimarySlug,
            "demo-admin@tidewater.example.test", password);
        var secondary = await EnsureOrganizationAsync(identity, users, "Isolation Test Management", SecondarySlug,
            "demo-admin@isolation.example.test", password);
        await PropFlow.Admin.TidewaterSeed.SeedAsync(adminConnection, primary.OrganizationId, primary.UserId);
        await SeedOperationsAsync(adminConnection, secondary.OrganizationId, secondary.UserId, "Isolation", "Private Place", "A1");
    }

    private static async Task<(Guid OrganizationId, Guid UserId)> EnsureOrganizationAsync(IdentityStore store, UserManager<ApplicationUser> users,
        string name, string slug, string email, string password)
    {
        var organization = await store.Organizations.SingleOrDefaultAsync(x => x.Slug == slug);
        if (organization is null)
        {
            organization = new Organization { Id = Guid.NewGuid(), Name = name, Slug = slug };
            store.Organizations.Add(organization);
        }
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, LockoutEnabled = true };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded) throw new ArgumentException(string.Join("; ", created.Errors.Select(x => x.Code)));
        }
        if (!await store.Memberships.AnyAsync(x => x.OrganizationId == organization.Id && x.UserId == user.Id))
            store.Memberships.Add(new OrganizationMembership { OrganizationId = organization.Id, UserId = user.Id, Role = "Organization Admin" });
        await store.SaveChangesAsync();
        return (organization.Id, user.Id);
    }

    private static async Task SeedOperationsAsync(string adminConnection, Guid organizationId, Guid creatorId, string portfolioName,
        string propertyName, string spaceCode)
    {
        await using var store = DatabaseProvisioner.CreateOperationsStore(adminConnection, organizationId);
        if (await store.WorkItems.AnyAsync()) return; // Safe to rerun; user-created/demo changes are preserved.
        var portfolioId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var buildingId = Guid.NewGuid();
        var spaceId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var maintenanceId = Guid.NewGuid();
        var pestId = Guid.NewGuid();
        store.Portfolios.Add(new Portfolio(organizationId, portfolioId, portfolioName + " Portfolio"));
        store.Properties.Add(new Property(organizationId, propertyId, portfolioId, propertyName, "America/New_York"));
        store.Buildings.Add(new Building(organizationId, buildingId, propertyId, "Building A"));
        store.Spaces.Add(new Space(organizationId, spaceId, propertyId, buildingId, spaceCode));
        var vendor = new Vendor(organizationId, vendorId, "Tidewater Pest Services");
        vendor.UpdateContact("dispatch@example.test", "555-0100", "Pest control", "Pest control");
        store.Vendors.Add(vendor);
        store.Employees.Add(new Employee(organizationId, employeeId, "Jordan Lee", "jordan@example.test", "555-0101"));
        store.Categories.AddRange(new WorkCategory(organizationId, maintenanceId, "Maintenance", 0),
            new WorkCategory(organizationId, pestId, "Pest control", 1));
        // PF-3.22: cover every WorkStatus and every WorkPriority. Four rows stay in New so the
        // demo/e2e "filter to New, select all, assign vendor" flow always has work to act on.
        var now = DateTimeOffset.UtcNow;
        var seeds = new (string Title, WorkPriority Priority, Guid Category, WorkStatus Status)[]
        {
            ("Replace lobby entry mats", WorkPriority.Low, maintenanceId, WorkStatus.Draft),
            ("Leaking kitchen sink", WorkPriority.Low, maintenanceId, WorkStatus.New),
            ("Pest inspection", WorkPriority.Normal, pestId, WorkStatus.New),
            ("Broken entry light", WorkPriority.High, maintenanceId, WorkStatus.New),
            ("Gas odor investigation", WorkPriority.Critical, maintenanceId, WorkStatus.New),
            ("Quarterly pest treatment", WorkPriority.Normal, pestId, WorkStatus.Assigned),
            ("Roof leak above the top floor", WorkPriority.High, maintenanceId, WorkStatus.Assigned),
            ("Ant swarm in the trash room", WorkPriority.High, pestId, WorkStatus.Scheduled),
            ("Water heater replacement", WorkPriority.Normal, maintenanceId, WorkStatus.InProgress),
            ("Repaint the stairwell", WorkPriority.Low, maintenanceId, WorkStatus.OnHold),
            ("Rodent bait station refresh", WorkPriority.Normal, pestId, WorkStatus.Completed),
            ("Duplicate pest report", WorkPriority.Critical, pestId, WorkStatus.Cancelled)
        };
        var offset = -3;
        foreach (var (title, priority, category, status) in seeds)
        {
            var work = new WorkItem(organizationId, Guid.NewGuid(), title, propertyId, creatorId);
            work.Edit(title, "Seeded demo work item", category, priority);
            work.SetLocation(propertyId, buildingId, spaceId, null);
            work.SetDueDate(now.AddDays(offset++)); // a mix of overdue and upcoming dates for the dueDate sort
            // Draft is reached by never publishing: a WorkItem starts Draft and ChangeStatus refuses a move back to it.
            if (status is not WorkStatus.Draft)
            {
                work.Publish(now);
                if (status is WorkStatus.Assigned or WorkStatus.Scheduled)
                {
                    work.AssignVendor(vendorId);
                    if (status is WorkStatus.Scheduled) work.Schedule(now.AddDays(1), now.AddDays(1).AddHours(2));
                }
                else if (status is WorkStatus.InProgress)
                {
                    work.AssignEmployee(employeeId); // Schedule/InProgress need an assignee; also gives the seed an employee-assigned row.
                    work.ChangeStatus(WorkStatus.InProgress, now);
                }
                else if (status is WorkStatus.OnHold or WorkStatus.Completed or WorkStatus.Cancelled)
                    work.ChangeStatus(status, now);
            }
            store.WorkItems.Add(work);
        }
        await store.SaveChangesAsync();
    }
}
