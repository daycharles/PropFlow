using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class SavedViewEndpointsTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Saved_views_are_crud_and_scoped_to_the_current_user()
    {
        await using var scenario = await fixture.CreateScenarioAsync();
        await scenario.LoginAsync();

        var create = await scenario.Client.PostAsJsonAsync("/api/saved-views/", new
        {
            name = "Open pest control", filters = new { status = "Open", search = "pest" },
            columns = new[] { "title", "status", "property" }, isDefault = true
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var saved = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = saved.GetProperty("id").GetGuid();
        Assert.True(saved.GetProperty("isDefault").GetBoolean());

        var update = await scenario.Client.PutAsJsonAsync($"/api/saved-views/{id}", new
        {
            name = "Scheduled pest control", filters = new { status = "Scheduled" },
            columns = new[] { "title" }, isDefault = false
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        await scenario.LoginAsync(reader: true);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Client.GetAsync($"/api/saved-views/{id}")).StatusCode);
        Assert.Empty(await scenario.Client.GetFromJsonAsync<JsonElement[]>("/api/saved-views/") ?? []);

        await scenario.LoginAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.Client.DeleteAsync($"/api/saved-views/{id}")).StatusCode);
        Assert.Empty(await scenario.Client.GetFromJsonAsync<JsonElement[]>("/api/saved-views/") ?? []);
    }

    [Fact]
    public async Task Only_one_default_view_is_kept_per_user()
    {
        await using var scenario = await fixture.CreateScenarioAsync();
        await scenario.LoginAsync();
        foreach (var name in new[] { "First", "Second" })
        {
            var response = await scenario.Client.PostAsJsonAsync("/api/saved-views/", new
            {
                name, filters = new { }, columns = new[] { "title" }, isDefault = true
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var views = await scenario.Client.GetFromJsonAsync<JsonElement[]>("/api/saved-views/");
        Assert.Equal("Second", Assert.Single(views!, x => x.GetProperty("isDefault").GetBoolean()).GetProperty("name").GetString());
    }
}
