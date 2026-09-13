using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class ReportingEndpointsTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Reports_are_tenant_scoped_and_csv_matches_json_projection()
    {
        await using var s = await fixture.CreateScenarioAsync(); await s.LoginAsync();
        using var jsonResponse = await s.Client.GetAsync("/api/reports/operational");
        Assert.Equal(HttpStatusCode.OK, jsonResponse.StatusCode);
        var json = await jsonResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("totals").GetProperty("count").GetInt32());
        Assert.DoesNotContain(s.WorkB.ToString(), json.GetRawText(), StringComparison.OrdinalIgnoreCase);
        using var csv = await s.Client.GetAsync("/api/reports/operational/export");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode); Assert.Equal("text/csv; charset=utf-8", csv.Content.Headers.ContentType?.ToString());
        var text = await csv.Content.ReadAsStringAsync(); Assert.Contains("report,json", text); Assert.Contains(s.WorkA.ToString(), text);
        using var bounded = await s.Client.GetAsync("/api/reports/operational?take=100000");
        var boundedJson = await bounded.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(boundedJson.GetProperty("rows").GetArrayLength() <= 10000);
    }

    [Fact]
    public async Task Read_only_can_read_reports_but_cannot_schedule_delivery()
    {
        await using var s = await fixture.CreateScenarioAsync(); await s.LoginAsync(reader: true);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/reports/portfolio")).StatusCode);
        using var response = await s.Client.PostAsJsonAsync("/api/reports/schedules", new { name = "Daily", kind = "portfolio", frequency = "Daily", recipient = "ops@example.test" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_and_run_a_schedule_with_delivery_records()
    {
        await using var s = await fixture.CreateScenarioAsync(); await s.LoginAsync();
        using var create = await s.Client.PostAsJsonAsync("/api/reports/schedules", new { name = "Daily portfolio", kind = "portfolio", frequency = "Daily", recipient = "ops@example.test" });
        Assert.True(create.StatusCode == HttpStatusCode.Created, await create.Content.ReadAsStringAsync()); var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var run = await s.Client.PostAsync($"/api/reports/schedules/{id}/run", null); Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        using var retry = await s.Client.PostAsync($"/api/reports/schedules/{id}/run", null); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        await using var store = s.Store(s.OrganizationA); Assert.Equal(1, store.ReportDeliveries.Count(x => x.ScheduleId == id));
    }
}
