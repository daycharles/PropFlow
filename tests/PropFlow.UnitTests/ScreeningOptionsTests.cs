using Microsoft.Extensions.Configuration;
using PropFlow.Application.Screening;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class ScreeningOptionsTests
{
    [Fact]
    public void Defaults_allow_three_attempts()
    {
        var options = new ScreeningOptions();
        options.Validate();
        Assert.Equal(3, options.MaxAttempts);
        Assert.Equal("Available", options.ProviderMode);
    }

    [Fact]
    public void An_unconfigured_section_falls_back_to_the_defaults()
    {
        var options = new ConfigurationBuilder().Build().GetSection(ScreeningOptions.SectionName).Get<ScreeningOptions>() ?? new ScreeningOptions();
        options.Validate();
        Assert.Equal(3, options.MaxAttempts);
    }

    [Fact]
    public void The_section_binds_max_attempts_and_provider_mode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Screening:MaxAttempts"] = "5",
                ["Screening:ProviderMode"] = "Unavailable"
            }).Build();
        var options = configuration.GetSection(ScreeningOptions.SectionName).Get<ScreeningOptions>()!;
        options.Validate();
        Assert.Equal(5, options.MaxAttempts);
        Assert.Equal("Unavailable", options.ProviderMode);
    }

    // A zero or negative ceiling would make ScreeningRequest.RecordFailure throw
    // ArgumentOutOfRangeException on the first failure, which would surface as a 500 rather than
    // as the configuration error it is. Refusing it at startup is the cheaper place to fail.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_ceiling_below_one_is_refused_at_startup(int maxAttempts)
    {
        var options = new ScreeningOptions { MaxAttempts = maxAttempts };
        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("MaxAttempts", exception.Message);
    }

    [Fact]
    public void One_attempt_is_a_legal_ceiling_meaning_no_retries()
    {
        var options = new ScreeningOptions { MaxAttempts = 1 };
        options.Validate();
    }
}
