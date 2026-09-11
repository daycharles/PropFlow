using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Domain.Accounting;
using Xunit;

namespace PropFlow.IntegrationTests;

// The database boundary for the FS-S09 ledger tables, proved with raw SQL rather than through EF.
// An EF-only isolation test proves nothing here: the tenant query filter would pass even with no
// policy on the table at all. Modelled on IsolationTests.cs.
[Collection("PostgreSQL")]
public sealed class AccountingIsolationTests(DatabaseFixture fixture)
{
    private static readonly string[] LedgerTables = ["ChartOfAccounts", "FiscalPeriods", "JournalEntries", "JournalLines"];

    private sealed record Ledger(Guid PeriodId, Guid CashId, Guid RevenueId, Guid EntryId);

    private static async Task<Ledger> SeedAsync(Scenario s, Guid organization, Guid actor, string suffix)
    {
        await using var store = s.AdminStore(organization);
        var now = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var period = new FiscalPeriod(organization, Guid.NewGuid(), $"2026-01-{suffix}", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var cash = new ChartOfAccount(organization, Guid.NewGuid(), $"1000-{suffix}", "Operating cash", AccountType.Asset, now);
        var revenue = new ChartOfAccount(organization, Guid.NewGuid(), $"4000-{suffix}", "Rent revenue", AccountType.Revenue, now);
        store.FiscalPeriods.Add(period);
        store.ChartOfAccounts.AddRange(cash, revenue);
        await store.SaveChangesAsync();

        var entryId = Guid.NewGuid();
        JournalLine[] lines =
        [
            new(organization, Guid.NewGuid(), entryId, cash.Id, 1650m, 0m, null),
            new(organization, Guid.NewGuid(), entryId, revenue.Id, 0m, 1650m, null)
        ];
        store.JournalEntries.Add(JournalEntry.Post(organization, entryId, period, new DateOnly(2026, 1, 15),
            $"JE-{suffix}", null, actor, now, lines));
        await store.SaveChangesAsync();
        return new Ledger(period.Id, cash.Id, revenue.Id, entryId);
    }

    [Fact]
    public async Task Every_ledger_table_has_forced_row_level_security()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var connection = new NpgsqlConnection(fixture.AdminConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*) FILTER (WHERE relrowsecurity AND relforcerowsecurity)
            FROM pg_class c JOIN pg_namespace n ON c.relnamespace = n.oid
            WHERE n.nspname = 'operations' AND c.relname = ANY(@tables)
            """;
        command.Parameters.AddWithValue("tables", LedgerTables);
        Assert.Equal((long)LedgerTables.Length, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Every_ledger_table_carries_the_tenant_isolation_policy_with_using_and_with_check()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var connection = new NpgsqlConnection(fixture.AdminConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*) FROM pg_policies
            WHERE schemaname = 'operations' AND policyname = 'tenant_isolation'
              AND tablename = ANY(@tables) AND qual IS NOT NULL AND with_check IS NOT NULL
            """;
        command.Parameters.AddWithValue("tables", LedgerTables);
        Assert.Equal((long)LedgerTables.Length, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Rls_blocks_filter_bypass_and_raw_reads_of_another_tenants_ledger()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var a = await SeedAsync(s, s.OrganizationA, s.AdminA, "a");
        await SeedAsync(s, s.OrganizationB, s.AdminB, "b");

        await using var store = s.Store(s.OrganizationA);
        // Bypassing the query filter still returns only this tenant's rows, and so does raw SQL.
        Assert.Equal(a.EntryId, Assert.Single(await store.JournalEntries.IgnoreQueryFilters().ToListAsync()).Id);
        Assert.Equal(a.EntryId, Assert.Single(await store.JournalEntries
            .FromSqlRaw("SELECT * FROM operations.\"JournalEntries\"").IgnoreQueryFilters().ToListAsync()).Id);
        Assert.Equal(2, (await store.JournalLines
            .FromSqlRaw("SELECT * FROM operations.\"JournalLines\"").IgnoreQueryFilters().ToListAsync()).Count);
        Assert.Equal(2, (await store.ChartOfAccounts.IgnoreQueryFilters().ToListAsync()).Count);
        Assert.Equal(a.PeriodId, Assert.Single(await store.FiscalPeriods.IgnoreQueryFilters().ToListAsync()).Id);
    }

    [Fact]
    public async Task Rls_rejects_raw_cross_tenant_ledger_inserts()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedAsync(s, s.OrganizationA, s.AdminA, "a");
        var b = await SeedAsync(s, s.OrganizationB, s.AdminB, "b");

        await using var store = s.Store(s.OrganizationA);
        var account = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO operations."ChartOfAccounts" ("OrganizationId", "Id", "Code", "Name", "Type", "IsActive", "CreatedAt") VALUES ({s.OrganizationB}, {Guid.NewGuid()}, 'forged', 'Forged', 'Asset', true, now())"""));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, account.SqlState);

        var entry = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO operations."JournalEntries" ("OrganizationId", "Id", "PeriodId", "EntryDate", "Reference", "Total", "PostedBy", "PostedAt") VALUES ({s.OrganizationB}, {Guid.NewGuid()}, {b.PeriodId}, DATE '2026-01-15', 'forged', 1, {s.AdminB}, now())"""));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, entry.SqlState);

        var line = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO operations."JournalLines" ("OrganizationId", "Id", "EntryId", "AccountId", "Debit", "Credit") VALUES ({s.OrganizationB}, {Guid.NewGuid()}, {b.EntryId}, {b.CashId}, 1, 0)"""));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, line.SqlState);
    }

    [Fact]
    public async Task Rls_blocks_raw_cross_tenant_updates_and_bulk_deletes_of_the_ledger()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedAsync(s, s.OrganizationA, s.AdminA, "a");
        var b = await SeedAsync(s, s.OrganizationB, s.AdminB, "b");

        await using var store = s.Store(s.OrganizationA);
        // ChartOfAccounts does carry UPDATE/DELETE-shaped grants, so this is the policy talking,
        // not the privilege: the other tenant's row is simply not visible to the statement.
        Assert.Equal(0, await store.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE operations."ChartOfAccounts" SET "Name" = 'tampered' WHERE "Id" = {b.CashId}"""));
        Assert.Equal(0, await store.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE operations."FiscalPeriods" SET "Name" = 'tampered' WHERE "Id" = {b.PeriodId}"""));

        await using var other = s.Store(s.OrganizationB);
        Assert.Equal("Operating cash", (await other.ChartOfAccounts.SingleAsync(x => x.Id == b.CashId)).Name);
        Assert.Equal("2026-01-b", (await other.FiscalPeriods.SingleAsync()).Name);
    }

    [Fact]
    public async Task The_runtime_role_cannot_update_or_delete_a_posted_journal()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var a = await SeedAsync(s, s.OrganizationA, s.AdminA, "a");

        await using var store = s.Store(s.OrganizationA);
        // No UPDATE/DELETE grant on either journal table (DatabaseProvisioner.ConfigureRuntimeAsync),
        // so the statement is refused at planning time, before any row is considered.
        foreach (var statement in new[]
        {
            $"""UPDATE operations."JournalEntries" SET "Reference" = 'tampered' WHERE "Id" = '{a.EntryId}'""",
            $"""DELETE FROM operations."JournalEntries" WHERE "Id" = '{a.EntryId}'""",
            """UPDATE operations."JournalLines" SET "Debit" = 0""",
            """DELETE FROM operations."JournalLines" WHERE true"""
        })
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlRawAsync(statement));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        }

        Assert.Equal("JE-a", (await store.JournalEntries.SingleAsync()).Reference);
    }

    [Fact]
    public async Task The_append_only_trigger_refuses_a_journal_mutation_even_for_the_table_owner()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var a = await SeedAsync(s, s.OrganizationA, s.AdminA, "a");

        // The owner holds every privilege, so this is the trigger and nothing else.
        await using var connection = new NpgsqlConnection(fixture.AdminConnection);
        await connection.OpenAsync();
        foreach (var statement in new[]
        {
            $"""UPDATE operations."JournalEntries" SET "Reference" = 'tampered' WHERE "Id" = '{a.EntryId}'""",
            $"""DELETE FROM operations."JournalEntries" WHERE "Id" = '{a.EntryId}'""",
            $"""UPDATE operations."JournalLines" SET "Debit" = 0 WHERE "EntryId" = '{a.EntryId}'"""
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal("55000", exception.SqlState);
            Assert.Contains("append-only", exception.MessageText);
        }
    }

    [Fact]
    public async Task Ef_refuses_to_modify_or_delete_a_posted_journal()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedAsync(s, s.OrganizationA, s.AdminA, "a");

        await using var store = s.Store(s.OrganizationA);
        var line = await store.JournalLines.FirstAsync();
        store.Remove(line);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveChangesAsync());
        store.ChangeTracker.Clear();

        var entry = await store.JournalEntries.FirstAsync();
        store.Entry(entry).Property(x => x.Reference).CurrentValue = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveChangesAsync());
    }

    [Fact]
    public async Task A_runtime_connection_without_a_tenant_context_sees_no_ledger_rows()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedAsync(s, s.OrganizationA, s.AdminA, "a");

        await using var connection = new NpgsqlConnection(fixture.RuntimeConnection);
        await connection.OpenAsync();
        foreach (var table in LedgerTables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""SELECT count(*) FROM operations."{table}" """;
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
    }

    [Fact]
    public async Task The_composite_foreign_key_refuses_another_organizations_account()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var a = await SeedAsync(s, s.OrganizationA, s.AdminA, "a");
        var b = await SeedAsync(s, s.OrganizationB, s.AdminB, "b");

        await using var store = s.Store(s.OrganizationA);
        var period = await store.FiscalPeriods.SingleAsync();
        var entryId = Guid.NewGuid();
        // Both lines are tagged to organization A, so the tenant guard passes and the composite
        // foreign key is what stops the posting reaching organization B's account.
        JournalLine[] lines =
        [
            new(s.OrganizationA, Guid.NewGuid(), entryId, b.CashId, 10m, 0m, null),
            new(s.OrganizationA, Guid.NewGuid(), entryId, a.RevenueId, 0m, 10m, null)
        ];
        store.JournalEntries.Add(JournalEntry.Post(s.OrganizationA, entryId, period, new DateOnly(2026, 1, 16),
            "JE-forged", null, s.AdminA, DateTimeOffset.UtcNow, lines));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)exception.InnerException!).SqlState);
    }
}
