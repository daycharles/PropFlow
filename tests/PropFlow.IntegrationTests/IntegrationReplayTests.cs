using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PropFlow.Application.Integrations;
using PropFlow.Domain.Integrations;
using PropFlow.Infrastructure.Integrations;
using Xunit;

namespace PropFlow.IntegrationTests;

// PF-S19.10, part one: replay safety and the reconciler's decision table, driven end to end.
//
// EfIntegrationReconciler had no end-to-end test before this file. ReconciliationRules is pure and
// unit-tested, but nothing exercised the two-commit ordering, the cascade of resolved parents, the
// retirement sweep, or the claim index against a real insert.
//
// Part two (the operator-facing API: conflicts, mappings, runs, retire) is
// IntegrationAdministrationApiTests.
[Collection("PostgreSQL")]
public sealed class IntegrationReplayTests(DatabaseFixture fixture)
{
    // A stand-in for MockIntegrationAdapter whose snapshot can be changed mid-test. The real mock
    // is deliberately fixed, which makes "a changed external record updates in place" untestable
    // against it: without a mutable source the only reachable assertion is "nothing ever changes",
    // which is the weaker half of idempotence.
    internal sealed class MutableMockAdapter : IIntegrationAdapter
    {
        public IntegrationSnapshot Snapshot { get; set; } = Baseline;
        public string SourceSystem => MockIntegrationAdapter.Source;
        public string DisplayName => "Mock property system";
        public IntegrationAdapterDescriptor Descriptor => IntegrationAdapterDescriptor.FullPropertyManagementSnapshot;
        public Task<IntegrationSnapshot> PullAsync(IntegrationPullContext context, CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot);
    }

    // Same ids and shape as MockIntegrationAdapter, so a count here and a count in
    // IntegrationsApiTests describe the same feed: 2 properties, 3 spaces, 2 occupancies,
    // 2 work orders, 2 assets.
    internal static IntegrationSnapshot Baseline => new(
        MockIntegrationAdapter.Source,
        Properties:
        [
            new CanonicalProperty("MOCK-PROP-1", "Cedar Court", "100 Cedar St", "Norfolk", "VA", "23510", "America/New_York"),
            new CanonicalProperty("MOCK-PROP-2", "Birch Terrace", "220 Birch Ave", "Norfolk", "VA", "23517", "America/New_York")
        ],
        Spaces:
        [
            new CanonicalSpace("MOCK-SPACE-1", "MOCK-PROP-1", "101", null),
            new CanonicalSpace("MOCK-SPACE-2", "MOCK-PROP-1", "102", null),
            new CanonicalSpace("MOCK-SPACE-3", "MOCK-PROP-2", "1A", null)
        ],
        Occupancies:
        [
            new CanonicalOccupancy("MOCK-OCC-1", "MOCK-SPACE-1", "Alex Turner", "alex.turner@example.test", "+15550110001",
                new DateOnly(2025, 3, 1), null),
            new CanonicalOccupancy("MOCK-OCC-2", "MOCK-SPACE-3", "Priya Nair", "priya.nair@example.test", null,
                new DateOnly(2024, 11, 15), null)
        ],
        WorkOrders:
        [
            new CanonicalWorkOrder("MOCK-WO-1", "MOCK-PROP-1", "MOCK-SPACE-1", "Leaking kitchen faucet",
                "Reported by resident, steady drip", "Open", new DateTimeOffset(2026, 8, 20, 14, 0, 0, TimeSpan.Zero), null),
            new CanonicalWorkOrder("MOCK-WO-2", "MOCK-PROP-2", null, "Annual fire-alarm inspection",
                null, "Closed", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 1, 11, 30, 0, TimeSpan.Zero))
        ],
        Assets:
        [
            new CanonicalAsset("MOCK-ASSET-1", "MOCK-PROP-1", "MOCK-SPACE-1", "Water heater", "WaterHeater",
                "Rheem", "XE40", "RH-4021-8873", new DateOnly(2021, 5, 10)),
            new CanonicalAsset("MOCK-ASSET-2", "MOCK-PROP-2", null, "Rooftop HVAC", "Hvac",
                "Carrier", "48TC", "CR-9932-1120", new DateOnly(2019, 9, 2))
        ]);

    // Replaces the registered MockIntegrationAdapter and leaves SandboxIntegrationAdapter alone, so
    // "mock" is mutable and a second source system is still available for a second connection.
    internal static Task<Scenario> ScenarioAsync(DatabaseFixture fixture, MutableMockAdapter adapter) =>
        fixture.CreateScenarioAsync(builder => builder.ConfigureServices(services =>
        {
            var registered = services.Single(d => d.ServiceType == typeof(IIntegrationAdapter)
                && d.ImplementationType == typeof(MockIntegrationAdapter));
            services.Remove(registered);
            services.AddSingleton<IIntegrationAdapter>(adapter);
        }));

    internal static async Task<Guid> ConnectAsync(Scenario s, string source = MockIntegrationAdapter.Source)
    {
        using var response = await s.Client.PostAsJsonAsync("/api/integrations",
            new { sourceSystem = source, displayName = $"{source} system" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    internal static async Task<JsonElement> SyncAsync(Scenario s, Guid connectionId)
    {
        using var response = await s.Client.PostAsync($"/api/integrations/{connectionId}/sync", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    internal static int Count(JsonElement report, string name) => report.GetProperty(name).GetInt32();

    // The mapping an operator supplies through the API to make this feed reconcilable. Property and
    // WorkOrder need profile facts the canonical records do not carry; Asset needs kind rules.
    // Space and Occupancy need no profile of their own — they inherit the Property profile's mode.
    internal static async Task ConfigureMappingAsync(Scenario s, Guid id)
    {
        await Put(s, $"/api/integrations/{id}/mappings/Property",
            new { targetPortfolioId = s.PortfolioA, defaultTimeZoneId = "America/New_York" });
        await Put(s, $"/api/integrations/{id}/mappings/WorkOrder", new { defaultCreatorId = s.AdminA });
        await Rule(s, id, "WorkOrder", "WorkOrderStatus", "Open", "New");
        await Rule(s, id, "WorkOrder", "WorkOrderStatus", "Closed", "Completed");
        await Rule(s, id, "Asset", "AssetKind", "WaterHeater", "WaterHeater");
        await Rule(s, id, "Asset", "AssetKind", "Hvac", "Hvac");
    }

    internal static async Task PromoteAllAsync(Scenario s, Guid id)
    {
        foreach (var kind in new[] { "Property", "WorkOrder", "Asset" })
        {
            using var response = await s.Client.PostAsync($"/api/integrations/{id}/mappings/{kind}/promote", null);
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"promote {kind} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    internal static async Task Put(Scenario s, string route, object body)
    {
        using var response = await s.Client.PutAsJsonAsync(route, body);
        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"{route} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    internal static async Task<Guid> Rule(Scenario s, Guid id, string kind, string field, string from, string to)
    {
        using var response = await s.Client.PostAsJsonAsync($"/api/integrations/{id}/mappings/{kind}/rules",
            new { sourceField = field, sourceValue = from, targetValue = to });
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"rule {kind}/{field} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    // The full row tuple of everything the reconciler writes, Ids included. The Ids are the point:
    // count equality would pass if a row were deleted and re-created under a new id, which is
    // exactly the failure replay safety exists to prevent.
    internal static async Task<SortedSet<string>> OperationsRowsAsync(Scenario s)
    {
        await using var store = s.Store(s.OrganizationA);
        var rows = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var x in await store.Properties.AsNoTracking().ToListAsync())
            rows.Add($"Property|{x.Id}|{x.PortfolioId}|{x.Name}|{x.TimeZoneId}");
        foreach (var x in await store.Spaces.AsNoTracking().ToListAsync())
            rows.Add($"Space|{x.Id}|{x.PropertyId}|{x.BuildingId}|{x.Code}");
        foreach (var x in await store.Residents.AsNoTracking().ToListAsync())
            rows.Add($"Resident|{x.Id}|{x.FullName}|{x.Email}|{x.Phone}");
        foreach (var x in await store.Occupancies.AsNoTracking().ToListAsync())
            rows.Add($"Occupancy|{x.Id}|{x.ResidentId}|{x.SpaceId}|{x.MovedInOn:O}|{x.MovedOutOn:O}");
        foreach (var x in await store.WorkItems.AsNoTracking().ToListAsync())
            rows.Add($"WorkItem|{x.Id}|{x.PropertyId}|{x.SpaceId}|{x.Title}|{x.Status}|{x.CreatorId}");
        foreach (var x in await store.Assets.AsNoTracking().ToListAsync())
            rows.Add($"Asset|{x.Id}|{x.PropertyId}|{x.SpaceId}|{x.Name}|{x.Kind}|{x.SerialNumber}");
        return rows;
    }

    // The other half of set equality: which external record points at which PropFlow row.
    internal static async Task<SortedSet<string>> LinkTargetsAsync(Scenario s, Guid connectionId)
    {
        var links = await LinksAsync(s, connectionId);
        return new SortedSet<string>(links.Select(x => $"{x.Kind}|{x.ExternalId}|{x.InternalId}"), StringComparer.Ordinal);
    }

    internal static async Task<List<ExternalRecordLink>> LinksAsync(Scenario s, Guid connectionId)
    {
        await using var store = s.Integrations(s.OrganizationA);
        return await store.RecordLinks.AsNoTracking().Where(x => x.ConnectionId == connectionId).ToListAsync();
    }

    internal static async Task<List<Conflict>> ConflictRowsAsync(Scenario s, Guid connectionId)
    {
        await using var store = s.Integrations(s.OrganizationA);
        return await store.Conflicts.AsNoTracking().Where(x => x.ConnectionId == connectionId).ToListAsync();
    }

    // ==== Fact 1: a third consecutive sync changes nothing ==================================

    [Fact]
    public async Task A_third_consecutive_sync_changes_nothing_in_the_operations_schema()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await ConfigureMappingAsync(s, id);
        await PromoteAllAsync(s, id);

        var first = await SyncAsync(s, id);
        Assert.Equal("Completed", first.GetProperty("outcome").GetString());
        Assert.True(Count(first, "added") > 0, $"the first sync must create rows; report was {first}");
        Assert.Equal(0, Count(first, "failed"));
        Assert.Equal(0, Count(first, "conflicted"));

        var afterFirst = await OperationsRowsAsync(s);
        var linksAfterFirst = await LinkTargetsAsync(s, id);
        var second = await SyncAsync(s, id);
        var afterSecond = await OperationsRowsAsync(s);
        var third = await SyncAsync(s, id);
        var afterThird = await OperationsRowsAsync(s);

        foreach (var report in new[] { second, third })
        {
            Assert.Equal(0, Count(report, "added"));
            Assert.Equal(0, Count(report, "updated"));
            Assert.Equal(0, Count(report, "retired"));
            Assert.Equal(0, Count(report, "failed"));
        }

        // SET equality on full row tuples including Ids, not count equality. A delete-and-recreate
        // keeps the count and changes the set, and that is the failure being guarded against.
        Assert.True(afterFirst.SetEquals(afterSecond), Diff(afterFirst, afterSecond));
        Assert.True(afterSecond.SetEquals(afterThird), Diff(afterSecond, afterThird));
        var linksAfterThird = await LinkTargetsAsync(s, id);
        Assert.True(linksAfterFirst.SetEquals(linksAfterThird), Diff(linksAfterFirst, linksAfterThird));

        // And the fast path is genuinely being taken rather than the rows happening to match.
        Assert.All(await LinksAsync(s, id), link =>
        {
            Assert.Equal(SyncState.Synced, link.SyncState);
            Assert.NotNull(link.InternalId);
            Assert.False(link.NeedsReconciliation);
        });
    }

    // ==== Fact 2: a crash between the two commits converges on replay =======================

    [Fact]
    public async Task A_sync_interrupted_after_the_domain_write_converges_on_replay()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await ConfigureMappingAsync(s, id);
        await PromoteAllAsync(s, id);
        await SyncAsync(s, id);

        var before = await OperationsRowsAsync(s);
        var link = (await LinksAsync(s, id))
            .Single(x => x.Kind == IntegrationEntityKind.Property && x.ExternalId == "MOCK-PROP-1");
        var internalId = link.InternalId!.Value;

        // Reproduce the crash window: the domain row was committed, the link was not. Writes are
        // domain-first and hash-last with no distributed transaction, so this state is reachable in
        // production whenever a process dies between the two SaveChanges calls. The admin
        // connection is used because no application path produces it on purpose.
        await using (var admin = new NpgsqlConnection(fixture.AdminConnection))
        {
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = """UPDATE integrations."RecordLinks" SET "ReconciledHash" = NULL WHERE "Id" = @id""";
            command.Parameters.AddWithValue("id", link.Id);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var replay = await SyncAsync(s, id);
        // The next run re-decides Update and re-applies the same values to the same InternalId.
        Assert.Equal(0, Count(replay, "added"));
        Assert.Equal(1, Count(replay, "updated"));
        Assert.Equal(0, Count(replay, "failed"));

        var after = await OperationsRowsAsync(s);
        Assert.True(before.SetEquals(after), Diff(before, after));

        await using (var store = s.Store(s.OrganizationA))
        {
            // Same Id, and no second row: the replay converged rather than duplicating.
            Assert.Single(await store.Properties.Where(x => x.Id == internalId).ToListAsync());
            // One seeded property plus the two imported ones, and no third copy of MOCK-PROP-1.
            Assert.Equal(3, await store.Properties.CountAsync(x => x.PortfolioId == s.PortfolioA));
        }
        var healed = (await LinksAsync(s, id))
            .Single(x => x.Kind == IntegrationEntityKind.Property && x.ExternalId == "MOCK-PROP-1");
        Assert.Equal(internalId, healed.InternalId);
        Assert.False(healed.NeedsReconciliation);
    }

    // ==== Fact 3: a changed record updates in place, then replays clean =====================

    [Fact]
    public async Task A_changed_external_record_updates_in_place_and_then_replays_clean()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await ConfigureMappingAsync(s, id);
        await PromoteAllAsync(s, id);
        await SyncAsync(s, id);

        var link = (await LinksAsync(s, id))
            .Single(x => x.Kind == IntegrationEntityKind.Property && x.ExternalId == "MOCK-PROP-1");
        var internalId = link.InternalId!.Value;

        // The source renames a property and re-titles a work order.
        adapter.Snapshot = Baseline with
        {
            Properties =
            [
                new CanonicalProperty("MOCK-PROP-1", "Cedar Court North", "100 Cedar St", "Norfolk", "VA", "23510", "America/New_York"),
                Baseline.Properties[1]
            ],
            WorkOrders =
            [
                Baseline.WorkOrders[0] with { Title = "Leaking kitchen faucet (escalated)" },
                Baseline.WorkOrders[1]
            ]
        };

        var changed = await SyncAsync(s, id);
        Assert.Equal(0, Count(changed, "added"));
        Assert.Equal(2, Count(changed, "updated"));
        Assert.Equal(0, Count(changed, "failed"));
        Assert.Equal(0, Count(changed, "retired"));

        await using (var store = s.Store(s.OrganizationA))
        {
            // Updated in place: same row id, new value. Not a delete-and-recreate.
            var property = await store.Properties.SingleAsync(x => x.Id == internalId);
            Assert.Equal("Cedar Court North", property.Name);
            // One seeded property plus the two imported ones, and no third copy of MOCK-PROP-1.
            Assert.Equal(3, await store.Properties.CountAsync(x => x.PortfolioId == s.PortfolioA));
            Assert.Contains(await store.WorkItems.ToListAsync(), x => x.Title == "Leaking kitchen faucet (escalated)");
        }

        // Idempotence is not "never writes anything": the very next run is clean again.
        var afterChange = await OperationsRowsAsync(s);
        var replay = await SyncAsync(s, id);
        Assert.Equal(0, Count(replay, "added"));
        Assert.Equal(0, Count(replay, "updated"));
        var afterReplay = await OperationsRowsAsync(s);
        Assert.True(afterChange.SetEquals(afterReplay), Diff(afterChange, afterReplay));
    }

    // ==== The ReportOnly guard rail =========================================================

    [Fact]
    public async Task A_report_only_profile_raises_conflicts_and_writes_nothing_to_operations()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);

        // A real profile, in the mode every profile is born in, still missing the portfolio a
        // Property needs. A first connect silently rewriting a tenant's rows is the support
        // incident this mode exists to prevent.
        using (var created = await s.Client.PutAsJsonAsync($"/api/integrations/{id}/mappings/Property",
                   new { defaultTimeZoneId = "America/New_York" }))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var before = await OperationsRowsAsync(s);
        var report = await SyncAsync(s, id);

        Assert.Equal("Completed", report.GetProperty("outcome").GetString());
        Assert.Equal(0, Count(report, "added"));
        Assert.Equal(0, Count(report, "updated"));
        Assert.True(Count(report, "conflicted") > 0, "ReportOnly still reports: that is the point of the mode.");

        // Nothing at all reached the operations schema.
        var after = await OperationsRowsAsync(s);
        Assert.True(before.SetEquals(after), Diff(before, after));
        Assert.All(await LinksAsync(s, id), link => Assert.Null(link.InternalId));

        // And the profile really is ReportOnly rather than absent.
        var profiles = await s.Client.GetFromJsonAsync<JsonElement>($"/api/integrations/{id}/mappings");
        var property = profiles.EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "Property");
        Assert.Equal("ReportOnly", property.GetProperty("mode").GetString());
        Assert.False(property.GetProperty("canAutoApply").GetBoolean());
    }

    // ==== The measured conflict set on a first unconfigured sync ============================

    // IntegrationsApiTests asserts only `conflicted > 0`. This pins the actual set, because the
    // number is a claim about the decision table and a claim about the mock feed at once.
    [Fact]
    public async Task An_unconfigured_first_sync_conflicts_on_exactly_the_records_with_mapping_gaps()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);

        var report = await SyncAsync(s, id);
        var actual = (await ConflictRowsAsync(s, id))
            .Select(x => $"{x.Kind}|{x.ExternalId}|{x.Reason}|{x.Field}")
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        // Step 1 of the decision table (a mapping gap) runs BEFORE the parent checks, so an asset
        // whose Kind no profile can map conflicts even though its parent property never reconciled.
        // Spaces and occupancies have no intrinsic gap, so they are skipped rather than conflicted.
        string[] expected =
        [
            "Asset|MOCK-ASSET-1|UnmappedValue|Kind",
            "Asset|MOCK-ASSET-2|UnmappedValue|Kind",
            "Property|MOCK-PROP-1|MissingRequiredMapping|TargetPortfolioId",
            "Property|MOCK-PROP-2|MissingRequiredMapping|TargetPortfolioId",
            "WorkOrder|MOCK-WO-1|MissingRequiredMapping|DefaultCreatorId",
            "WorkOrder|MOCK-WO-2|MissingRequiredMapping|DefaultCreatorId"
        ];
        Assert.Equal(expected, actual);
        Assert.Equal(expected.Length, Count(report, "conflicted"));
    }

    // ==== Asset: absent Kind is not the same as unmapped Kind ===============================

    [Fact]
    public async Task A_blank_asset_kind_imports_as_other_while_an_unmapped_one_conflicts()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await ConfigureMappingAsync(s, id);
        await PromoteAllAsync(s, id);

        adapter.Snapshot = Baseline with
        {
            Assets =
            [
                // Absent: the source does not classify its assets. Other is the honest import.
                Baseline.Assets[0] with { Kind = "   " },
                // Unmapped: the source DOES classify them and PropFlow does not know the word.
                // Importing this as Other would silently discard what the source told us.
                Baseline.Assets[1] with { Kind = "Zorkmid" }
            ]
        };

        var report = await SyncAsync(s, id);
        Assert.Equal(1, Count(report, "conflicted"));

        var conflict = Assert.Single(await ConflictRowsAsync(s, id));
        Assert.Equal(IntegrationEntityKind.Asset, conflict.Kind);
        Assert.Equal("MOCK-ASSET-2", conflict.ExternalId);
        Assert.Equal(ConflictReason.UnmappedValue, conflict.Reason);
        Assert.Equal("Kind", conflict.Field);
        Assert.Equal("Zorkmid", conflict.ObservedValue);

        await using var store = s.Store(s.OrganizationA);
        var imported = await store.Assets.SingleAsync(x => x.SerialNumber == "RH-4021-8873");
        Assert.Equal(PropFlow.Domain.Assets.AssetKind.Other, imported.Kind);
        Assert.DoesNotContain(await store.Assets.ToListAsync(), x => x.SerialNumber == "CR-9932-1120");
    }

    // ==== The claim mechanism ===============================================================

    // If the partial unique index does not actually bite, the claim silently stops being a claim
    // and two dispatchers both proceed against the same connection.
    [Fact]
    public async Task A_held_claim_stops_a_second_sync_and_a_second_running_row_is_refused()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);

        await using (var store = s.Integrations(s.OrganizationA))
        {
            store.SyncRuns.Add(SyncRun.Begin(s.OrganizationA, Guid.NewGuid(), id, SyncTrigger.Manual,
                DateTimeOffset.UtcNow));
            await store.SaveChangesAsync();
        }

        // A single-threaded test cannot race the claim, so the held row stands in for the loser's
        // view of the race. The sync route sees it and answers 409 rather than syncing twice.
        using (var blocked = await s.Client.PostAsync($"/api/integrations/{id}/sync", null))
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        // And the index itself refuses a second Running row for the same connection, which is the
        // mechanism the route depends on.
        await using (var store = s.Integrations(s.OrganizationA))
        {
            store.SyncRuns.Add(SyncRun.Begin(s.OrganizationA, Guid.NewGuid(), id, SyncTrigger.Scheduled,
                DateTimeOffset.UtcNow));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.Equal("IX_SyncRuns_ActiveClaim", postgres.ConstraintName);
        }
    }

    // Without the reclaim sweep one crashed worker disables a connection permanently.
    [Fact]
    public async Task A_stale_running_row_is_reclaimed_so_the_connection_can_sync_again()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await ConfigureMappingAsync(s, id);
        await PromoteAllAsync(s, id);

        var staleRunId = Guid.NewGuid();
        await using (var store = s.Integrations(s.OrganizationA))
        {
            store.SyncRuns.Add(SyncRun.Begin(s.OrganizationA, staleRunId, id, SyncTrigger.Scheduled,
                DateTimeOffset.UtcNow));
            await store.SaveChangesAsync();
        }
        // Age the heartbeat past any sane StaleRunTimeout. Again the admin connection, because a
        // crashed worker is a state no application path produces on purpose.
        await using (var admin = new NpgsqlConnection(fixture.AdminConnection))
        {
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = """
                UPDATE integrations."SyncRuns"
                   SET "HeartbeatAt" = now() - interval '1 day', "StartedAt" = now() - interval '1 day'
                 WHERE "Id" = @id
                """;
            command.Parameters.AddWithValue("id", staleRunId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        using (var blocked = await s.Client.PostAsync($"/api/integrations/{id}/sync", null))
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        // The relay's reclaim sweep runs before it attempts anything. Driven directly rather than
        // through the hosted timer, for the reason in .claude/rules/traps.md.
        var relay = s.Factory.Services.GetRequiredService<IntegrationSyncRelay>();
        await relay.SyncDueAsync(CancellationToken.None);

        await using (var store = s.Integrations(s.OrganizationA))
        {
            var reclaimed = await store.SyncRuns.AsNoTracking().SingleAsync(x => x.Id == staleRunId);
            Assert.Equal(SyncRunStatus.Failed, reclaimed.Status);
            Assert.Equal(SyncRun.ReclaimedError, reclaimed.Error);
        }

        // The claim is released, so the connection syncs again without a human touching anything.
        var recovered = await SyncAsync(s, id);
        Assert.Equal("Completed", recovered.GetProperty("outcome").GetString());
    }

    // ==== Retirement ==========================================================================

    [Fact]
    public async Task A_record_the_source_stops_reporting_is_retired_and_its_propflow_row_survives()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await ConfigureMappingAsync(s, id);
        await PromoteAllAsync(s, id);
        await SyncAsync(s, id);

        var link = (await LinksAsync(s, id))
            .Single(x => x.Kind == IntegrationEntityKind.Asset && x.ExternalId == "MOCK-ASSET-2");
        var internalId = link.InternalId!.Value;

        adapter.Snapshot = Baseline with { Assets = [Baseline.Assets[0]] };
        var report = await SyncAsync(s, id);
        Assert.Equal(1, Count(report, "retired"));

        var retired = (await LinksAsync(s, id))
            .Single(x => x.Kind == IntegrationEntityKind.Asset && x.ExternalId == "MOCK-ASSET-2");
        Assert.Equal(SyncState.Retired, retired.SyncState);
        Assert.Contains(await ConflictRowsAsync(s, id),
            x => x.Reason == ConflictReason.UpstreamDisappearance && x.ExternalId == "MOCK-ASSET-2");

        // Retiring a link is never a delete on the PropFlow side.
        await using var store = s.Store(s.OrganizationA);
        Assert.Single(await store.Assets.Where(x => x.Id == internalId).ToListAsync());
    }

    // ==== Tenant isolation of a reconciled feed ===============================================

    [Fact]
    public async Task A_reconciled_feed_is_invisible_to_the_other_tenant()
    {
        var adapter = new MutableMockAdapter();
        await using var s = await ScenarioAsync(fixture, adapter);
        await s.LoginAsync();
        var id = await ConnectAsync(s);
        await ConfigureMappingAsync(s, id);
        await PromoteAllAsync(s, id);
        await SyncAsync(s, id);

        Assert.Equal(HttpStatusCode.NoContent, (await s.AttemptLoginAsync(s.EmailB, s.OrganizationB)).StatusCode);
        await s.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/integrations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsync($"/api/integrations/{id}/sync", null)).StatusCode);

        await using var other = s.Integrations(s.OrganizationB);
        Assert.Empty(await other.RecordLinks.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await other.Conflicts.IgnoreQueryFilters().ToListAsync());
        await using var otherOperations = s.Store(s.OrganizationB);
        Assert.DoesNotContain(await otherOperations.Properties.IgnoreQueryFilters().ToListAsync(),
            x => x.Name == "Cedar Court");
    }

    internal static string Diff(SortedSet<string> expected, SortedSet<string> actual)
    {
        var missing = expected.Except(actual).ToList();
        var extra = actual.Except(expected).ToList();
        return $"rows differ.\n  disappeared ({missing.Count}):\n    {string.Join("\n    ", missing)}"
            + $"\n  appeared ({extra.Count}):\n    {string.Join("\n    ", extra)}";
    }
}
