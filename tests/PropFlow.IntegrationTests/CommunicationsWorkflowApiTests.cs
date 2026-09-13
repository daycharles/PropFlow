using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Domain.Communications;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class CommunicationsWorkflowApiTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Campaign_publish_fans_out_only_to_consented_recipients_and_is_idempotent()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var create = await s.Client.PostAsJsonAsync("/api/communication/campaigns", new { name = "Rent reminder", channel = "Sms", subject = (string?)null, body = "Reminder" });
        Assert.True(create.StatusCode == HttpStatusCode.Created, $"Campaign create failed: {await create.Content.ReadAsStringAsync()}");
        var campaign = await create.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var id = campaign.GetProperty("id").GetGuid();
        var first = await s.Client.PostAsJsonAsync($"/api/communication/campaigns/{id}/publish", new { residentIds = new[] { s.ResidentA } });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await s.Client.PostAsJsonAsync($"/api/communication/campaigns/{id}/publish", new { residentIds = new[] { s.ResidentA } });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await using var comms = s.Comms(s.OrganizationA);
        Assert.Single(await comms.OutboxMessages.Where(x => x.IdempotencyKey.StartsWith($"campaign:{id:N}")).ToListAsync());
    }

    [Fact]
    public async Task Read_only_role_cannot_manage_campaigns_or_document_templates()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsJsonAsync("/api/communication/campaigns", new { name = "Nope", channel = "Sms", subject = "", body = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsJsonAsync("/api/communication/document-templates", new { name = "Nope", content = "x" })).StatusCode);
    }
}
