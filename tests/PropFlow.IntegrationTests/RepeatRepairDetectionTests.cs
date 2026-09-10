using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class RepeatRepairDetectionTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Policy_defaults_then_round_trips_through_the_api()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var defaults = await s.Client.GetFromJsonAsync<JsonElement>("/api/assets/repeat-repair-policy");
        Assert.Equal(3, defaults.GetProperty("repairThreshold").GetInt32());
        Assert.Equal(120, defaults.GetProperty("windowDays").GetInt32());
        Assert.False(defaults.GetProperty("matchByCategory").GetBoolean());

        var put = await s.Client.PutAsJsonAsync("/api/assets/repeat-repair-policy",
            new { repairThreshold = 4, windowDays = 90, matchByCategory = true });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var saved = await s.Client.GetFromJsonAsync<JsonElement>("/api/assets/repeat-repair-policy");
        Assert.Equal(4, saved.GetProperty("repairThreshold").GetInt32());
        Assert.Equal(90, saved.GetProperty("windowDays").GetInt32());
        Assert.True(saved.GetProperty("matchByCategory").GetBoolean());

        // A second write updates the single per-org row rather than adding one.
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PutAsJsonAsync("/api/assets/repeat-repair-policy",
            new { repairThreshold = 5, windowDays = 100, matchByCategory = false })).StatusCode);
        await using var store = s.AdminStore(s.OrganizationA);
        Assert.Single(store.RepeatRepairPolicies);
    }

    [Fact]
    public async Task Out_of_range_policy_values_are_400_and_a_reader_cannot_set_the_policy()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PutAsJsonAsync("/api/assets/repeat-repair-policy",
            new { repairThreshold = 1, windowDays = 90, matchByCategory = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PutAsJsonAsync("/api/assets/repeat-repair-policy",
            new { repairThreshold = 3, windowDays = 5, matchByCategory = false })).StatusCode);

        await s.LoginAsync(reader: true);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/assets/repeat-repair-policy")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PutAsJsonAsync("/api/assets/repeat-repair-policy",
            new { repairThreshold = 3, windowDays = 90, matchByCategory = false })).StatusCode);
    }

    [Fact]
    public async Task Assessment_counts_recent_work_in_the_window_and_flags_a_repeat_repair()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        await using (var store = s.AdminStore(s.OrganizationA))
        {
            foreach (var (title, daysAgo, cost) in new[]
            {
                ("Compressor 1", 10, 200m), ("Compressor 2", 40, 150m), ("Compressor 3", 80, 175m),
                ("Ancient repair", 400, 999m), // outside the 120-day window
            })
            {
                var work = new WorkItem(s.OrganizationA, Guid.NewGuid(), title, s.PropertyA, s.AdminA);
                work.SetAsset(s.AssetA);
                work.Publish(DateTimeOffset.UtcNow.AddDays(-daysAgo));
                work.SetCost(cost);
                store.WorkItems.Add(work);
            }
            await store.SaveChangesAsync();
        }

        var assessment = await s.Client.GetFromJsonAsync<JsonElement>($"/api/assets/{s.AssetA}/repeat-repair");
        Assert.Equal(3, assessment.GetProperty("repairCount").GetInt32());
        Assert.Equal(525m, assessment.GetProperty("totalCostInWindow").GetDecimal());
        Assert.True(assessment.GetProperty("isRepeatRepair").GetBoolean());

        // An asset with no work is not a repeat repair.
        var quiet = await s.Client.GetFromJsonAsync<JsonElement>($"/api/assets/{s.AssetA}/repeat-repair?categoryId={Guid.NewGuid()}");
        // categoryId is ignored while matchByCategory is off, so still 3.
        Assert.Equal(3, quiet.GetProperty("repairCount").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/assets/{Guid.NewGuid()}/repeat-repair")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/assets/{s.AssetB}/repeat-repair")).StatusCode);
    }

    [Fact]
    public async Task Category_similarity_narrows_the_count_when_enabled()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var hvacCategory = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            store.Categories.Add(new PropFlow.Domain.Work.WorkCategory(s.OrganizationA, hvacCategory, "HVAC", 1));
            await store.SaveChangesAsync();

            foreach (var (title, category) in new (string, Guid?)[]
            {
                ("HVAC 1", hvacCategory), ("HVAC 2", hvacCategory), ("Plumbing", null),
            })
            {
                var work = new WorkItem(s.OrganizationA, Guid.NewGuid(), title, s.PropertyA, s.AdminA);
                if (category is { } c) work.Edit(title, null, c, WorkPriority.Normal);
                work.SetAsset(s.AssetA);
                work.Publish(DateTimeOffset.UtcNow.AddDays(-5));
                store.WorkItems.Add(work);
            }
            await store.SaveChangesAsync();
        }

        await s.Client.PutAsJsonAsync("/api/assets/repeat-repair-policy",
            new { repairThreshold = 2, windowDays = 120, matchByCategory = true });

        var all = await s.Client.GetFromJsonAsync<JsonElement>($"/api/assets/{s.AssetA}/repeat-repair");
        Assert.Equal(3, all.GetProperty("repairCount").GetInt32()); // no categoryId supplied -> not narrowed

        var narrowed = await s.Client.GetFromJsonAsync<JsonElement>($"/api/assets/{s.AssetA}/repeat-repair?categoryId={hvacCategory}");
        Assert.Equal(2, narrowed.GetProperty("repairCount").GetInt32());
        Assert.True(narrowed.GetProperty("isRepeatRepair").GetBoolean());
    }

    [Fact]
    public async Task Rls_with_check_rejects_a_cross_tenant_policy_insert()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        await using var storeA = s.Store(s.OrganizationA);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => storeA.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO operations."RepeatRepairPolicies"
              ("OrganizationId", "Id", "RepairThreshold", "WindowDays", "MatchByCategory")
            VALUES ({s.OrganizationB}, {Guid.NewGuid()}, 3, 120, false)
            """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task Runtime_role_without_a_tenant_context_sees_no_policy_rows()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        await s.Client.PutAsJsonAsync("/api/assets/repeat-repair-policy",
            new { repairThreshold = 3, windowDays = 120, matchByCategory = false });

        await using var connection = new NpgsqlConnection(fixture.RuntimeConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM operations.\"RepeatRepairPolicies\"";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }
}
