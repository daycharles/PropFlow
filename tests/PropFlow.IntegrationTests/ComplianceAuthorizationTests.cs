using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class ComplianceAuthorizationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Reader_cannot_create_or_transition_compliance_records()
    {
        await using var scenario = await fixture.CreateScenarioAsync();
        await scenario.LoginAsync(reader: true);

        var violation = await scenario.Client.PostAsJsonAsync("/api/compliance/violations",
            new { propertyId = scenario.PropertyA, title = "Blocked write", identifiedOn = "2026-01-01", severity = 2 });
        Assert.Equal(HttpStatusCode.Forbidden, violation.StatusCode);

        var remediation = await scenario.Client.PostAsJsonAsync("/api/compliance/remediations",
            new { propertyId = scenario.PropertyA, action = "Blocked write", dueOn = "2026-01-10" });
        Assert.Equal(HttpStatusCode.Forbidden, remediation.StatusCode);

        var evidence = await scenario.Client.PostAsJsonAsync("/api/compliance/evidence",
            new { attachmentId = Guid.NewGuid(), violationId = Guid.NewGuid(), retentionDays = 30 });
        Assert.Equal(HttpStatusCode.Forbidden, evidence.StatusCode);
    }
}
