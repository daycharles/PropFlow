using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PropFlow.Application;
using PropFlow.Application.Automation;
using PropFlow.Domain.Automation;
using PropFlow.Domain.Configuration;
using PropFlow.Domain.People;
using PropFlow.Domain.Timeline;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Persistence;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class IsolationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Api_filters_reads_and_conceals_foreign_work_and_timeline()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        s.Client.DefaultRequestHeaders.Add("X-Organization-Id", s.OrganizationB.ToString());
        var list = await s.Client.GetFromJsonAsync<JsonElement>($"/api/work/?organizationId={s.OrganizationB}");
        Assert.Equal(s.WorkA, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/work/{s.WorkB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/work/{s.WorkB}/timeline")).StatusCode);
    }

    [Fact]
    public async Task Cross_tenant_assignment_is_rejected_without_mutation_or_audit()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await s.AssignAsync(s.WorkA, s.VendorB)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.AssignAsync(s.WorkB, s.VendorA)).StatusCode);
        await using var a = s.Store(s.OrganizationA);
        await using var b = s.Store(s.OrganizationB);
        Assert.Null((await a.WorkItems.SingleAsync()).VendorId);
        Assert.Null((await b.WorkItems.SingleAsync()).VendorId);
        Assert.Empty(await a.Timeline.ToListAsync());
        Assert.Empty(await b.Timeline.ToListAsync());
    }

    [Fact]
    public async Task Assignment_persists_with_authenticated_actor_and_no_duplicate_history()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var response = await s.Client.PostAsJsonAsync($"/api/work/{s.WorkA}/vendor", new
        {
            vendorId = s.VendorA, organizationId = s.OrganizationB, actorId = s.AdminB, role = "Organization Admin"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.AssignAsync(s.WorkA, s.VendorA)).StatusCode);
        await using var store = s.Store(s.OrganizationA);
        Assert.Equal(s.VendorA, (await store.WorkItems.SingleAsync()).VendorId);
        var audit = Assert.Single(await store.Timeline.ToListAsync());
        Assert.Equal(s.AdminA, audit.ActorId);
        Assert.Equal(s.OrganizationA, audit.OrganizationId);
        Assert.Null(audit.OldValue);
        Assert.Equal(s.VendorA.ToString(), audit.NewValue);
        Assert.Equal(nameof(VendorAssigned), audit.EventType);
        Assert.Equal("WorkItem", audit.RelatedObjectType);
    }

    [Fact]
    public async Task Rls_blocks_filter_bypass_raw_reads_and_bulk_writes()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var store = s.Store(s.OrganizationA);
        Assert.Equal(s.WorkA, Assert.Single(await store.WorkItems.IgnoreQueryFilters().ToListAsync()).Id);
        Assert.Equal(s.WorkA, Assert.Single(await store.WorkItems.FromSqlRaw("SELECT *, xmin FROM operations.\"WorkItems\"").IgnoreQueryFilters().ToListAsync()).Id);
        Assert.Equal(0, await store.WorkItems.IgnoreQueryFilters().Where(x => x.Id == s.WorkB).ExecuteDeleteAsync());
        Assert.Equal(0, await store.Database.ExecuteSqlInterpolatedAsync($"UPDATE operations.\"WorkItems\" SET \"Title\" = 'tampered' WHERE \"Id\" = {s.WorkB}"));
        await using var other = s.Store(s.OrganizationB);
        Assert.Equal("Private work", (await other.WorkItems.SingleAsync()).Title);
    }

    [Fact]
    public async Task Rls_rejects_raw_cross_tenant_inserts()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var store = s.Store(s.OrganizationA);
        var id = Guid.NewGuid();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO operations.\"WorkItems\" (\"OrganizationId\", \"Id\", \"Title\") VALUES ({s.OrganizationB}, {id}, 'forged')"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task Rls_confines_the_attachments_table_to_its_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            admin.Attachments.Add(Attachment.Create(s.OrganizationA, Guid.NewGuid(), s.WorkA, "a.pdf",
                "application/pdf", 12, $"{s.OrganizationA:N}/x/a.pdf", residentVisible: false, DateTimeOffset.UtcNow, null));
            await admin.SaveChangesAsync();
        }

        await using var store = s.Store(s.OrganizationB);
        // Filter-bypass read from the other tenant sees nothing.
        Assert.Empty(await store.Attachments.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await store.Attachments.FromSqlRaw("SELECT * FROM operations.\"Attachments\"").IgnoreQueryFilters().ToListAsync());
        // Raw cross-tenant insert is refused by the forced RLS policy.
        var exception = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO operations.\"Attachments\" (\"OrganizationId\", \"Id\", \"WorkId\", \"FileName\", \"ContentType\", \"Length\", \"StorageKey\", \"ResidentVisible\", \"CreatedAt\") VALUES ({s.OrganizationA}, {Guid.NewGuid()}, {s.WorkA}, 'f.pdf', 'application/pdf', 1, 'k', false, now())"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task Rls_confines_custom_field_definitions_to_their_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            admin.CustomFieldDefinitions.Add(new CustomFieldDefinition(s.OrganizationA, Guid.NewGuid(), "warranty_note",
                "Warranty note", CustomFieldAppliesTo.WorkItem, CustomFieldType.Text, null, false, 0, DateTimeOffset.UtcNow));
            await admin.SaveChangesAsync();
        }

        await using var store = s.Store(s.OrganizationB);
        Assert.Empty(await store.CustomFieldDefinitions.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await store.CustomFieldDefinitions.FromSqlRaw("SELECT * FROM operations.\"CustomFieldDefinitions\"").IgnoreQueryFilters().ToListAsync());
        var exception = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO operations.\"CustomFieldDefinitions\" (\"OrganizationId\", \"Id\", \"Key\", \"Name\", \"AppliesTo\", \"FieldType\", \"Options\", \"IsRequired\", \"SortOrder\", \"IsArchived\", \"CreatedAt\") VALUES ({s.OrganizationA}, {Guid.NewGuid()}, 'forged', 'Forged', 'WorkItem', 'Text', '[]', false, 0, false, now())"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task Rls_confines_custom_field_values_to_their_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        Guid definitionId;
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            var definition = new CustomFieldDefinition(s.OrganizationA, Guid.NewGuid(), "warranty_note",
                "Warranty note", CustomFieldAppliesTo.WorkItem, CustomFieldType.Text, null, false, 0, DateTimeOffset.UtcNow);
            definitionId = definition.Id;
            admin.CustomFieldDefinitions.Add(definition);
            admin.CustomFieldValues.Add(CustomFieldValue.Create(s.OrganizationA, Guid.NewGuid(), s.WorkA, definitionId,
                "3 years remaining", DateTimeOffset.UtcNow));
            await admin.SaveChangesAsync();
        }

        await using var store = s.Store(s.OrganizationB);
        Assert.Empty(await store.CustomFieldValues.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await store.CustomFieldValues.FromSqlRaw("SELECT * FROM operations.\"CustomFieldValues\"").IgnoreQueryFilters().ToListAsync());
        var exception = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO operations.\"CustomFieldValues\" (\"OrganizationId\", \"Id\", \"WorkId\", \"CustomFieldDefinitionId\", \"Value\", \"UpdatedAt\") VALUES ({s.OrganizationA}, {Guid.NewGuid()}, {s.WorkA}, {definitionId}, 'forged', now())"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    // FS-S13S15SecurityFix (2026-09-11): AssetLifecycleCosts, ComplianceObligations, Incidents,
    // MeterReadings, PreventiveMaintenancePlans, Remediations, Violations and IncidentAudit
    // shipped with a GRANT but no RLS at all — the restricted role could read and write every
    // organization's compliance/incident/preventive-maintenance data with zero DB-level tenant
    // confinement. Fixed; this is the regression test for the fix, on one representative table.
    [Fact]
    public async Task Rls_confines_the_incidents_table_to_its_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            await admin.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO operations.\"Incidents\" (\"OrganizationId\", \"Id\", \"PropertyId\", \"Title\", \"OccurredAt\", \"Status\") VALUES ({s.OrganizationA}, {Guid.NewGuid()}, {s.PropertyA}, 'Leak in unit 4', now(), 'Open')");
        }

        await using var store = s.Store(s.OrganizationB);
        Assert.Empty(await store.Database.SqlQueryRaw<Guid>("SELECT \"Id\" FROM operations.\"Incidents\"").ToListAsync());
        var exception = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO operations.\"Incidents\" (\"OrganizationId\", \"Id\", \"PropertyId\", \"Title\", \"OccurredAt\", \"Status\") VALUES ({s.OrganizationA}, {Guid.NewGuid()}, {s.PropertyA}, 'forged', now(), 'Open')"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    // Coverage sweep after the FS-S13S15SecurityFix incident (above): RLS itself is now verified
    // present on all 67 operations tables (a live pg_class + pg_policies + grant check, not a
    // static read of the migrations), but most of FS-S08/S09/S10/S13/S15's tables still had no
    // raw-SQL isolation test — only the aggregate readiness count, which is how the RLS gap hid
    // in the first place. These three are one representative table per previously-unaudited
    // area (compliance, preventive maintenance, owner budgeting); not exhaustive — see
    // docs/followups.md for the remaining tables.
    [Fact]
    public async Task Rls_confines_the_violations_table_to_its_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            await admin.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO operations.\"Violations\" (\"OrganizationId\", \"Id\", \"PropertyId\", \"Title\", \"IdentifiedOn\", \"Severity\", \"Status\") VALUES ({s.OrganizationA}, {Guid.NewGuid()}, {s.PropertyA}, 'Missing fire extinguisher', '2026-01-01', 1, 'Open')");
        }

        await using var store = s.Store(s.OrganizationB);
        Assert.Empty(await store.Database.SqlQueryRaw<Guid>("SELECT \"Id\" FROM operations.\"Violations\"").ToListAsync());
        var exception = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO operations.\"Violations\" (\"OrganizationId\", \"Id\", \"PropertyId\", \"Title\", \"IdentifiedOn\", \"Severity\", \"Status\") VALUES ({s.OrganizationA}, {Guid.NewGuid()}, {s.PropertyA}, 'forged', '2026-01-01', 1, 'Open')"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task Rls_confines_the_preventive_maintenance_plans_table_to_its_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            await admin.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO operations.\"PreventiveMaintenancePlans\" (\"OrganizationId\", \"Id\", \"AssetId\", \"Name\", \"Recurrence\", \"FirstDueOn\", \"IsActive\") VALUES ({s.OrganizationA}, {Guid.NewGuid()}, {s.AssetA}, 'Quarterly HVAC filter', 'Quarterly', '2026-01-01', true)");
        }

        await using var store = s.Store(s.OrganizationB);
        Assert.Empty(await store.Database.SqlQueryRaw<Guid>("SELECT \"Id\" FROM operations.\"PreventiveMaintenancePlans\"").ToListAsync());
        var exception = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO operations.\"PreventiveMaintenancePlans\" (\"OrganizationId\", \"Id\", \"AssetId\", \"Name\", \"Recurrence\", \"FirstDueOn\", \"IsActive\") VALUES ({s.OrganizationA}, {Guid.NewGuid()}, {s.AssetA}, 'forged', 'Quarterly', '2026-01-01', true)"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task Rls_confines_the_budgets_table_to_its_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            await admin.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO operations.\"Budgets\" (\"OrganizationId\", \"Id\", \"PropertyId\", \"Year\", \"Name\", \"Status\", \"CreatedAt\") VALUES ({s.OrganizationA}, {Guid.NewGuid()}, {s.PropertyA}, 2026, '2026 plan', 'Draft', now())");
        }

        await using var store = s.Store(s.OrganizationB);
        Assert.Empty(await store.Database.SqlQueryRaw<Guid>("SELECT \"Id\" FROM operations.\"Budgets\"").ToListAsync());
        var exception = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO operations.\"Budgets\" (\"OrganizationId\", \"Id\", \"PropertyId\", \"Year\", \"Name\", \"Status\", \"CreatedAt\") VALUES ({s.OrganizationA}, {Guid.NewGuid()}, {s.PropertyA}, 2026, 'forged', 'Draft', now())"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_guard_rejects_cross_tenant_entities(bool synchronous)
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var store = s.Store(s.OrganizationA);
        store.WorkItems.Add(new WorkItem(s.OrganizationB, Guid.NewGuid(), "forged"));
        if (synchronous) Assert.Throws<TenantAccessException>(() => store.SaveChanges());
        else await Assert.ThrowsAsync<TenantAccessException>(() => store.SaveChangesAsync());
    }

    [Fact]
    public async Task Detached_foreign_entity_cannot_be_modified_or_deleted()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var store = s.Store(s.OrganizationA);
        var foreign = new WorkItem(s.OrganizationB, s.WorkB, "forged");
        store.Update(foreign);
        await Assert.ThrowsAsync<TenantAccessException>(() => store.SaveChangesAsync());
        store.ChangeTracker.Clear();
        store.Remove(foreign);
        await Assert.ThrowsAsync<TenantAccessException>(() => store.SaveChangesAsync());
    }

    [Fact]
    public async Task Composite_foreign_key_rejects_other_organizations_vendor()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var store = s.Store(s.OrganizationA);
        var work = await store.WorkItems.SingleAsync();
        work.AssignVendor(s.VendorB, s.AdminA, DateTimeOffset.UtcNow);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)exception.InnerException!).SqlState);
    }

    [Fact]
    public async Task Missing_database_tenant_context_sees_no_business_rows()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var connection = new NpgsqlConnection(fixture.RuntimeConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM operations.\"WorkItems\"";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Pooled_connections_do_not_leak_previous_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        foreach (var organization in new[] { s.OrganizationA, s.OrganizationB, s.OrganizationA, s.OrganizationB })
        {
            await using var store = s.Store(organization);
            Assert.Equal(organization, Assert.Single(await store.WorkItems.IgnoreQueryFilters().ToListAsync()).OrganizationId);
        }
    }

    [Fact]
    public async Task Audit_failure_rolls_back_work_update()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using (var store = s.Store(s.OrganizationA))
        await using (var comms = s.Comms(s.OrganizationA))
        {
            var operations = new EfWorkOperations(store, comms, TimeProvider.System, new NoAutomation(), NullLogger<EfWorkOperations>.Instance);
            await Assert.ThrowsAsync<DbUpdateException>(() => operations.AssignVendorAsync(s.WorkA, s.VendorA, s.AdminB, default));
        }
        await using var verify = s.Store(s.OrganizationA);
        Assert.Null((await verify.WorkItems.SingleAsync()).VendorId);
        Assert.Empty(await verify.Timeline.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_assignment_cannot_silently_overwrite_history()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var secondVendor = Guid.NewGuid();
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            admin.Vendors.Add(new Vendor(s.OrganizationA, secondVendor, "Second vendor"));
            await admin.SaveChangesAsync();
        }
        await using var first = s.Store(s.OrganizationA);
        await using var stale = s.Store(s.OrganizationA);
        var a = await first.WorkItems.SingleAsync();
        var b = await stale.WorkItems.SingleAsync();
        first.Timeline.Add(TimelineEntry.From(a.AssignVendor(s.VendorA, s.AdminA, DateTimeOffset.UtcNow)!));
        stale.Timeline.Add(TimelineEntry.From(b.AssignVendor(secondVendor, s.AdminA, DateTimeOffset.UtcNow)!));
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var verify = s.Store(s.OrganizationA);
        Assert.Equal(s.VendorA, (await verify.WorkItems.SingleAsync()).VendorId);
        Assert.Single(await verify.Timeline.ToListAsync());
    }

    [Fact]
    public async Task Audit_cannot_be_deleted_through_ef_or_raw_sql()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, (await s.AssignAsync(s.WorkA, s.VendorA)).StatusCode);
        await using var store = s.Store(s.OrganizationA);
        store.Timeline.Remove(await store.Timeline.SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveChangesAsync());
        await using var admin = s.AdminStore(s.OrganizationA);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM operations.\"Timeline\" WHERE \"OrganizationId\" = {s.OrganizationA}"));
        Assert.Equal("55000", exception.SqlState);
    }

    [Fact]
    public async Task Migrations_are_repeatable_and_runtime_role_has_no_bypass()
    {
        await DatabaseProvisioner.MigrateAsync(fixture.AdminConnection);
        await using var admin = new NpgsqlConnection(fixture.AdminConnection);
        await admin.OpenAsync();
        Assert.False(await DatabaseSafety.HasSafeRuntimeRoleAsync(admin, default));
        await using var runtime = new NpgsqlConnection(fixture.RuntimeConnection);
        await runtime.OpenAsync();
        Assert.True(await DatabaseSafety.HasSafeRuntimeRoleAsync(runtime, default));
    }

    private sealed class NoAutomation : IAutomationEngine
    {
        public Task<AutomationRunSummary> RunAsync(AutomationTrigger trigger, Guid workId, Guid occurrenceId, CancellationToken cancellationToken) =>
            Task.FromResult(AutomationRunSummary.Empty);
    }
}
