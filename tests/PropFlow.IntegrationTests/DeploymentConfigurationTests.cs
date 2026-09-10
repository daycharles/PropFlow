using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace PropFlow.IntegrationTests;

public sealed class DeploymentConfigurationTests
{
    [Fact]
    public void Production_requires_verified_postgres_tls()
    {
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PropFlow.Api.DeploymentConfiguration.ValidatePostgresTls(
                "Host=db;Database=propflow;Username=app;Password=secret;Ssl Mode=Require", environment));

        Assert.Contains("Ssl Mode=VerifyFull", exception.Message);
    }

    [Fact]
    public void Production_requires_an_explicit_proxy_allow_list()
    {
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ForwardedHeaders:Enabled"] = "true" }).Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PropFlow.Api.DeploymentConfiguration.ConfigureForwardedHeaders(
                new ForwardedHeadersOptions(), configuration, environment));

        Assert.Contains("KnownProxies", exception.Message);
    }

    private sealed class HostingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "PropFlow";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
