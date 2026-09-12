using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Domain.Integrations;
using PropFlow.Infrastructure.Integrations;
using Xunit;
using static PropFlow.IntegrationTests.IntegrationReplayTests;

namespace PropFlow.IntegrationTests;

// PF-S19.10, part two: the operator-facing surface PF-S19.08 added — the conflict queue, the
// mapping configuration, the run history and manual retirement — plus the acceptance narrative
// that ties them together: connect → sync → review conflicts → fix the mapping → promote → sync →
// reconciled.
//
// PF-S19.08 shipped with no tests of its own and said so. Everything here is a first measurement.
[Collection("PostgreSQL")]
public sealed class IntegrationAdministrationApiTests(DatabaseFixture fixture)
{
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode,
            $"{(int)response.StatusCode} from {response.RequestMessage?.RequestUri}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> Conflicts(Scenario s, Guid id, string query = "") =>
        await Json(await s.Client.GetAsync($"/api/integrations/{id}/conflicts{query}"));

    private static List<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray().ToList();

    // Everything except the work-order status rule that would map "Open". Leaves exactly one
    // recurring UnmappedValue divergence to watch through the queue's lifecycle.
    private static async Task ConfigureExceptOpenStatusAsync(Scenario s, Guid id)
    {
        await Put(s, $"/api/integrations/{id}/mappings/Property",
            new { targetPortfolioId = s.PortfolioA, defaultTimeZoneId = "America/New_York" });
        await Put(s, $"/api/integrations/{id}/mappings/WorkOrder", new { defaultCreatorId = s.AdminA });
        await Rule(s, id, "WorkOrder", "WorkOrderStatus", "Closed", "Completed");
        await Rule(s, id, "Asset", "AssetKind", "WaterHeater", "WaterHeater");
        await Rule(s, id, "Asset", "AssetKind", "Hvac", "Hvac");
        await PromoteAllAsync(s, id);
    }

    // ==== 1. The acceptance narrative, end to end ==========================================

    [Fact]
    public async Task Connect_sync_review_fix_promote_sync_reconciles_the_feed()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();

        // Connect.
        var id = await ConnectAsync(s);
        var empty = await OperationsRowsAsync(s);

        // Sync. Nothing is written and the gaps are reported.
        var first = await SyncAsync(s, id);
        Assert.Equal(0, Count(first, "added"));
        Assert.True(Count(first, "conflicted") > 0);
        Assert.True(empty.SetEquals(await OperationsRowsAsync(s)), "an unconfigured sync must write nothing");

        // Review the conflict queue. Each row names the field a human has to supply.
        var queue = await Conflicts(s, id);
        Assert.Equal(Count(first, "conflicted"), queue.GetProperty("openCount").GetInt32());
        Assert.Equal(0, queue.GetProperty("staleCount").GetInt32());
        Assert.Contains(Items(queue), x => x.GetProperty("field").GetString() == "TargetPortfolioId");
        Assert.Contains(Items(queue), x => x.GetProperty("field").GetString() == "DefaultCreatorId");
        Assert.All(Items(queue), x => Assert.True(x.GetProperty("seenInLatestRun").GetBoolean()));

        // Fix the mapping and promote.
        await ConfigureMappingAsync(s, id);
        await PromoteAllAsync(s, id);
        var profiles = await Json(await s.Client.GetAsync($"/api/integrations/{id}/mappings"));
        Assert.All(profiles.EnumerateArray(), p => Assert.Equal("AutoApply", p.GetProperty("mode").GetString()));

        // Sync. Now it reconciles.
        var second = await SyncAsync(s, id);
        Assert.Equal("Completed", second.GetProperty("outcome").GetString());
        Assert.True(Count(second, "added") > 0);
        Assert.Equal(0, Count(second, "failed"));
        Assert.Equal(0, Count(second, "conflicted"));

        // Real rows, with the mapped values.
        await using (var store = s.Store(s.OrganizationA))
        {
            var cedar = await store.Properties.SingleAsync(x => x.Name == "Cedar Court");
            Assert.Equal(s.PortfolioA, cedar.PortfolioId);
            Assert.Equal("America/New_York", cedar.TimeZoneId);

            var faucet = await store.WorkItems.SingleAsync(x => x.Title == "Leaking kitchen faucet");
            Assert.Equal(PropFlow.Domain.Work.WorkStatus.New, faucet.Status);
            Assert.Equal(s.AdminA, faucet.CreatorId);
            Assert.Equal(cedar.Id, faucet.PropertyId);

            Assert.Equal(PropFlow.Domain.Assets.AssetKind.WaterHeater,
                (await store.Assets.SingleAsync(x => x.SerialNumber == "RH-4021-8873")).Kind);
        }

        // Every link now points at a PropFlow row.
        Assert.All(await LinksAsync(s, id), link =>
        {
            Assert.Equal(SyncState.Synced, link.SyncState);
            Assert.NotNull(link.InternalId);
        });

        // The run history records both runs, newest first.
        var runs = await Json(await s.Client.GetAsync($"/api/integrations/{id}/runs"));
        Assert.Equal(2, runs.GetProperty("totalCount").GetInt32());
        Assert.All(Items(runs), r => Assert.Equal("Completed", r.GetProperty("status").GetString()));
        var newest = Items(runs)[0];
        Assert.True(newest.GetProperty("added").GetInt32() > 0);
        Assert.False(string.IsNullOrEmpty(newest.GetProperty("snapshotHash").GetString()));

        var single = await Json(await s.Client.GetAsync(
            $"/api/integrations/{id}/runs/{newest.GetProperty("id").GetGuid()}"));
        Assert.Equal(newest.GetProperty("id").GetGuid(), single.GetProperty("id").GetGuid());
    }

    // ==== 2. Promotion refuses and says why ================================================

    [Fact]
    public async Task Promoting_a_bare_profile_is_a_409_that_names_the_missing_fields()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);

        // A profile with nothing supplied.
        await Put(s, $"/api/integrations/{id}/mappings/Property", new { });

        using var refused = await s.Client.PostAsync($"/api/integrations/{id}/mappings/Property/promote", null);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        var body = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("issues", out var issues),
            $"the refusal must carry the issue list; body was {body}");
        var fields = issues.EnumerateArray().Select(x => x.GetProperty("field").GetString()).ToList();
        Assert.Contains("TargetPortfolioId", fields);
        Assert.Contains("DefaultTimeZoneId", fields);
        // Severity must serialize as the member name through the app's JsonStringEnumConverter,
        // not as an integer — an operator UI keys on "Error".
        Assert.Contains(issues.EnumerateArray(),
            x => x.GetProperty("severity").ValueKind == JsonValueKind.String
                && x.GetProperty("severity").GetString() == "Error");

        // The profile is untouched by a refused promotion.
        var profiles = await Json(await s.Client.GetAsync($"/api/integrations/{id}/mappings"));
        var property = profiles.EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "Property");
        Assert.Equal("ReportOnly", property.GetProperty("mode").GetString());
        Assert.False(property.GetProperty("canAutoApply").GetBoolean());
    }

    // ==== 5 and 6. Conflict idempotency, then staleness ====================================

    [Fact]
    public async Task A_recurring_divergence_is_one_row_that_counts_observations_and_then_goes_stale()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await ConfigureExceptOpenStatusAsync(s, id);

        await SyncAsync(s, id);
        await SyncAsync(s, id);
        var third = await SyncAsync(s, id);

        // Exactly one row, not one per run. IX_Conflicts_OpenDivergence enforces this at the
        // database too, so a regression here surfaces as a unique violation rather than a queue
        // that grows by a row a night.
        var rows = (await ConflictRowsAsync(s, id))
            .Where(x => x.Kind == IntegrationEntityKind.WorkOrder && x.Reason == ConflictReason.UnmappedValue).ToList();
        var conflict = Assert.Single(rows);
        Assert.Equal("MOCK-WO-1", conflict.ExternalId);
        Assert.Equal(3, conflict.ObservationCount);
        Assert.NotEqual(conflict.FirstSeenInRunId, conflict.LastSeenInRunId);
        Assert.Equal(third.GetProperty("runId").GetGuid(), conflict.LastSeenInRunId);

        var before = await Conflicts(s, id, "?kind=WorkOrder");
        Assert.Equal(0, before.GetProperty("staleCount").GetInt32());
        Assert.All(Items(before), x => Assert.True(x.GetProperty("seenInLatestRun").GetBoolean()));

        // Fix the mapping, re-promote (adding a rule can demote), sync.
        await Rule(s, id, "WorkOrder", "WorkOrderStatus", "Open", "New");
        await PromoteAllAsync(s, id);
        var fixedRun = await SyncAsync(s, id);
        Assert.Equal(0, Count(fixedRun, "conflicted"));

        // The row is still Open — the reconciler never closes one, because "I no longer detect it"
        // and "a human decided" are different facts. It reads as stale instead.
        var after = await Conflicts(s, id, "?kind=WorkOrder");
        Assert.True(after.GetProperty("staleCount").GetInt32() > 0);
        var stale = Items(after).Single(x => x.GetProperty("externalId").GetString() == "MOCK-WO-1"
            && x.GetProperty("reason").GetString() == "UnmappedValue");
        Assert.False(stale.GetProperty("seenInLatestRun").GetBoolean());
        Assert.Equal("Open", stale.GetProperty("status").GetString());
        Assert.Equal(3, stale.GetProperty("observationCount").GetInt32());
    }

    // ==== 7. Resolve is a transition, a recurrence is a new row, and there is no delete =====

    [Fact]
    public async Task Resolving_is_a_transition_a_recurrence_is_a_new_row_and_delete_is_refused()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await ConfigureExceptOpenStatusAsync(s, id);
        await SyncAsync(s, id);

        var conflictId = Items(await Conflicts(s, id, "?kind=WorkOrder&reason=UnmappedValue"))
            .Single().GetProperty("id").GetGuid();

        using (var resolved = await s.Client.PostAsJsonAsync(
                   $"/api/integrations/{id}/conflicts/{conflictId}/resolve", new { note = "Rule coming." }))
            Assert.Equal(HttpStatusCode.NoContent, resolved.StatusCode);

        // A second close is a refused transition, not a silent no-op.
        using (var again = await s.Client.PostAsJsonAsync(
                   $"/api/integrations/{id}/conflicts/{conflictId}/resolve", new { note = "Again." }))
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // The divergence is still there, so the next run raises a NEW row. The partial unique index
        // permits that precisely because it only covers Open rows.
        await SyncAsync(s, id);
        var rows = (await ConflictRowsAsync(s, id))
            .Where(x => x.Kind == IntegrationEntityKind.WorkOrder && x.Reason == ConflictReason.UnmappedValue)
            .ToList();
        Assert.Equal(2, rows.Count);
        var closed = rows.Single(x => x.Id == conflictId);
        Assert.Equal(ConflictStatus.Resolved, closed.Status);
        Assert.Equal(s.AdminA, closed.ResolvedByUserId);
        Assert.Equal("Rule coming.", closed.ResolutionNote);
        var reopened = rows.Single(x => x.Id != conflictId);
        Assert.Equal(ConflictStatus.Open, reopened.Status);
        Assert.Equal(1, reopened.ObservationCount);

        // And the runtime role genuinely cannot delete the history: SELECT/INSERT/UPDATE only.
        await using var runtime = new NpgsqlConnection(fixture.RuntimeConnection);
        await runtime.OpenAsync();
        await using var command = runtime.CreateCommand();
        command.CommandText = """DELETE FROM integrations."Conflicts" WHERE "Id" = @id""";
        command.Parameters.AddWithValue("id", conflictId);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
    }

    // ==== 8 and 9. Ordering and filters =====================================================

    [Fact]
    public async Task The_queue_puts_open_conflicts_first_and_filters_narrow_or_refuse()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await SyncAsync(s, id);

        // Ignore one, leave the rest open.
        var all = Items(await Conflicts(s, id));
        Assert.True(all.Count >= 2, "the unconfigured feed must raise more than one divergence");
        var ignoredId = all.First(x => x.GetProperty("kind").GetString() == "Asset").GetProperty("id").GetGuid();
        using (var ignored = await s.Client.PostAsJsonAsync(
                   $"/api/integrations/{id}/conflicts/{ignoredId}/ignore", new { note = "Not our asset." }))
            Assert.Equal(HttpStatusCode.NoContent, ignored.StatusCode);

        // Open first. Ordering by the Status column itself would not do this: the column is
        // HasConversion<string>(), so the database sorts member names and "Ignored" sorts above
        // "Open".
        var page = await Conflicts(s, id);
        Assert.Equal("Open", Items(page)[0].GetProperty("status").GetString());
        Assert.Equal("Ignored", Items(page)[^1].GetProperty("status").GetString());
        Assert.Equal(all.Count - 1, page.GetProperty("openCount").GetInt32());

        var open = await Conflicts(s, id, "?status=Open");
        Assert.All(Items(open), x => Assert.Equal("Open", x.GetProperty("status").GetString()));
        Assert.Equal(all.Count - 1, open.GetProperty("totalCount").GetInt32());

        var workOrders = await Conflicts(s, id, "?kind=WorkOrder");
        Assert.NotEmpty(Items(workOrders));
        Assert.All(Items(workOrders), x => Assert.Equal("WorkOrder", x.GetProperty("kind").GetString()));

        var unmapped = await Conflicts(s, id, "?reason=UnmappedValue");
        Assert.NotEmpty(Items(unmapped));
        Assert.All(Items(unmapped), x => Assert.Equal("UnmappedValue", x.GetProperty("reason").GetString()));

        // A present but unparseable filter is a 400. Silently ignoring it would show an operator a
        // queue they did not ask for and let them close conflicts they never meant to see.
        foreach (var bad in new[] { "?status=Nonsense", "?kind=Nonsense", "?reason=Nonsense" })
            Assert.Equal(HttpStatusCode.BadRequest,
                (await s.Client.GetAsync($"/api/integrations/{id}/conflicts{bad}")).StatusCode);
    }

    // ==== 10. Manual retirement ==============================================================

    [Fact]
    public async Task Retiring_a_record_by_hand_records_the_actor_and_leaves_the_propflow_row()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await ConfigureMappingAsync(s, id);
        await PromoteAllAsync(s, id);
        await SyncAsync(s, id);

        var link = (await LinksAsync(s, id))
            .Single(x => x.Kind == IntegrationEntityKind.Asset && x.ExternalId == "MOCK-ASSET-1");
        var internalId = link.InternalId!.Value;

        using (var retired = await s.Client.PostAsync($"/api/integrations/{id}/records/{link.Id}/retire", null))
            Assert.Equal(HttpStatusCode.NoContent, retired.StatusCode);

        var records = await Json(await s.Client.GetAsync($"/api/integrations/{id}/records?pageSize=200"));
        var row = Items(records).Single(x => x.GetProperty("id").GetGuid() == link.Id);
        Assert.Equal("Retired", row.GetProperty("syncState").GetString());
        Assert.Equal(s.AdminA, row.GetProperty("retiredByUserId").GetGuid());

        // Retiring a link says "stop reconciling this", never "delete the tenant's row".
        await using (var store = s.Store(s.OrganizationA))
            Assert.Single(await store.Assets.Where(x => x.Id == internalId).ToListAsync());

        using var twice = await s.Client.PostAsync($"/api/integrations/{id}/records/{link.Id}/retire", null);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
    }

    // ==== 11. Ids from one connection do not work on another ================================

    [Fact]
    public async Task An_id_from_one_connection_is_a_404_on_another_connections_routes()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var mock = await ConnectAsync(s);
        var other = await ConnectAsync(s, SandboxIntegrationAdapter.Source);

        var report = await SyncAsync(s, mock);
        var runId = report.GetProperty("runId").GetGuid();
        var conflictId = Items(await Conflicts(s, mock))[0].GetProperty("id").GetGuid();
        var recordId = (await LinksAsync(s, mock))[0].Id;

        Assert.Equal(HttpStatusCode.NotFound,
            (await s.Client.GetAsync($"/api/integrations/{other}/runs/{runId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await s.Client.PostAsJsonAsync($"/api/integrations/{other}/conflicts/{conflictId}/resolve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await s.Client.PostAsync($"/api/integrations/{other}/records/{recordId}/retire", null)).StatusCode);

        // And the other connection's own views are empty rather than borrowing the first's.
        var runs = await Json(await s.Client.GetAsync($"/api/integrations/{other}/runs"));
        Assert.Equal(0, runs.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await Conflicts(s, other)).GetProperty("totalCount").GetInt32());
    }

    // ==== 12. Every administration route refuses a Read Only session ========================

    [Fact]
    public async Task Every_administration_route_refuses_a_read_only_session()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await SyncAsync(s, id);
        var conflictId = Items(await Conflicts(s, id))[0].GetProperty("id").GetGuid();
        var recordId = (await LinksAsync(s, id))[0].Id;

        Assert.Equal(HttpStatusCode.NoContent, (await s.AttemptLoginAsync(s.ReaderEmail, s.OrganizationA)).StatusCode);
        await s.RefreshCsrfAsync();

        var ruleId = Guid.NewGuid();
        (HttpMethod Method, string Route)[] routes =
        [
            (HttpMethod.Get, $"/api/integrations/{id}/conflicts"),
            (HttpMethod.Post, $"/api/integrations/{id}/conflicts/{conflictId}/resolve"),
            (HttpMethod.Post, $"/api/integrations/{id}/conflicts/{conflictId}/ignore"),
            (HttpMethod.Get, $"/api/integrations/{id}/mappings"),
            (HttpMethod.Put, $"/api/integrations/{id}/mappings/Property"),
            (HttpMethod.Post, $"/api/integrations/{id}/mappings/WorkOrder/rules"),
            (HttpMethod.Delete, $"/api/integrations/{id}/mappings/WorkOrder/rules/{ruleId}"),
            (HttpMethod.Post, $"/api/integrations/{id}/mappings/Property/promote"),
            (HttpMethod.Post, $"/api/integrations/{id}/mappings/Property/report-only"),
            (HttpMethod.Get, $"/api/integrations/{id}/runs"),
            (HttpMethod.Get, $"/api/integrations/{id}/runs/{Guid.NewGuid()}"),
            (HttpMethod.Post, $"/api/integrations/{id}/records/{recordId}/retire"),
            (HttpMethod.Get, $"/api/integrations/{id}/records")
        ];

        foreach (var (method, route) in routes)
        {
            using var request = new HttpRequestMessage(method, route)
            {
                Content = JsonContent.Create(new { })
            };
            using var response = await s.Client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                $"{method} {route} answered {(int)response.StatusCode}, expected 403");
        }
    }

    // ==== 13. Tenant isolation through the routes ===========================================

    [Fact]
    public async Task The_other_tenant_reaches_none_of_the_administration_routes()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await ConfigureMappingAsync(s, id);
        await PromoteAllAsync(s, id);
        var report = await SyncAsync(s, id);
        var runId = report.GetProperty("runId").GetGuid();
        var recordId = (await LinksAsync(s, id))[0].Id;

        Assert.Equal(HttpStatusCode.NoContent, (await s.AttemptLoginAsync(s.EmailB, s.OrganizationB)).StatusCode);
        await s.RefreshCsrfAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/integrations/{id}/conflicts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/integrations/{id}/mappings")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/integrations/{id}/runs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/integrations/{id}/runs/{runId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await s.Client.PostAsync($"/api/integrations/{id}/records/{recordId}/retire", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await s.Client.PutAsJsonAsync($"/api/integrations/{id}/mappings/Property", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await s.Client.PostAsync($"/api/integrations/{id}/mappings/Property/promote", null)).StatusCode);

        // Org B's own integration list is empty, so nothing leaked into the collection routes.
        var list = await Json(await s.Client.GetAsync("/api/integrations"));
        Assert.Empty(list.EnumerateArray());
    }

    // ==== 14. The health badge tracks the queue =============================================

    [Fact]
    public async Task Open_conflicts_on_the_health_snapshot_tracks_the_queue()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);

        var clean = await Json(await s.Client.GetAsync($"/api/integrations/{id}"));
        Assert.Equal(0, clean.GetProperty("openConflicts").GetInt32());

        var report = await SyncAsync(s, id);
        var raised = await Json(await s.Client.GetAsync($"/api/integrations/{id}"));
        // Without this badge a connection raising the same conflict on every run looks identical to
        // a clean one: recent lastSucceededAt, zero failures, because a conflict is not a failure.
        Assert.Equal(Count(report, "conflicted"), raised.GetProperty("openConflicts").GetInt32());
        Assert.Equal(0, raised.GetProperty("consecutiveFailures").GetInt32());

        var conflictId = Items(await Conflicts(s, id))[0].GetProperty("id").GetGuid();
        using (var resolved = await s.Client.PostAsJsonAsync(
                   $"/api/integrations/{id}/conflicts/{conflictId}/resolve", new { note = "Handled." }))
            Assert.Equal(HttpStatusCode.NoContent, resolved.StatusCode);

        var afterResolve = await Json(await s.Client.GetAsync($"/api/integrations/{id}"));
        Assert.Equal(Count(report, "conflicted") - 1, afterResolve.GetProperty("openConflicts").GetInt32());
    }

    // ==== 16. Editing a profile demotes it ==================================================

    [Fact]
    public async Task Editing_an_auto_apply_profile_into_an_invalid_state_demotes_it()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await ConfigureMappingAsync(s, id);
        await PromoteAllAsync(s, id);

        // Clear the portfolio the Property profile needs. SetDefaults demotes in the safe
        // direction rather than leaving an AutoApply profile that cannot apply.
        await Put(s, $"/api/integrations/{id}/mappings/Property", new { defaultTimeZoneId = "America/New_York" });

        var profiles = await Json(await s.Client.GetAsync($"/api/integrations/{id}/mappings"));
        var property = profiles.EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "Property");
        Assert.Equal("ReportOnly", property.GetProperty("mode").GetString());
        Assert.False(property.GetProperty("canAutoApply").GetBoolean());

        // And the demotion is load-bearing: the next sync writes nothing.
        var before = await OperationsRowsAsync(s);
        var report = await SyncAsync(s, id);
        Assert.Equal(0, Count(report, "added"));
        Assert.True(before.SetEquals(await OperationsRowsAsync(s)), "a demoted profile must not write");
    }

    // ==== Rules: removal, and a rule that does not belong to the kind =======================

    [Fact]
    public async Task A_rule_for_another_kind_is_refused_and_a_removed_rule_stops_applying()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await ConfigureMappingAsync(s, id);

        // AssetKind does not apply to a WorkOrder profile — a caller mistake, reported as a 400
        // with a usable message rather than an exception from the profile.
        using (var wrongKind = await s.Client.PostAsJsonAsync($"/api/integrations/{id}/mappings/WorkOrder/rules",
                   new { sourceField = "AssetKind", sourceValue = "Hvac", targetValue = "Hvac" }))
            Assert.Equal(HttpStatusCode.BadRequest, wrongKind.StatusCode);

        using (var unknownField = await s.Client.PostAsJsonAsync($"/api/integrations/{id}/mappings/WorkOrder/rules",
                   new { sourceField = "Nonsense", sourceValue = "x", targetValue = "y" }))
            Assert.Equal(HttpStatusCode.BadRequest, unknownField.StatusCode);

        using (var unknownKind = await s.Client.PutAsJsonAsync($"/api/integrations/{id}/mappings/Nonsense", new { }))
            Assert.Equal(HttpStatusCode.BadRequest, unknownKind.StatusCode);

        // Removing the WaterHeater rule makes that asset's kind unmapped again.
        var profiles = await Json(await s.Client.GetAsync($"/api/integrations/{id}/mappings"));
        var assetProfile = profiles.EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "Asset");
        var ruleId = assetProfile.GetProperty("rules").EnumerateArray()
            .Single(x => x.GetProperty("sourceValue").GetString() == "WaterHeater").GetProperty("id").GetGuid();

        using (var removed = await s.Client.DeleteAsync($"/api/integrations/{id}/mappings/Asset/rules/{ruleId}"))
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using (var again = await s.Client.DeleteAsync($"/api/integrations/{id}/mappings/Asset/rules/{ruleId}"))
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);

        await PromoteAllAsync(s, id);
        await SyncAsync(s, id);
        Assert.Contains(await ConflictRowsAsync(s, id),
            x => x.Kind == IntegrationEntityKind.Asset && x.ExternalId == "MOCK-ASSET-1"
                && x.Reason == ConflictReason.UnmappedValue);
    }

    // ==== report-only is the way back ========================================================

    [Fact]
    public async Task Report_only_reverts_a_promoted_profile()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await ConfigureMappingAsync(s, id);
        await PromoteAllAsync(s, id);

        using (var reverted = await s.Client.PostAsync($"/api/integrations/{id}/mappings/Property/report-only", null))
            Assert.Equal(HttpStatusCode.NoContent, reverted.StatusCode);

        var profiles = await Json(await s.Client.GetAsync($"/api/integrations/{id}/mappings"));
        var property = profiles.EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "Property");
        Assert.Equal("ReportOnly", property.GetProperty("mode").GetString());
        // Still valid — reverting is a stance, not an invalidation.
        Assert.True(property.GetProperty("canAutoApply").GetBoolean());

        var before = await OperationsRowsAsync(s);
        await SyncAsync(s, id);
        Assert.True(before.SetEquals(await OperationsRowsAsync(s)), "a reverted profile must not write");
    }
}
