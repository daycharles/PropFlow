using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PropFlow.Domain.Approvals;
using PropFlow.Domain.Configuration;
using Xunit;

namespace PropFlow.IntegrationTests;

// PF-S03.09: negative-authorization and tenant-isolation coverage for the whole FS-S03
// configuration surface (custom fields, categories, numbering, approvals, business hours,
// notification preferences - PF-S03.01-.07), mirroring what IdentityAdministrationTests is for
// FS-S01. The individual feature test files (CustomFieldTests, NumberingTests, ApprovalTests,
// OrganizationSettingsTests, ...) already cover their own endpoint's shape-level 400s and one
// negative-auth/tenant case each; this file is the consolidated sweep across all of them plus
// the specific gaps that fell between two features' own test files.
[Collection("PostgreSQL")]
public sealed class ConfigurationAdministrationTests(DatabaseFixture fixture)
{
    private static async Task<Guid> SeedCustomFieldAsync(Scenario s, string key)
    {
        await using var admin = s.AdminStore(s.OrganizationA);
        var field = new CustomFieldDefinition(s.OrganizationA, Guid.NewGuid(), key, key,
            ConfigurationEntityType.WorkItem, CustomFieldType.Text, null, false, 0, DateTimeOffset.UtcNow);
        admin.CustomFieldDefinitions.Add(field);
        await admin.SaveChangesAsync();
        return field.Id;
    }

    // Requested by a synthetic actor, not the logged-in test client - same reasoning as
    // ApprovalTests.SeedRequestAsync: every logged-in client in this fixture is the same admin,
    // and ApprovalRequest.Decide refuses a self-decision.
    private static async Task<Guid> SeedApprovalAsync(Scenario s)
    {
        await using var admin = s.AdminStore(s.OrganizationA);
        var request = ApprovalRequest.Create(s.OrganizationA, Guid.NewGuid(), "Budget", Guid.NewGuid(), Guid.NewGuid(), null, DateTimeOffset.UtcNow);
        admin.ApprovalRequests.Add(request);
        await admin.SaveChangesAsync();
        return request.Id;
    }

    [Fact]
    public async Task Read_only_role_is_forbidden_from_every_PF_S03_write_endpoint_except_its_own_notification_preferences()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var fieldId = await SeedCustomFieldAsync(s, "readonly_probe");
        var approvalId = await SeedApprovalAsync(s);
        await s.LoginAsync(reader: true);

        // Reads: allowed. A Read Only user administers nothing here, but every list/get in this
        // surface only needs Work.Read.
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/settings/custom-fields/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/settings/numbering/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/settings/organization/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/approvals/")).StatusCode);

        // Writes gated on Settings.ManageConfiguration or Settings.ManageCategories: forbidden.
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsJsonAsync("/api/settings/custom-fields/",
            new { key = "nope", name = "Nope", appliesTo = "WorkItem", fieldType = "Text", options = (string[]?)null, isRequired = false, sortOrder = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PutAsJsonAsync($"/api/settings/custom-fields/{fieldId}",
            new { name = "Still no", options = (string[]?)null, isRequired = false, sortOrder = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsync($"/api/settings/custom-fields/{fieldId}/archive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsJsonAsync("/api/categories", new { name = "Nope", sortOrder = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PutAsJsonAsync("/api/settings/numbering/WorkItem",
            new { prefix = "NO-", width = 4 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PutAsJsonAsync("/api/settings/organization/", new
        {
            defaultTimeZoneId = "America/New_York",
            businessHours = Enum.GetValues<DayOfWeek>().Select(d => new { day = d.ToString(), open = (string?)null, close = (string?)null }),
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsJsonAsync($"/api/approvals/{approvalId}/decide",
            new { approved = true, reason = (string?)null })).StatusCode);

        // Requesting an approval needs only Work.Read, not ManageConfiguration - deliberately.
        Assert.Equal(HttpStatusCode.Created, (await s.Client.PostAsJsonAsync("/api/approvals/",
            new { subjectType = "Budget", subjectId = Guid.NewGuid(), note = (string?)null })).StatusCode);

        // Notification preferences are personal settings (PF-S03.07), not gated on
        // Settings.ManageConfiguration at all - a Read Only user manages their own the same as
        // anyone else. This is the one write in the sweep that must succeed.
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PutAsJsonAsync(
            "/api/settings/notification-preferences/WorkAssigned", new { enabled = false })).StatusCode);
    }

    [Fact]
    public async Task Categories_are_tenant_scoped()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var created = await s.Client.PostAsJsonAsync("/api/categories", new { name = "Org A only", sortOrder = 0 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using (var logout = await s.Client.PostAsync("/api/auth/logout", null)) Assert.True(logout.IsSuccessStatusCode);
        using (var login = await s.AttemptLoginAsync(s.EmailB, s.OrganizationB)) Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await s.RefreshCsrfAsync();

        var otherList = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/categories");
        Assert.DoesNotContain(otherList!, c => c.GetProperty("name").GetString() == "Org A only");
    }

    [Fact]
    public async Task Numbering_schemes_are_independent_per_organization()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(); // OrganizationA
        var configuredA = await s.Client.PutAsJsonAsync("/api/settings/numbering/WorkItem", new { prefix = "A-", width = 3 });
        Assert.Equal(HttpStatusCode.OK, configuredA.StatusCode);
        var firstA = await (await s.Client.PostAsJsonAsync("/api/work/", new { title = "Org A work", propertyId = s.PropertyA }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("A-001", firstA.GetProperty("item").GetProperty("displayNumber").GetString());

        using (var logout = await s.Client.PostAsync("/api/auth/logout", null)) Assert.True(logout.IsSuccessStatusCode);
        using (var login = await s.AttemptLoginAsync(s.EmailB, s.OrganizationB)) Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await s.RefreshCsrfAsync();

        // OrganizationB never configured numbering - it must not have inherited OrganizationA's
        // scheme, so its own work still gets no display number at all.
        var beforeB = await (await s.Client.PostAsJsonAsync("/api/work/", new { title = "Org B work, unconfigured", propertyId = s.PropertyB }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, beforeB.GetProperty("item").GetProperty("displayNumber").ValueKind);

        var configuredB = await s.Client.PutAsJsonAsync("/api/settings/numbering/WorkItem", new { prefix = "B-", width = 3 });
        Assert.Equal(HttpStatusCode.OK, configuredB.StatusCode);
        // OrganizationB's sequence starts at 1 - it was not advanced by OrganizationA's work.
        var firstB = await (await s.Client.PostAsJsonAsync("/api/work/", new { title = "Org B work, configured", propertyId = s.PropertyB }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("B-001", firstB.GetProperty("item").GetProperty("displayNumber").GetString());
    }

    [Fact]
    public async Task Custom_field_value_validation_does_not_leak_definitions_across_organizations()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedCustomFieldAsync(s, "org_a_only_key");

        using (var login = await s.AttemptLoginAsync(s.EmailB, s.OrganizationB)) Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await s.RefreshCsrfAsync();

        // OrganizationB has no such definition - the key must be rejected as unknown, not
        // accidentally validated (or worse, accepted) against OrganizationA's definition.
        var response = await s.Client.PostAsJsonAsync("/api/work/", new
        {
            title = "Org B work referencing org A's field",
            propertyId = s.PropertyB,
            customFields = new Dictionary<string, string?> { ["org_a_only_key"] = "should not be accepted" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
