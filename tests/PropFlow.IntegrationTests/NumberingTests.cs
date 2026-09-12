using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PropFlow.IntegrationTests;

// PF-S03.04 - numbering configuration and the atomic allocator wired into work creation.
[Collection("PostgreSQL")]
public sealed class NumberingTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Work_created_before_numbering_is_configured_has_no_display_number()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var created = await s.Client.PostAsJsonAsync("/api/work/", new { title = "Before numbering", propertyId = s.PropertyA });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("item").GetProperty("displayNumber").ValueKind is JsonValueKind.Null);
    }

    [Fact]
    public async Task Configuring_a_scheme_then_creating_work_assigns_a_formatted_number()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var configured = await s.Client.PutAsJsonAsync("/api/settings/numbering/WorkItem", new { prefix = "WO-", width = 5 });
        Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
        var configuredBody = await configured.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("WO-00001", configuredBody.GetProperty("nextFormatted").GetString());

        var created = await s.Client.PostAsJsonAsync("/api/work/", new { title = "After numbering", propertyId = s.PropertyA });
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("WO-00001", body.GetProperty("item").GetProperty("displayNumber").GetString());
    }

    [Fact]
    public async Task Consecutive_work_items_get_consecutive_numbers()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        await s.Client.PutAsJsonAsync("/api/settings/numbering/WorkItem", new { prefix = "WO-", width = 3 });

        var first = await (await s.Client.PostAsJsonAsync("/api/work/", new { title = "First", propertyId = s.PropertyA })).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await s.Client.PostAsJsonAsync("/api/work/", new { title = "Second", propertyId = s.PropertyA })).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("WO-001", first.GetProperty("item").GetProperty("displayNumber").GetString());
        Assert.Equal("WO-002", second.GetProperty("item").GetProperty("displayNumber").GetString());
    }

    [Fact]
    public async Task Reconfiguring_a_scheme_changes_prefix_and_width_but_not_the_next_value()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        await s.Client.PutAsJsonAsync("/api/settings/numbering/WorkItem", new { prefix = "WO-", width = 5 });
        await (await s.Client.PostAsJsonAsync("/api/work/", new { title = "Consumes 1", propertyId = s.PropertyA })).Content.ReadAsStringAsync();

        var reconfigured = await s.Client.PutAsJsonAsync("/api/settings/numbering/WorkItem", new { prefix = "INC-", width = 3 });
        var body = await reconfigured.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INC-", body.GetProperty("prefix").GetString());
        Assert.Equal(3, body.GetProperty("width").GetInt32());
        Assert.Equal("INC-002", body.GetProperty("nextFormatted").GetString());
    }

    [Fact]
    public async Task Concurrent_work_creation_never_allocates_the_same_number_twice()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        await s.Client.PutAsJsonAsync("/api/settings/numbering/WorkItem", new { prefix = "WO-", width = 5 });

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(i => s.Client.PostAsJsonAsync("/api/work/", new { title = $"Concurrent {i}", propertyId = s.PropertyA })));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        var numbers = new List<string>();
        foreach (var response in responses)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            numbers.Add(body.GetProperty("item").GetProperty("displayNumber").GetString()!);
        }
        Assert.Equal(10, numbers.Distinct().Count());
    }

    [Fact]
    public async Task Read_only_users_can_read_but_not_configure_numbering()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);

        var list = await s.Client.GetAsync("/api/settings/numbering/");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var write = await s.Client.PutAsJsonAsync("/api/settings/numbering/WorkItem", new { prefix = "WO-", width = 5 });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task An_invalid_width_is_a_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var response = await s.Client.PutAsJsonAsync("/api/settings/numbering/WorkItem", new { prefix = "WO-", width = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
