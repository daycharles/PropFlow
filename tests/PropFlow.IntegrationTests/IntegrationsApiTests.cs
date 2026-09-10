using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class IntegrationsApiTests(DatabaseFixture fixture)
{
    private static async Task<Guid> CreateMockConnection(Scenario s)
    {
        var response = await s.Client.PostAsJsonAsync("/api/integrations",
            new { sourceSystem = "mock", displayName = "Mock property system" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Available_sources_lists_the_mock_adapter()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var sources = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/integrations/sources");
        Assert.Contains(sources!, x => x.GetProperty("sourceSystem").GetString() == "mock");
    }

    [Fact]
    public async Task Creating_the_same_source_twice_is_a_conflict_and_an_unknown_source_is_a_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        await CreateMockConnection(s);

        var duplicate = await s.Client.PostAsJsonAsync("/api/integrations",
            new { sourceSystem = "mock", displayName = "Second try" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var unknown = await s.Client.PostAsJsonAsync("/api/integrations",
            new { sourceSystem = "yardi", displayName = "Yardi" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task A_sync_records_every_external_record_and_re_syncing_reports_no_changes()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var id = await CreateMockConnection(s);

        var first = await (await s.Client.PostAsync($"/api/integrations/{id}/sync", null))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", first.GetProperty("outcome").GetString());
        var seen = first.GetProperty("seen").GetInt32();
        Assert.True(seen > 0);
        Assert.Equal(seen, first.GetProperty("added").GetInt32());
        Assert.Equal(0, first.GetProperty("updated").GetInt32());

        var second = await (await s.Client.PostAsync($"/api/integrations/{id}/sync", null))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, second.GetProperty("added").GetInt32());
        Assert.Equal(0, second.GetProperty("updated").GetInt32());

        var records = await s.Client.GetFromJsonAsync<JsonElement>($"/api/integrations/{id}/records");
        var items = records.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(seen, records.GetProperty("totalCount").GetInt32());
        Assert.Equal(seen, items.Count);
        Assert.All(items, r => Assert.Equal("Synced", r.GetProperty("syncState").GetString()));

        var firstPage = await s.Client.GetFromJsonAsync<JsonElement>($"/api/integrations/{id}/records?page=1&pageSize=2");
        Assert.Equal(2, firstPage.GetProperty("items").GetArrayLength());
        Assert.Equal(seen, firstPage.GetProperty("totalCount").GetInt32());

        var health = await s.Client.GetFromJsonAsync<JsonElement>($"/api/integrations/{id}");
        Assert.False(string.IsNullOrEmpty(health.GetProperty("lastSucceededAt").GetString()));
        Assert.Equal(0, health.GetProperty("consecutiveFailures").GetInt32());
        Assert.Equal(seen, health.GetProperty("trackedRecords").GetInt32());
        Assert.Equal(0, health.GetProperty("failedRecords").GetInt32());
    }

    [Fact]
    public async Task A_disabled_connection_refuses_to_sync()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var id = await CreateMockConnection(s);

        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.PostAsync($"/api/integrations/{id}/disable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsync($"/api/integrations/{id}/sync", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.PostAsync($"/api/integrations/{id}/enable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/integrations/{id}/sync", null)).StatusCode);
    }

    [Fact]
    public async Task Unknown_connection_ids_are_404s()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/integrations/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/integrations/{Guid.NewGuid()}/records")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsync($"/api/integrations/{Guid.NewGuid()}/sync", null)).StatusCode);
    }

    [Fact]
    public async Task Only_an_integration_manager_can_use_the_endpoints()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/api/integrations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsJsonAsync("/api/integrations",
            new { sourceSystem = "mock", displayName = "Nope" })).StatusCode);
    }

    [Fact]
    public async Task Connections_and_record_links_are_confined_to_the_current_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var id = await CreateMockConnection(s);
        await s.Client.PostAsync($"/api/integrations/{id}/sync", null);

        // Org B, through its own restricted store, sees nothing of org A's integration data.
        await using var storeB = s.Integrations(s.OrganizationB);
        Assert.Empty(await storeB.Connections.ToListAsync());
        Assert.Empty(await storeB.RecordLinks.ToListAsync());
        Assert.Empty(await storeB.Connections.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Rls_with_check_rejects_a_cross_tenant_record_link_insert()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var id = await CreateMockConnection(s);

        await using var storeA = s.Integrations(s.OrganizationA);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => storeA.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO integrations."RecordLinks"
              ("OrganizationId", "Id", "ConnectionId", "Kind", "ExternalId", "SyncState")
            VALUES ({s.OrganizationB}, {Guid.NewGuid()}, {id}, 'Property', 'forged', 'Pending')
            """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task Runtime_role_without_a_tenant_context_sees_no_integration_rows()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        await s.Client.PostAsync($"/api/integrations/{await CreateMockConnection(s)}/sync", null);

        await using var connection = new NpgsqlConnection(fixture.RuntimeConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM integrations.\"Connections\"";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }
}
