using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class AdministrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Api_refuses_owner_database_credentials()
    {
        await using var factory = new ApplicationFactory(fixture.AdminConnection);
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("non-owner database role", exception.Message);
    }

    [Fact]
    public async Task Bootstrap_creates_login_and_refuses_to_reset_existing_account()
    {
        var email = $"bootstrap-{Guid.NewGuid():N}@example.test";
        var password = $"A!a9{Guid.NewGuid():N}";
        var first = await RunBootstrapAsync(email, password);
        Assert.True(first.ExitCode == 0, first.Output);
        string? slug;
        await using (var identity = PropFlow.Infrastructure.Persistence.DatabaseProvisioner.CreateIdentityStore(fixture.AdminConnection))
        {
            var user = await identity.Users.SingleAsync(x => x.Email == email);
            var membership = await identity.Memberships.SingleAsync(x => x.UserId == user.Id);
            slug = (await identity.Organizations.SingleAsync(x => x.Id == membership.OrganizationId)).Slug;
            Assert.Equal("Organization Admin", membership.Role);
            Assert.False(string.IsNullOrWhiteSpace(slug));
            Assert.NotEqual(password, user.PasswordHash);
        }
        var repeat = await RunBootstrapAsync(email, "Different!Password99");
        Assert.Equal(1, repeat.ExitCode);
        Assert.Contains("already exists", repeat.Output);
        await using var factory = new ApplicationFactory(fixture.RuntimeConnection);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var login = await client.PostAsJsonAsync("/api/auth/login", new { organizationSlug = slug, email, password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    private async Task<(int ExitCode, string Output)> RunBootstrapAsync(string email, string password)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PropFlow.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var configuration = typeof(AdministrationTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var assembly = Path.Combine(directory.FullName, "tools", "PropFlow.Admin", "bin", configuration, "net10.0", "PropFlow.Admin.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("bootstrap");
        start.Environment["ConnectionStrings__Admin"] = fixture.AdminConnection;
        start.Environment["Bootstrap__Organization"] = "Provisioning test";
        start.Environment["Bootstrap__Email"] = email;
        start.Environment["Bootstrap__Password"] = password;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }
}
