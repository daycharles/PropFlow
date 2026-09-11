using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PropFlow.IntegrationTests;

// PF-S03.01 — custom field definitions. Values on a work item are PF-S03.02.
[Collection("PostgreSQL")]
public sealed class CustomFieldTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Admin_can_create_list_update_and_archive_a_custom_field()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var created = await s.Client.PostAsJsonAsync("/api/settings/custom-fields/", new
        {
            key = "warranty_note", name = "Warranty note", appliesTo = "WorkItem", fieldType = "Text",
            options = (string[]?)null, isRequired = false, sortOrder = 0,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var list = await s.Client.GetFromJsonAsync<JsonElement>("/api/settings/custom-fields/");
        Assert.Contains(list.EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);

        var updated = await s.Client.PutAsJsonAsync($"/api/settings/custom-fields/{id}", new
        {
            name = "Warranty details", options = (string[]?)null, isRequired = true, sortOrder = 3,
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Warranty details", body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("isRequired").GetBoolean());

        var archived = await s.Client.PostAsync($"/api/settings/custom-fields/{id}/archive", null);
        Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        var afterArchive = await s.Client.GetFromJsonAsync<JsonElement>("/api/settings/custom-fields/");
        Assert.True(afterArchive.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id).GetProperty("isArchived").GetBoolean());
    }

    [Fact]
    public async Task A_SingleSelect_field_carries_its_options()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var created = await s.Client.PostAsJsonAsync("/api/settings/custom-fields/", new
        {
            key = "priority_tier", name = "Priority tier", appliesTo = "WorkItem", fieldType = "SingleSelect",
            options = new[] { "Gold", "Silver" }, isRequired = false, sortOrder = 0,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["Gold", "Silver"], body.GetProperty("options").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task A_malformed_key_is_a_400_and_a_duplicate_key_is_a_409()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var badKey = await s.Client.PostAsJsonAsync("/api/settings/custom-fields/", new
        {
            key = "Has Space", name = "Bad", appliesTo = "WorkItem", fieldType = "Text",
            options = (string[]?)null, isRequired = false, sortOrder = 0,
        });
        Assert.Equal(HttpStatusCode.BadRequest, badKey.StatusCode);

        var first = await s.Client.PostAsJsonAsync("/api/settings/custom-fields/", new
        {
            key = "dup", name = "First", appliesTo = "WorkItem", fieldType = "Text",
            options = (string[]?)null, isRequired = false, sortOrder = 0,
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await s.Client.PostAsJsonAsync("/api/settings/custom-fields/", new
        {
            key = "dup", name = "Second", appliesTo = "WorkItem", fieldType = "Text",
            options = (string[]?)null, isRequired = false, sortOrder = 0,
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Read_only_users_can_read_but_not_write()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);

        var list = await s.Client.GetAsync("/api/settings/custom-fields/");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var write = await s.Client.PostAsJsonAsync("/api/settings/custom-fields/", new
        {
            key = "note", name = "Note", appliesTo = "WorkItem", fieldType = "Text",
            options = (string[]?)null, isRequired = false, sortOrder = 0,
        });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Custom_fields_are_tenant_scoped()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            admin.CustomFieldDefinitions.Add(new PropFlow.Domain.Configuration.CustomFieldDefinition(
                s.OrganizationA, Guid.NewGuid(), "org_a_only", "Org A only", PropFlow.Domain.Configuration.CustomFieldAppliesTo.WorkItem,
                PropFlow.Domain.Configuration.CustomFieldType.Text, null, false, 0, DateTimeOffset.UtcNow));
            await admin.SaveChangesAsync();
        }

        await s.LoginAsync(); // OrganizationA's admin — sees its own field
        var ownList = await s.Client.GetFromJsonAsync<JsonElement>("/api/settings/custom-fields/");
        Assert.Contains(ownList.EnumerateArray(), x => x.GetProperty("key").GetString() == "org_a_only");

        using (var logout = await s.Client.PostAsync("/api/auth/logout", null)) Assert.True(logout.IsSuccessStatusCode);
        using (var login = await s.AttemptLoginAsync(s.EmailB, s.OrganizationB)) Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await s.RefreshCsrfAsync();
        var otherList = await s.Client.GetFromJsonAsync<JsonElement>("/api/settings/custom-fields/");
        Assert.DoesNotContain(otherList.EnumerateArray(), x => x.GetProperty("key").GetString() == "org_a_only");
    }
}
