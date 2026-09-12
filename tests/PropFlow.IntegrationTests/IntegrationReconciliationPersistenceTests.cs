using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Domain.Integrations;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Integrations;
using Xunit;

namespace PropFlow.IntegrationTests;

// PF-S19.04. Proves the four reconciliation tables actually behave the way the domain assumes:
// the two partial unique indexes are real constraints, the grants really withhold DELETE, RLS
// really confines each table to its tenant, and every new column round-trips.
[Collection("PostgreSQL")]
public sealed class IntegrationReconciliationPersistenceTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> SeedConnectionAsync(Scenario s, Guid organization, string source = "mock")
    {
        var id = Guid.NewGuid();
        await using var store = s.Integrations(organization);
        store.Connections.Add(new IntegrationConnection(organization, id, source, "Mock property system"));
        await store.SaveChangesAsync();
        return id;
    }

    private static Conflict NewConflict(Guid organization, Guid connection, Guid run,
        ConflictReason reason = ConflictReason.UnmappedValue, string field = "Status") =>
        new(organization, Guid.NewGuid(), connection, IntegrationEntityKind.WorkOrder, "WO-1", reason, field,
            "Awaiting Parts", "InProgress", "No mapping rule covers 'Awaiting Parts'.", run, T0);

    private static string UniqueViolationOf(DbUpdateException exception) =>
        ((PostgresException)exception.InnerException!).SqlState;

    // --- Round trips --------------------------------------------------------------------------

    [Fact]
    public async Task A_mapping_profile_round_trips_with_its_rules_and_still_validates()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var connection = await SeedConnectionAsync(s, s.OrganizationA);
        var profileId = Guid.NewGuid();

        await using (var write = s.Integrations(s.OrganizationA))
        {
            var profile = new MappingProfile(s.OrganizationA, profileId, connection,
                IntegrationEntityKind.WorkOrder, T0);
            profile.SetDefaults(null, s.AdminA, null, T0);
            profile.AddRule(new MappingRule(s.OrganizationA, Guid.NewGuid(), profileId,
                MappingSourceField.WorkOrderStatus, "Open", "New"), T0);
            profile.AddRule(new MappingRule(s.OrganizationA, Guid.NewGuid(), profileId,
                MappingSourceField.WorkOrderStatus, "Closed", "Completed"), T0);
            write.MappingProfiles.Add(profile);
            await write.SaveChangesAsync();
        }

        await using var read = s.Integrations(s.OrganizationA);
        var loaded = await read.MappingProfiles.Include(x => x.Rules)
            .SingleAsync(x => x.Id == profileId);

        // The Rules navigation has to materialise, because Validate() and MapWorkStatus read it.
        Assert.Equal(2, loaded.Rules.Count);
        Assert.Equal(MappingMode.ReportOnly, loaded.Mode);
        Assert.Equal(s.AdminA, loaded.DefaultCreatorId);
        Assert.Equal(WorkStatus.New, loaded.MapWorkStatus("open"));
        Assert.Equal(WorkStatus.Completed, loaded.MapWorkStatus("CLOSED"));
        Assert.Null(loaded.MapWorkStatus("Awaiting Parts"));
        Assert.DoesNotContain(loaded.Validate(), i => i.Severity == MappingIssueSeverity.Error);
    }

    [Fact]
    public async Task Every_reconciliation_column_on_a_record_link_round_trips()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var connection = await SeedConnectionAsync(s, s.OrganizationA);
        var linkId = Guid.NewGuid();
        var internalId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        await using (var write = s.Integrations(s.OrganizationA))
        {
            var link = new ExternalRecordLink(s.OrganizationA, linkId, connection,
                IntegrationEntityKind.Resident, SyntheticExternalId.ForResident("OCC-1"));
            link.Observe("hash-a", T0, runId);
            link.Reconcile(internalId, "hash-a", runId, T0.AddSeconds(1));
            write.RecordLinks.Add(link);
            await write.SaveChangesAsync();
        }

        await using var read = s.Integrations(s.OrganizationA);
        var loaded = await read.RecordLinks.SingleAsync(x => x.Id == linkId);

        Assert.Equal("OCC-1#resident", loaded.ExternalId);
        Assert.Equal(IntegrationEntityKind.Resident, loaded.Kind);
        Assert.Equal(internalId, loaded.InternalId);
        Assert.Equal("hash-a", loaded.ContentHash);
        Assert.Equal("hash-a", loaded.ReconciledHash);
        Assert.Equal(runId, loaded.LastRunId);
        Assert.Equal(T0.AddSeconds(1), loaded.LastReconciledAt);
        Assert.False(loaded.NeedsReconciliation);
        Assert.Null(loaded.RetiredAt);
    }

    [Fact]
    public async Task An_external_id_longer_than_the_old_two_hundred_character_column_is_storable()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var connection = await SeedConnectionAsync(s, s.OrganizationA);
        // The widening exists for exactly this: a full-width source id plus the resident suffix.
        var externalId = SyntheticExternalId.ForResident(new string('o', SyntheticExternalId.MaxSourceLength));
        Assert.Equal(256, externalId.Length);

        await using (var write = s.Integrations(s.OrganizationA))
        {
            write.RecordLinks.Add(new ExternalRecordLink(s.OrganizationA, Guid.NewGuid(), connection,
                IntegrationEntityKind.Resident, externalId));
            await write.SaveChangesAsync();
        }

        await using var read = s.Integrations(s.OrganizationA);
        Assert.Equal(externalId, (await read.RecordLinks.SingleAsync()).ExternalId);
    }

    // --- The two partial unique indexes -------------------------------------------------------

    [Fact]
    public async Task A_second_open_conflict_for_the_same_divergence_is_refused_by_the_index()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var connection = await SeedConnectionAsync(s, s.OrganizationA);
        var run = Guid.NewGuid();

        await using (var first = s.Integrations(s.OrganizationA))
        {
            first.Conflicts.Add(NewConflict(s.OrganizationA, connection, run));
            await first.SaveChangesAsync();
        }

        await using (var duplicate = s.Integrations(s.OrganizationA))
        {
            duplicate.Conflicts.Add(NewConflict(s.OrganizationA, connection, Guid.NewGuid()));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, UniqueViolationOf(exception));
        }

        // A different reason or a different field is a different divergence, so both are allowed.
        await using (var other = s.Integrations(s.OrganizationA))
        {
            other.Conflicts.Add(NewConflict(s.OrganizationA, connection, run, ConflictReason.ValidationRefusal));
            other.Conflicts.Add(NewConflict(s.OrganizationA, connection, run, field: "Title"));
            await other.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task A_recurrence_after_resolution_is_allowed_because_the_index_only_covers_open_rows()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var connection = await SeedConnectionAsync(s, s.OrganizationA);

        await using (var write = s.Integrations(s.OrganizationA))
        {
            write.Conflicts.Add(NewConflict(s.OrganizationA, connection, Guid.NewGuid()));
            await write.SaveChangesAsync();
        }

        await using (var resolve = s.Integrations(s.OrganizationA))
        {
            (await resolve.Conflicts.SingleAsync()).Resolve(s.AdminA, T0.AddHours(1), "Added a rule.");
            await resolve.SaveChangesAsync();
        }

        await using (var again = s.Integrations(s.OrganizationA))
        {
            again.Conflicts.Add(NewConflict(s.OrganizationA, connection, Guid.NewGuid()));
            await again.SaveChangesAsync();
        }

        await using var read = s.Integrations(s.OrganizationA);
        Assert.Equal(2, await read.Conflicts.CountAsync());
        Assert.Equal(1, await read.Conflicts.CountAsync(x => x.Status == ConflictStatus.Open));
    }

    [Fact]
    public async Task A_second_running_sync_run_for_one_connection_is_refused_by_the_claim_index()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var connection = await SeedConnectionAsync(s, s.OrganizationA);

        await using (var first = s.Integrations(s.OrganizationA))
        {
            first.SyncRuns.Add(SyncRun.Begin(s.OrganizationA, Guid.NewGuid(), connection, SyncTrigger.Manual, T0));
            await first.SaveChangesAsync();
        }

        // This is the claim: the second dispatcher's insert must not land.
        await using (var second = s.Integrations(s.OrganizationA))
        {
            second.SyncRuns.Add(SyncRun.Begin(s.OrganizationA, Guid.NewGuid(), connection, SyncTrigger.Scheduled, T0));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, UniqueViolationOf(exception));
        }

        await using (var finish = s.Integrations(s.OrganizationA))
        {
            var run = await finish.SyncRuns.SingleAsync();
            run.Complete(new SyncCounts(5, 2, 1, 0, 1), new string('a', 64), T0.AddMinutes(1));
            await finish.SaveChangesAsync();
        }

        // Once the claim is released the next run starts normally.
        await using (var next = s.Integrations(s.OrganizationA))
        {
            next.SyncRuns.Add(SyncRun.Begin(s.OrganizationA, Guid.NewGuid(), connection, SyncTrigger.Retry,
                T0.AddMinutes(2), attemptNumber: 2));
            await next.SaveChangesAsync();
        }

        await using var read = s.Integrations(s.OrganizationA);
        var completed = await read.SyncRuns.SingleAsync(x => x.Status == SyncRunStatus.Completed);
        Assert.Equal(new SyncCounts(5, 2, 1, 0, 1), completed.Counts);
        Assert.Equal(new string('a', 64), completed.SnapshotHash);
        Assert.Equal(2, await read.SyncRuns.CountAsync());
    }

    [Fact]
    public async Task One_mapping_profile_per_connection_and_entity_kind()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var connection = await SeedConnectionAsync(s, s.OrganizationA);

        await using (var first = s.Integrations(s.OrganizationA))
        {
            first.MappingProfiles.Add(new MappingProfile(s.OrganizationA, Guid.NewGuid(), connection,
                IntegrationEntityKind.Property, T0));
            await first.SaveChangesAsync();
        }

        await using (var duplicate = s.Integrations(s.OrganizationA))
        {
            duplicate.MappingProfiles.Add(new MappingProfile(s.OrganizationA, Guid.NewGuid(), connection,
                IntegrationEntityKind.Property, T0));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, UniqueViolationOf(exception));
        }
    }

    // --- Grants: a conflict and a run are audit facts -----------------------------------------

    [Fact]
    public async Task The_runtime_role_cannot_delete_a_conflict_or_a_sync_run()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var connection = await SeedConnectionAsync(s, s.OrganizationA);

        await using (var write = s.Integrations(s.OrganizationA))
        {
            write.Conflicts.Add(NewConflict(s.OrganizationA, connection, Guid.NewGuid()));
            write.SyncRuns.Add(SyncRun.Begin(s.OrganizationA, Guid.NewGuid(), connection, SyncTrigger.Manual, T0));
            await write.SaveChangesAsync();
        }

        await using var store = s.Integrations(s.OrganizationA);
        var conflicts = await Assert.ThrowsAsync<PostgresException>(() =>
            store.Database.ExecuteSqlRawAsync("DELETE FROM integrations.\"Conflicts\""));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, conflicts.SqlState);

        var runs = await Assert.ThrowsAsync<PostgresException>(() =>
            store.Database.ExecuteSqlRawAsync("DELETE FROM integrations.\"SyncRuns\""));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, runs.SqlState);
    }

    [Fact]
    public async Task The_runtime_role_can_delete_mapping_configuration()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var connection = await SeedConnectionAsync(s, s.OrganizationA);
        var profileId = Guid.NewGuid();

        await using (var write = s.Integrations(s.OrganizationA))
        {
            var profile = new MappingProfile(s.OrganizationA, profileId, connection,
                IntegrationEntityKind.Asset, T0);
            profile.AddRule(new MappingRule(s.OrganizationA, Guid.NewGuid(), profileId,
                MappingSourceField.AssetKind, "furnace", "Hvac"), T0);
            write.MappingProfiles.Add(profile);
            await write.SaveChangesAsync();
        }

        await using (var delete = s.Integrations(s.OrganizationA))
        {
            delete.MappingProfiles.Remove(await delete.MappingProfiles.SingleAsync());
            await delete.SaveChangesAsync();
        }

        await using var read = s.Integrations(s.OrganizationA);
        Assert.Empty(await read.MappingProfiles.ToListAsync());
        // The rule went with it through the cascade.
        Assert.Empty(await read.MappingRules.ToListAsync());
    }

    // --- Tenant isolation ---------------------------------------------------------------------

    [Fact]
    public async Task Rls_confines_every_reconciliation_table_to_its_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var connectionA = await SeedConnectionAsync(s, s.OrganizationA);
        var runA = Guid.NewGuid();
        var profileA = Guid.NewGuid();

        await using (var write = s.Integrations(s.OrganizationA))
        {
            var profile = new MappingProfile(s.OrganizationA, profileA, connectionA,
                IntegrationEntityKind.WorkOrder, T0);
            profile.AddRule(new MappingRule(s.OrganizationA, Guid.NewGuid(), profileA,
                MappingSourceField.WorkOrderStatus, "Open", "New"), T0);
            write.MappingProfiles.Add(profile);
            write.SyncRuns.Add(SyncRun.Begin(s.OrganizationA, runA, connectionA, SyncTrigger.Manual, T0));
            write.Conflicts.Add(NewConflict(s.OrganizationA, connectionA, runA));
            await write.SaveChangesAsync();
        }

        await using var other = s.Integrations(s.OrganizationB);

        // Filter-bypass reads from the other tenant see nothing, because the policy is FORCEd.
        Assert.Empty(await other.MappingProfiles.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await other.MappingRules.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await other.SyncRuns.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await other.Conflicts.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await other.Conflicts
            .FromSqlRaw("SELECT * FROM integrations.\"Conflicts\"").IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await other.SyncRuns
            .FromSqlRaw("SELECT * FROM integrations.\"SyncRuns\"").IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Rls_rejects_raw_cross_tenant_inserts_into_the_reconciliation_tables()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var connectionA = await SeedConnectionAsync(s, s.OrganizationA);

        await using var store = s.Integrations(s.OrganizationB);

        var profile = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO integrations."MappingProfiles"
               ("OrganizationId", "Id", "ConnectionId", "Kind", "Mode", "CreatedAt", "UpdatedAt")
             VALUES ({s.OrganizationA}, {Guid.NewGuid()}, {connectionA}, 'WorkOrder', 'AutoApply', now(), now())
             """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, profile.SqlState);

        var run = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO integrations."SyncRuns"
               ("OrganizationId", "Id", "ConnectionId", "Trigger", "AttemptNumber", "Status", "StartedAt",
                "HeartbeatAt", "Seen", "Added", "Updated", "Failed", "Conflicted")
             VALUES ({s.OrganizationA}, {Guid.NewGuid()}, {connectionA}, 'Manual', 1, 'Running', now(), now(), 0, 0, 0, 0, 0)
             """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, run.SqlState);

        var conflict = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO integrations."Conflicts"
               ("OrganizationId", "Id", "ConnectionId", "Kind", "ExternalId", "Reason", "Field", "Status",
                "FirstSeenInRunId", "LastSeenInRunId", "FirstSeenAt", "LastSeenAt", "ObservationCount")
             VALUES ({s.OrganizationA}, {Guid.NewGuid()}, {connectionA}, 'WorkOrder', 'forged', 'UnmappedValue',
                     'Status', 'Open', {Guid.NewGuid()}, {Guid.NewGuid()}, now(), now(), 1)
             """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, conflict.SqlState);
    }
}
