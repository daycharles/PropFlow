using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Domain.Configuration;
using Xunit;

namespace PropFlow.IntegrationTests;

// PF-S03.02 — custom field values on a work item.
[Collection("PostgreSQL")]
public sealed class CustomFieldValueTests(DatabaseFixture fixture)
{
    private static async Task<Guid> SeedDefinitionAsync(Scenario s, string key, CustomFieldType type = CustomFieldType.Text,
        IReadOnlyList<string>? options = null, bool isRequired = false)
    {
        await using var store = s.AdminStore(s.OrganizationA);
        var definition = new CustomFieldDefinition(s.OrganizationA, Guid.NewGuid(), key, key, CustomFieldAppliesTo.WorkItem,
            type, options, isRequired, 0, DateTimeOffset.UtcNow);
        store.CustomFieldDefinitions.Add(definition);
        await store.SaveChangesAsync();
        return definition.Id;
    }

    [Fact]
    public async Task Creating_work_with_a_custom_field_round_trips_it_on_the_detail_response()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedDefinitionAsync(s, "warranty_note");
        await s.LoginAsync();

        var created = await s.Client.PostAsJsonAsync("/api/work/", new
        {
            title = "Roof repair", propertyId = s.PropertyA,
            customFields = new Dictionary<string, string?> { ["warranty_note"] = "3 years remaining" },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("3 years remaining", body.GetProperty("customFields").GetProperty("warranty_note").GetString());

        var workId = body.GetProperty("item").GetProperty("id").GetGuid();
        var detail = await s.Client.GetFromJsonAsync<JsonElement>($"/api/work/{workId}");
        Assert.Equal("3 years remaining", detail.GetProperty("customFields").GetProperty("warranty_note").GetString());
    }

    [Fact]
    public async Task Updating_work_sets_and_then_clears_a_custom_field()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedDefinitionAsync(s, "unit_color");
        await s.LoginAsync();
        var detail = await s.Client.GetFromJsonAsync<JsonElement>($"/api/work/{s.WorkA}");
        var version = detail.GetProperty("version").GetUInt32();

        var updateBody = (uint v, string? value) => new
        {
            title = "Pest control", description = (string?)null, categoryId = (Guid?)null, priority = "Normal",
            propertyId = s.PropertyA, buildingId = (Guid?)null, spaceId = (Guid?)null, residentId = (Guid?)null,
            assetId = (Guid?)null, dueDate = (string?)null, cost = (decimal?)null, internalNotes = (string?)null,
            residentVisibleNotes = (string?)null, status = (string?)null, scheduledStart = (string?)null,
            scheduledEnd = (string?)null, version = v,
            customFields = new Dictionary<string, string?> { ["unit_color"] = value },
        };

        var set = await s.Client.PutAsJsonAsync($"/api/work/{s.WorkA}", updateBody(version, "Blue"));
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var setBody = await set.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Blue", setBody.GetProperty("customFields").GetProperty("unit_color").GetString());
        var version2 = setBody.GetProperty("version").GetUInt32();

        var cleared = await s.Client.PutAsJsonAsync($"/api/work/{s.WorkA}", updateBody(version2, null));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var clearedBody = await cleared.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(clearedBody.GetProperty("customFields").TryGetProperty("unit_color", out _));
    }

    [Fact]
    public async Task An_unknown_key_an_archived_field_and_a_bad_value_are_all_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var archivedId = await SeedDefinitionAsync(s, "old_field");
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            (await admin.CustomFieldDefinitions.SingleAsync(x => x.Id == archivedId)).Archive();
            await admin.SaveChangesAsync();
        }
        await SeedDefinitionAsync(s, "age_years", CustomFieldType.Number);
        await s.LoginAsync();

        var unknownKey = await s.Client.PostAsJsonAsync("/api/work/", new
        {
            title = "Work A", propertyId = s.PropertyA,
            customFields = new Dictionary<string, string?> { ["does_not_exist"] = "x" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, unknownKey.StatusCode);

        var archivedField = await s.Client.PostAsJsonAsync("/api/work/", new
        {
            title = "Work B", propertyId = s.PropertyA,
            customFields = new Dictionary<string, string?> { ["old_field"] = "x" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, archivedField.StatusCode);

        var badValue = await s.Client.PostAsJsonAsync("/api/work/", new
        {
            title = "Work C", propertyId = s.PropertyA,
            customFields = new Dictionary<string, string?> { ["age_years"] = "not-a-number" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, badValue.StatusCode);
    }

    [Fact]
    public async Task A_bad_custom_field_on_create_leaves_no_partial_work_item()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var failed = await s.Client.PostAsJsonAsync("/api/work/", new
        {
            title = "zzqx-rollback-check", propertyId = s.PropertyA,
            customFields = new Dictionary<string, string?> { ["does_not_exist"] = "x" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);

        var search = await s.Client.GetFromJsonAsync<JsonElement>("/api/work/?search=zzqx-rollback-check");
        Assert.Empty(search.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Custom_field_values_are_tenant_scoped_on_the_work_detail_response()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var definitionId = await SeedDefinitionAsync(s, "org_a_only");
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            admin.CustomFieldValues.Add(CustomFieldValue.Create(s.OrganizationA, Guid.NewGuid(), s.WorkA, definitionId,
                "org a value", DateTimeOffset.UtcNow));
            await admin.SaveChangesAsync();
        }
        await s.LoginAsync();
        var detail = await s.Client.GetFromJsonAsync<JsonElement>($"/api/work/{s.WorkA}");
        Assert.Equal("org a value", detail.GetProperty("customFields").GetProperty("org_a_only").GetString());
    }
}
