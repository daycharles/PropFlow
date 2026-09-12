using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PropFlow.IntegrationTests;

// PF-S03.06 - business hours + org default time zone. Storage and validation only: nothing
// consults this yet, so these tests cover the round trip and the guardrails, not a consumer.
[Collection("PostgreSQL")]
public sealed class OrganizationSettingsTests(DatabaseFixture fixture)
{
    private static object AllClosed(string timeZone) => new
    {
        defaultTimeZoneId = timeZone,
        businessHours = Enum.GetValues<DayOfWeek>().Select(d => new { day = d.ToString(), open = (string?)null, close = (string?)null }),
    };

    private static object OpenWeekdays(string timeZone) => new
    {
        defaultTimeZoneId = timeZone,
        businessHours = Enum.GetValues<DayOfWeek>().Select(d => d is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? new { day = d.ToString(), open = (string?)null, close = (string?)null }
            : new { day = d.ToString(), open = (string?)"09:00:00", close = (string?)"17:00:00" }),
    };

    [Fact]
    public async Task Reading_settings_before_any_are_saved_reports_no_default_zone_and_all_closed()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var response = await s.Client.GetAsync("/api/settings/organization/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("defaultTimeZoneId").ValueKind);
        Assert.Equal(7, body.GetProperty("businessHours").GetArrayLength());
    }

    [Fact]
    public async Task Saving_and_reading_business_hours_round_trips_them()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var saved = await s.Client.PutAsJsonAsync("/api/settings/organization/", OpenWeekdays("America/New_York"));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var response = await s.Client.GetAsync("/api/settings/organization/");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("America/New_York", body.GetProperty("defaultTimeZoneId").GetString());
        var monday = body.GetProperty("businessHours").EnumerateArray().Single(h => h.GetProperty("day").GetString() == "Monday");
        Assert.Equal("09:00:00", monday.GetProperty("open").GetString());
        var saturday = body.GetProperty("businessHours").EnumerateArray().Single(h => h.GetProperty("day").GetString() == "Saturday");
        Assert.Equal(JsonValueKind.Null, saturday.GetProperty("open").ValueKind);
    }

    [Fact]
    public async Task Saving_again_updates_the_same_row_rather_than_creating_a_second_one()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        await s.Client.PutAsJsonAsync("/api/settings/organization/", OpenWeekdays("America/New_York"));
        var updated = await s.Client.PutAsJsonAsync("/api/settings/organization/", AllClosed("America/Los_Angeles"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var response = await s.Client.GetAsync("/api/settings/organization/");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("America/Los_Angeles", body.GetProperty("defaultTimeZoneId").GetString());
        Assert.All(body.GetProperty("businessHours").EnumerateArray(), h => Assert.Equal(JsonValueKind.Null, h.GetProperty("open").ValueKind));

        await using var admin = s.AdminStore(s.OrganizationA);
        Assert.Equal(1, await admin.OrganizationSettings.CountAsync());
    }

    [Fact]
    public async Task An_invalid_time_zone_is_a_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var response = await s.Client.PutAsJsonAsync("/api/settings/organization/", AllClosed("UTC"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_week_missing_a_day_is_a_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var response = await s.Client.PutAsJsonAsync("/api/settings/organization/", new
        {
            defaultTimeZoneId = "America/New_York",
            businessHours = new[] { new { day = "Monday", open = (string?)null, close = (string?)null } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Read_only_users_can_read_but_not_save()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);

        var read = await s.Client.GetAsync("/api/settings/organization/");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var write = await s.Client.PutAsJsonAsync("/api/settings/organization/", AllClosed("America/New_York"));
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Settings_are_tenant_scoped()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(); // OrganizationA
        await s.Client.PutAsJsonAsync("/api/settings/organization/", OpenWeekdays("America/New_York"));

        using (var logout = await s.Client.PostAsync("/api/auth/logout", null)) Assert.True(logout.IsSuccessStatusCode);
        using (var login = await s.AttemptLoginAsync(s.EmailB, s.OrganizationB)) Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await s.RefreshCsrfAsync();

        var response = await s.Client.GetAsync("/api/settings/organization/");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("defaultTimeZoneId").ValueKind);
    }
}
