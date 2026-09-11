using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class CategoryEndpointTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Create_and_list_a_category()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var create = await s.Client.PostAsJsonAsync("/api/categories", new { name = "Pest Control", sortOrder = 1 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var list = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/categories");
        Assert.Contains(list!, c => c.GetProperty("name").GetString() == "Pest Control");
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("valid name", -1)]
    public async Task Invalid_category_input_is_a_400_not_a_500(string name, int sortOrder)
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var response = await s.Client.PostAsJsonAsync("/api/categories", new { name, sortOrder });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Overlong_category_name_is_a_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var response = await s.Client.PostAsJsonAsync("/api/categories", new { name = new string('x', 300), sortOrder = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Updating_a_category_with_a_null_name_is_a_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var created = await (await s.Client.PostAsJsonAsync("/api/categories", new { name = "Original", sortOrder = 0 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var response = await s.Client.PutAsJsonAsync($"/api/categories/{id}", new { name = (string?)null, sortOrder = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Read_only_role_cannot_create_a_category()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);

        var response = await s.Client.PostAsJsonAsync("/api/categories", new { name = "Nope", sortOrder = 0 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // PF-S03.03: AppliesTo defaults to WorkItem, so every test above (all written before this
    // field existed) still passes unchanged; these cover the field itself.
    [Fact]
    public async Task A_category_created_without_appliesTo_defaults_to_WorkItem()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var created = await (await s.Client.PostAsJsonAsync("/api/categories", new { name = "Legacy caller", sortOrder = 0 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("WorkItem", created.GetProperty("appliesTo").GetString());

        var list = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/categories");
        Assert.Contains(list!, c => c.GetProperty("name").GetString() == "Legacy caller" && c.GetProperty("appliesTo").GetString() == "WorkItem");
    }

    [Fact]
    public async Task A_duplicate_name_for_the_same_entity_type_is_a_409()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var first = await s.Client.PostAsJsonAsync("/api/categories", new { name = "Dup", sortOrder = 0, appliesTo = "WorkItem" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await s.Client.PostAsJsonAsync("/api/categories", new { name = "Dup", sortOrder = 1, appliesTo = "WorkItem" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}
