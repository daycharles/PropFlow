using Microsoft.Extensions.Configuration;
using PropFlow.Application.Communications;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class CommunicationsOptionsTests
{
    [Fact]
    public void Defaults_match_the_documented_values()
    {
        var options = new CommunicationsOptions();

        Assert.Equal(TimeSpan.FromSeconds(10), options.PollInterval);
        Assert.Equal(TimeSpan.FromMinutes(2), options.RetryDelay);
        Assert.Equal(8, options.MaxDeliveryAttempts);
        Assert.Equal(TimeSpan.FromMinutes(5), options.StaleClaimTimeout);
        Assert.Equal("Mock", options.SmsProvider);
        Assert.Equal("Mock", options.EmailProvider);
    }

    [Fact]
    public void Binds_from_the_communications_configuration_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Communications:PollInterval"] = "00:00:05",
                ["Communications:RetryDelay"] = "00:05:00",
                ["Communications:MaxDeliveryAttempts"] = "3",
                ["Communications:StaleClaimTimeout"] = "00:10:00"
            })
            .Build();

        var options = configuration.GetSection(CommunicationsOptions.SectionName).Get<CommunicationsOptions>()!;

        Assert.Equal(TimeSpan.FromSeconds(5), options.PollInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), options.RetryDelay);
        Assert.Equal(3, options.MaxDeliveryAttempts);
        Assert.Equal(TimeSpan.FromMinutes(10), options.StaleClaimTimeout);
    }

    [Fact]
    public void Real_provider_configuration_requires_its_credentials()
    {
        var options = new CommunicationsOptions { SmsProvider = "Twilio" };
        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate(productionOrStaging: true));
        Assert.Contains("Twilio", exception.Message);
    }

    [Fact]
    public void Unknown_provider_is_rejected()
    {
        var options = new CommunicationsOptions { EmailProvider = "Unknown" };
        Assert.Throws<InvalidOperationException>(() => options.Validate(productionOrStaging: false));
    }
}
