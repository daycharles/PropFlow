using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Infrastructure.Identity;
using PropFlow.Infrastructure.Persistence;

if (args.Length != 1 || args[0] is not ("migrate" or "configure-runtime" or "bootstrap"))
{
    Console.Error.WriteLine("Usage: PropFlow.Admin migrate | configure-runtime | bootstrap. See docs/local-development.md.");
    return 1;
}
try
{
    var admin = Required("ConnectionStrings__Admin");
    if (args[0] == "migrate")
    {
        await DatabaseProvisioner.MigrateAsync(admin);
        Console.WriteLine("Identity and operations migrations applied.");
        return 0;
    }
    if (args[0] == "configure-runtime")
    {
        await DatabaseProvisioner.ConfigureRuntimeAsync(admin, Required("Runtime__Password"));
        Console.WriteLine("Restricted propflow_app role configured. No credentials are printed.");
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
