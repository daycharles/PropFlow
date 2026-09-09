using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class SecurityHeaderTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Every_response_carries_the_lockdown_headers()
    {
        await using var s = await fixture.CreateScenarioAsync();
        using var response = await s.Client.GetAsync("/health/live");

        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Single(response, "Referrer-Policy"));
        Assert.Equal("DENY", Single(response, "X-Frame-Options"));
        Assert.Equal("same-origin", Single(response, "Cross-Origin-Resource-Policy"));
        Assert.Contains("default-src 'none'", Single(response, "Content-Security-Policy"));
    }

    private static string Single(HttpResponseMessage response, string header) =>
        Assert.Single(response.Headers.GetValues(header));
}
