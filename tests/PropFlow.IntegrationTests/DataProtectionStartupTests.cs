using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class DataProtectionStartupTests(DatabaseFixture fixture)
{
    [Fact]
    public void Production_host_refuses_to_start_without_a_data_protection_key_path()
    {
        using var factory = new ApplicationFactory(fixture.RuntimeConnection);
        var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var exception = Assert.Throws<InvalidOperationException>(() => production.CreateClient());
        Assert.Contains("DataProtection:KeyPath", exception.Message);
    }

    [Fact]
    public async Task Production_host_starts_when_a_key_path_is_configured()
    {
        var keyPath = Directory.CreateTempSubdirectory("propflow-dp-").FullName;
        try
        {
            using var factory = new ApplicationFactory(fixture.RuntimeConnection);
            var production = factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("DataProtection:KeyPath", keyPath);
            });

            using var client = production.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            using var live = await client.GetAsync("/health/live");
            Assert.Equal(System.Net.HttpStatusCode.OK, live.StatusCode);
        }
        finally
        {
            Directory.Delete(keyPath, recursive: true);
        }
    }
}
