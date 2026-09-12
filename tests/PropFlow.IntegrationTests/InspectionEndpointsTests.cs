using System.Net;
using System.Net.Http.Json;
using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class InspectionEndpointsTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Read_only_cannot_create_inspection_template()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);
        var response = await s.Client.PostAsJsonAsync("/api/inspection-templates", new { name = "Denied", checklist = new[] { "Walls" } });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Inspection_template_list_is_tenant_scoped()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var foreignId = Guid.NewGuid();
        await using (var b = s.AdminStore(s.OrganizationB))
        {
            b.InspectionTemplates.Add(new InspectionTemplate(s.OrganizationB, foreignId, "Other tenant", 1, ["Walls"], s.AdminB, DateTimeOffset.UtcNow));
            await b.SaveChangesAsync();
        }
        await s.LoginAsync();
        using var http = await s.Client.GetAsync("/api/inspection-templates/");
        var body = await http.Content.ReadAsStringAsync();
        Assert.True(http.IsSuccessStatusCode, $"{(int)http.StatusCode}: {body}");
        var response = System.Text.Json.JsonSerializer.Deserialize<InspectionTemplate[]>(body);
        Assert.DoesNotContain(response!, x => x.Id == foreignId);
    }
}
