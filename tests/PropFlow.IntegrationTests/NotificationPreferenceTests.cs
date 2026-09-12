using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PropFlow.IntegrationTests;

// PF-S03.07 - personal, per-user staff notification preferences. Storage and validation only:
// no delivery channel reads these yet.
[Collection("PostgreSQL")]
public sealed class NotificationPreferenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_user_with_no_rows_reads_as_fully_subscribed()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var response = await s.Client.GetAsync("/api/settings/notification-preferences/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.Equal(3, body!.Length);
        Assert.All(body, p => Assert.True(p.GetProperty("enabled").GetBoolean()));
    }

    [Fact]
    public async Task Disabling_one_event_type_leaves_the_others_enabled()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var updated = await s.Client.PutAsJsonAsync("/api/settings/notification-preferences/AutomationApplied", new { enabled = false });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedBody = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AutomationApplied", updatedBody.GetProperty("eventType").GetString());
        Assert.False(updatedBody.GetProperty("enabled").GetBoolean());

        var list = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/settings/notification-preferences/") ?? [];
        Assert.False(list.Single(p => p.GetProperty("eventType").GetString() == "AutomationApplied").GetProperty("enabled").GetBoolean());
        Assert.True(list.Single(p => p.GetProperty("eventType").GetString() == "WorkAssigned").GetProperty("enabled").GetBoolean());
        Assert.True(list.Single(p => p.GetProperty("eventType").GetString() == "InvitationReceived").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Saving_the_same_event_type_again_updates_the_same_row_rather_than_creating_a_second_one()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        await s.Client.PutAsJsonAsync("/api/settings/notification-preferences/WorkAssigned", new { enabled = false });
        await s.Client.PutAsJsonAsync("/api/settings/notification-preferences/WorkAssigned", new { enabled = true });

        await using var admin = s.AdminStore(s.OrganizationA);
        Assert.Equal(1, await admin.NotificationPreferences.CountAsync(x => x.UserId == s.AdminA));
    }

    [Fact]
    public async Task An_unknown_event_type_is_a_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var response = await s.Client.PutAsJsonAsync("/api/settings/notification-preferences/NotARealEvent", new { enabled = false });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Preferences_are_scoped_to_the_calling_user_not_the_whole_organization()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        await s.Client.PutAsJsonAsync("/api/settings/notification-preferences/WorkAssigned", new { enabled = false });

        using (var logout = await s.Client.PostAsync("/api/auth/logout", null)) Assert.True(logout.IsSuccessStatusCode);
        using (var login = await s.AttemptLoginAsync(s.ReaderEmail, s.OrganizationA)) Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await s.RefreshCsrfAsync();

        var list = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/settings/notification-preferences/");
        Assert.True(list!.Single(p => p.GetProperty("eventType").GetString() == "WorkAssigned").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Preferences_are_tenant_scoped()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(); // OrganizationA
        await s.Client.PutAsJsonAsync("/api/settings/notification-preferences/WorkAssigned", new { enabled = false });

        using (var logout = await s.Client.PostAsync("/api/auth/logout", null)) Assert.True(logout.IsSuccessStatusCode);
        using (var login = await s.AttemptLoginAsync(s.EmailB, s.OrganizationB)) Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await s.RefreshCsrfAsync();

        var list = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/settings/notification-preferences/");
        Assert.True(list!.Single(p => p.GetProperty("eventType").GetString() == "WorkAssigned").GetProperty("enabled").GetBoolean());
    }
}
