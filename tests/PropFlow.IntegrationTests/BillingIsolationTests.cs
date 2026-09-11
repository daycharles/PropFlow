using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Domain.Billing;
using PropFlow.Domain.Leasing;
using PropFlow.Infrastructure.Persistence;
using Xunit;

namespace PropFlow.IntegrationTests;

// .claude/rules/testing.md:51 — an isolation test that only goes through EF proves nothing,
// because the query filter would pass with no policy at all. Every assertion here either
// bypasses the filter or runs raw SQL against the restricted propflow_app role.
[Collection("PostgreSQL")]
public sealed class BillingIsolationTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed record Seeded(Guid LeaseId, Guid ChargeId, Guid PaymentId, Guid ScheduleId, Guid CreditId, Guid RuleId, Guid RefundId);

    private static async Task<Seeded> SeedBillingAsync(Scenario s, Guid organization, Guid resident, Guid space, Guid property)
    {
        var seeded = new Seeded(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var store = s.AdminStore(organization);
        var lease = new Lease(organization, seeded.LeaseId, resident, space, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1000m, null);
        store.Leases.Add(lease);
        var charge = new LeaseCharge(organization, seeded.ChargeId, seeded.LeaseId, LeaseChargeType.OneTime, "Rent", 1000m, new DateOnly(2026, 2, 1));
        charge.Apply(400m);
        store.LeaseCharges.Add(charge);
        var payment = new ResidentPayment(organization, seeded.PaymentId, seeded.LeaseId, resident, 400m, new DateOnly(2026, 2, 1), "check", seeded.ChargeId, $"ref-{organization:N}");
        payment.Settle(Now);
        store.ResidentPayments.Add(payment);
        store.RecurringCharges.Add(new RecurringCharge(organization, seeded.ScheduleId, seeded.LeaseId, "Rent", 1000m, 1, new DateOnly(2026, 1, 1), null, Now));
        store.Credits.Add(new Credit(organization, seeded.CreditId, seeded.LeaseId, 50m, "Goodwill", new DateOnly(2026, 2, 1), Now));
        store.LateFeeRules.Add(new LateFeeRule(organization, seeded.RuleId, property, "Standard", 5, 50m, 0m, null, Now));
        store.PaymentRefunds.Add(new PaymentRefund(organization, seeded.RefundId, seeded.PaymentId, 100m, $"rf-{organization:N}", "refund", Now));
        await store.SaveChangesAsync();
        return seeded;
    }

    [Fact]
    public async Task Rls_confines_every_billing_table_to_its_tenant_on_filter_bypassed_and_raw_reads()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedBillingAsync(s, s.OrganizationA, s.ResidentA, s.SpaceA, s.PropertyA);
        await SeedBillingAsync(s, s.OrganizationB, s.ResidentB, s.SpaceB, s.PropertyB);

        await using var store = s.Store(s.OrganizationA);
        // IgnoreQueryFilters removes the EF tenant filter, so anything still hidden is hidden
        // by the database policy and nothing else.
        Assert.Single(await store.RecurringCharges.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await store.Credits.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await store.LateFeeRules.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await store.PaymentRefunds.IgnoreQueryFilters().ToListAsync());

        Assert.Single(await store.RecurringCharges.FromSqlRaw("SELECT * FROM operations.\"RecurringCharges\"").IgnoreQueryFilters().ToListAsync());
        Assert.Single(await store.Credits.FromSqlRaw("SELECT * FROM operations.\"Credits\"").IgnoreQueryFilters().ToListAsync());
        Assert.Single(await store.LateFeeRules.FromSqlRaw("SELECT * FROM operations.\"LateFeeRules\"").IgnoreQueryFilters().ToListAsync());
        Assert.Single(await store.PaymentRefunds.FromSqlRaw("SELECT * FROM operations.\"PaymentRefunds\"").IgnoreQueryFilters().ToListAsync());

        foreach (var row in await store.RecurringCharges.IgnoreQueryFilters().ToListAsync()) Assert.Equal(s.OrganizationA, row.OrganizationId);
        foreach (var row in await store.PaymentRefunds.IgnoreQueryFilters().ToListAsync()) Assert.Equal(s.OrganizationA, row.OrganizationId);
    }

    [Fact]
    public async Task Rls_rejects_raw_cross_tenant_inserts_into_every_billing_table()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var b = await SeedBillingAsync(s, s.OrganizationB, s.ResidentB, s.SpaceB, s.PropertyB);

        await using var store = s.Store(s.OrganizationA);
        await AssertInsertRefused(store, $"""
            INSERT INTO operations."RecurringCharges" ("OrganizationId", "Id", "LeaseId", "Description", "Amount", "DayOfMonth", "StartsOn", "Status", "CreatedAt")
            VALUES ('{s.OrganizationB}', '{Guid.NewGuid()}', '{b.LeaseId}', 'forged', 1, 1, DATE '2026-01-01', 'Active', now())
            """);
        await AssertInsertRefused(store, $"""
            INSERT INTO operations."Credits" ("OrganizationId", "Id", "LeaseId", "Amount", "AppliedAmount", "Reason", "IssuedOn", "Status", "CreatedAt")
            VALUES ('{s.OrganizationB}', '{Guid.NewGuid()}', '{b.LeaseId}', 1, 0, 'forged', DATE '2026-01-01', 'Open', now())
            """);
        await AssertInsertRefused(store, $"""
            INSERT INTO operations."LateFeeRules" ("OrganizationId", "Id", "Name", "GraceDays", "FlatAmount", "PercentOfOutstanding", "IsEnabled", "CreatedAt")
            VALUES ('{s.OrganizationB}', '{Guid.NewGuid()}', 'forged', 0, 1, 0, true, now())
            """);
        await AssertInsertRefused(store, $"""
            INSERT INTO operations."PaymentRefunds" ("OrganizationId", "Id", "PaymentId", "Amount", "ProviderReference", "IssuedAt")
            VALUES ('{s.OrganizationB}', '{Guid.NewGuid()}', '{b.PaymentId}', 1, 'forged', now())
            """);
    }

    [Fact]
    public async Task Rls_blocks_raw_cross_tenant_updates_and_deletes_of_billing_rows()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var b = await SeedBillingAsync(s, s.OrganizationB, s.ResidentB, s.SpaceB, s.PropertyB);

        await using var store = s.Store(s.OrganizationA);
        // A cross-tenant write matches no row at all, so it reports zero rather than throwing.
        Assert.Equal(0, await store.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE operations.\"Credits\" SET \"Reason\" = 'tampered' WHERE \"Id\" = {b.CreditId}"));
        Assert.Equal(0, await store.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE operations.\"RecurringCharges\" SET \"Amount\" = 1 WHERE \"Id\" = {b.ScheduleId}"));
        Assert.Equal(0, await store.Credits.IgnoreQueryFilters().Where(x => x.Id == b.CreditId).ExecuteDeleteAsync());

        await using var owner = s.Store(s.OrganizationB);
        Assert.Equal("Goodwill", (await owner.Credits.SingleAsync()).Reason);
        Assert.Equal(1000m, (await owner.RecurringCharges.SingleAsync()).Amount);
    }

    [Fact]
    public async Task The_runtime_role_cannot_alter_or_erase_the_refund_trail()
    {
        // PaymentRefunds is granted SELECT, INSERT only (DatabaseProvisioner.ConfigureRuntimeAsync),
        // matching operations."Timeline". A refund is a fact about money that left.
        await using var s = await fixture.CreateScenarioAsync();
        var a = await SeedBillingAsync(s, s.OrganizationA, s.ResidentA, s.SpaceA, s.PropertyA);

        await using var store = s.Store(s.OrganizationA);
        var update = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE operations.\"PaymentRefunds\" SET \"Amount\" = 1 WHERE \"Id\" = {a.RefundId}"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, update.SqlState);
        var delete = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM operations.\"PaymentRefunds\" WHERE \"Id\" = {a.RefundId}"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, delete.SqlState);
        Assert.Equal(100m, (await store.PaymentRefunds.SingleAsync()).Amount);
    }

    [Fact]
    public async Task The_provider_reference_is_unique_per_organization_but_not_across_them()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedBillingAsync(s, s.OrganizationA, s.ResidentA, s.SpaceA, s.PropertyA);

        await using var store = s.AdminStore(s.OrganizationA);
        var lease = await store.Leases.SingleAsync();
        var duplicate = new ResidentPayment(s.OrganizationA, Guid.NewGuid(), lease.Id, s.ResidentA, 10m,
            new DateOnly(2026, 2, 1), "check", null, $"ref-{s.OrganizationA:N}");
        store.ResidentPayments.Add(duplicate);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)exception.InnerException!).SqlState);

        // The index is scoped by organization, so the *same* reference string in another
        // tenant is a different payment and is allowed.
        store.ChangeTracker.Clear();
        var b = await SeedBillingAsync(s, s.OrganizationB, s.ResidentB, s.SpaceB, s.PropertyB);
        await using var other = s.AdminStore(s.OrganizationB);
        other.ResidentPayments.Add(new ResidentPayment(s.OrganizationB, Guid.NewGuid(), b.LeaseId, s.ResidentB, 10m,
            new DateOnly(2026, 2, 1), "check", null, $"ref-{s.OrganizationA:N}"));
        await other.SaveChangesAsync();
        Assert.Equal(2, await other.ResidentPayments.CountAsync());
    }

    [Fact]
    public async Task A_generated_charge_cannot_be_duplicated_for_the_same_schedule_and_period()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var a = await SeedBillingAsync(s, s.OrganizationA, s.ResidentA, s.SpaceA, s.PropertyA);

        await using var store = s.AdminStore(s.OrganizationA);
        var dueOn = new DateOnly(2026, 4, 1);
        store.LeaseCharges.Add(new LeaseCharge(s.OrganizationA, Guid.NewGuid(), a.LeaseId, LeaseChargeType.Recurring, "Rent", 1000m, dueOn, a.ScheduleId));
        await store.SaveChangesAsync();
        store.LeaseCharges.Add(new LeaseCharge(s.OrganizationA, Guid.NewGuid(), a.LeaseId, LeaseChargeType.Recurring, "Rent", 1000m, dueOn, a.ScheduleId));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)exception.InnerException!).SqlState);
    }

    private static async Task AssertInsertRefused(OperationsStore store, string sql)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }
}
