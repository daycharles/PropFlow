using System.Net;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class ObservabilityTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Every_response_exposes_a_trace_id()
    {
        await using var scenario = await fixture.CreateScenarioAsync();
        using var response = await scenario.Client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var traceId = Assert.Single(response.Headers.GetValues("X-Trace-ID"));
        Assert.NotEmpty(traceId);
    }

    [Fact]
    public async Task Metrics_endpoint_reports_handled_requests()
    {
        await using var scenario = await fixture.CreateScenarioAsync();
        using var response = await scenario.Client.GetAsync("/health/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(body.RootElement.GetProperty("requests").GetInt64() > 0);
        Assert.True(body.RootElement.TryGetProperty("averageDurationMs", out _));
    }
}
