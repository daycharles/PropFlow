using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Domain.Communications;
using PropFlow.Domain.Marketing;
using Xunit;

namespace PropFlow.IntegrationTests;

// The database boundary for the six FS-S05 application tables, proved with raw SQL rather than
// through EF. An EF-only isolation test proves nothing here: the tenant query filter would pass
// even with no policy on the table at all. Modelled on AccountingIsolationTests.cs.
[Collection("PostgreSQL")]
public sealed class ApplicationPersistenceTests(DatabaseFixture fixture)
{
    private static readonly string[] ApplicationTables =
    [
        "RentalApplications", "ApplicationApplicants", "ScreeningRequests",
        "ApplicationConsents", "ScreeningResults", "ApplicationDecisions"
    ];

    private static readonly string[] AppendOnlyTables =
        ["ApplicationConsents", "ScreeningResults", "ApplicationDecisions"];

    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private sealed record Seeded(Guid ApplicationId, Guid ApplicantId, Guid RequestId, Guid ConsentId, Guid ResultId, Guid DecisionId, string IdempotencyKey);

    private static async Task<Seeded> SeedAsync(Scenario s, Guid organization, Guid actor, Guid propertyId, string suffix)
    {
        await using var store = s.AdminStore(organization);
        var listing = new Listing(organization, Guid.NewGuid(), propertyId, null, $"2BR unit {suffix}", null, null, 1650m);
        var inquiry = new Inquiry(organization, Guid.NewGuid(), listing.Id, "Dana Reyes", $"dana-{suffix}@example.test", null, null, null);
        var applicant = new Applicant(organization, Guid.NewGuid(), listing.Id, inquiry.Id, "Dana Reyes", $"dana-{suffix}@example.test");
        store.Listings.Add(listing);
        store.Inquiries.Add(inquiry);
        store.Applicants.Add(applicant);
        await store.SaveChangesAsync();

        var application = new RentalApplication(organization, Guid.NewGuid(), listing.Id);
        application.AddApplicant(Guid.NewGuid(), applicant.Id, ApplicantRole.Primary, 5200m, "Employed full time");
        store.RentalApplications.Add(application);
        await store.SaveChangesAsync();

        var consent = new ApplicationConsent(organization, Guid.NewGuid(), application.Id, applicant.Id,
            ApplicationConsentType.BackgroundCheck, ConsentDecision.Granted, Now, actor, "203.0.113.7");
        var key = $"idem-{suffix}-{application.Id:N}";
        var request = new ScreeningRequest(organization, Guid.NewGuid(), application.Id, applicant.Id, key);
        request.BeginAttempt(Now);
        request.Complete(Now);
        store.ApplicationConsents.Add(consent);
        store.ScreeningRequests.Add(request);
        await store.SaveChangesAsync();

        var result = new ScreeningResult(organization, Guid.NewGuid(), request.Id, applicant.Id,
            ScreeningRecommendation.Pass, 742, "No adverse findings.", Now);
        store.ScreeningResults.Add(result);
        await store.SaveChangesAsync();

        application.Submit(actor, Now);
        application.MarkConsented(actor);
        application.BeginScreening(actor);
        application.CompleteScreening(actor);
        var decision = application.Approve(actor, Now, "MEETS_CRITERIA", null, [ScreeningRecommendation.Pass]);
        store.ApplicationDecisions.Add(decision);
        await store.SaveChangesAsync();

        return new Seeded(application.Id, applicant.Id, request.Id, consent.Id, result.Id, decision.Id, key);
    }

    [Fact]
    public async Task Every_application_table_has_forced_row_level_security()
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
        command.Parameters.AddWithValue("tables", ApplicationTables);
        Assert.Equal((long)ApplicationTables.Length, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Every_application_table_carries_the_tenant_isolation_policy_with_using_and_with_check()
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
        command.Parameters.AddWithValue("tables", ApplicationTables);
        Assert.Equal((long)ApplicationTables.Length, await command.ExecuteScalarAsync());
    }

    // The readiness floor in DatabaseHealth.cs is a count of what is actually there, not the
    // previous literal plus six. This is the check that keeps it honest.
    [Fact]
    public async Task The_operations_schema_meets_the_readiness_floor_with_every_table_forced()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var connection = new NpgsqlConnection(fixture.AdminConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*), count(*) FILTER (WHERE NOT (c.relrowsecurity AND c.relforcerowsecurity))
            FROM pg_class c JOIN pg_namespace n ON c.relnamespace = n.oid
            WHERE n.nspname = 'operations' AND c.relkind = 'r' AND c.relname <> '__OperationsMigrations'
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetInt64(0) >= 77, $"operations holds {reader.GetInt64(0)} tables; the readiness floor is 77.");
        Assert.Equal(0L, reader.GetInt64(1));
    }

    [Fact]
    public async Task Rls_blocks_filter_bypass_and_raw_reads_of_another_tenants_application()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var a = await SeedAsync(s, s.OrganizationA, s.AdminA, s.PropertyA, "a");
        await SeedAsync(s, s.OrganizationB, s.AdminB, s.PropertyB, "b");

        await using var store = s.Store(s.OrganizationA);
        Assert.Equal(a.ApplicationId, Assert.Single(await store.RentalApplications.IgnoreQueryFilters().ToListAsync()).Id);
        Assert.Equal(a.ApplicationId, Assert.Single(await store.RentalApplications
            .FromSqlRaw("SELECT *, xmin FROM operations.\"RentalApplications\"").IgnoreQueryFilters().ToListAsync()).Id);
        Assert.Equal(a.ConsentId, Assert.Single(await store.ApplicationConsents
            .FromSqlRaw("SELECT * FROM operations.\"ApplicationConsents\"").IgnoreQueryFilters().ToListAsync()).Id);
        Assert.Equal(a.ResultId, Assert.Single(await store.ScreeningResults.IgnoreQueryFilters().ToListAsync()).Id);
        Assert.Equal(a.DecisionId, Assert.Single(await store.ApplicationDecisions.IgnoreQueryFilters().ToListAsync()).Id);
        Assert.Equal(a.RequestId, Assert.Single(await store.ScreeningRequests.IgnoreQueryFilters().ToListAsync()).Id);
        Assert.Equal(a.ApplicantId, Assert.Single(await store.ApplicationApplicants.IgnoreQueryFilters().ToListAsync()).ApplicantId);
    }

    [Fact]
    public async Task A_runtime_connection_without_a_tenant_context_sees_no_application_rows()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedAsync(s, s.OrganizationA, s.AdminA, s.PropertyA, "a");

        await using var connection = new NpgsqlConnection(fixture.RuntimeConnection);
        await connection.OpenAsync();
        foreach (var table in ApplicationTables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""SELECT count(*) FROM operations."{table}" """;
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
    }

    // Rung two of three. No UPDATE/DELETE grant on the append-only tables
    // (DatabaseProvisioner.ConfigureRuntimeAsync), so the statement is refused at planning time,
    // before any row is considered.
    [Fact]
    public async Task The_runtime_role_cannot_update_or_delete_consent_results_or_decisions()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var a = await SeedAsync(s, s.OrganizationA, s.AdminA, s.PropertyA, "a");

        await using var store = s.Store(s.OrganizationA);
        foreach (var statement in new[]
        {
            $"""UPDATE operations."ApplicationConsents" SET "Decision" = 'Revoked' WHERE "Id" = '{a.ConsentId}'""",
            $"""DELETE FROM operations."ApplicationConsents" WHERE "Id" = '{a.ConsentId}'""",
            $"""UPDATE operations."ScreeningResults" SET "Recommendation" = 'Pass' WHERE "Id" = '{a.ResultId}'""",
            $"""DELETE FROM operations."ScreeningResults" WHERE "Id" = '{a.ResultId}'""",
            $"""UPDATE operations."ApplicationDecisions" SET "Outcome" = 'Denied' WHERE "Id" = '{a.DecisionId}'""",
            $"""DELETE FROM operations."ApplicationDecisions" WHERE "Id" = '{a.DecisionId}'"""
        })
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlRawAsync(statement));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        }

        Assert.Equal(ApplicationDecisionOutcome.Approved, (await store.ApplicationDecisions.SingleAsync()).Outcome);
    }

    // Rung three. The owner holds every privilege, so this is the trigger and nothing else — the
    // control a careless verb on a shared GRANT line cannot undo.
    [Fact]
    public async Task The_append_only_trigger_refuses_a_mutation_even_for_the_table_owner()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var a = await SeedAsync(s, s.OrganizationA, s.AdminA, s.PropertyA, "a");

        await using var connection = new NpgsqlConnection(fixture.AdminConnection);
        await connection.OpenAsync();
        foreach (var statement in new[]
        {
            $"""UPDATE operations."ApplicationConsents" SET "Decision" = 'Revoked' WHERE "Id" = '{a.ConsentId}'""",
            $"""DELETE FROM operations."ApplicationConsents" WHERE "Id" = '{a.ConsentId}'""",
            $"""UPDATE operations."ScreeningResults" SET "Score" = 800 WHERE "Id" = '{a.ResultId}'""",
            $"""DELETE FROM operations."ScreeningResults" WHERE "Id" = '{a.ResultId}'""",
            $"""UPDATE operations."ApplicationDecisions" SET "Reason" = 'tampered' WHERE "Id" = '{a.DecisionId}'""",
            $"""DELETE FROM operations."ApplicationDecisions" WHERE "Id" = '{a.DecisionId}'"""
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal("55000", exception.SqlState);
            Assert.Contains("append-only", exception.MessageText);
        }
    }

    // Rung one. A violation is a test failure here, rather than a production 42501 nobody sees
    // until a decision cannot be written.
    [Fact]
    public async Task Ef_refuses_to_modify_or_delete_a_consent_a_result_or_a_decision()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedAsync(s, s.OrganizationA, s.AdminA, s.PropertyA, "a");

        await using var store = s.Store(s.OrganizationA);
        var consent = await store.ApplicationConsents.FirstAsync();
        store.Remove(consent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveChangesAsync());
        store.ChangeTracker.Clear();

        var result = await store.ScreeningResults.FirstAsync();
        store.Entry(result).Property(x => x.Score).CurrentValue = 800;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveChangesAsync());
        store.ChangeTracker.Clear();

        var decision = await store.ApplicationDecisions.FirstAsync();
        store.Entry(decision).Property(x => x.Reason).CurrentValue = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveChangesAsync());
    }

    // The other three tables are working state, not evidence. This is the check that the
    // append-only treatment was applied to exactly the three tables it belongs on.
    [Fact]
    public async Task The_runtime_role_can_still_progress_an_application_and_its_screening_request()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var a = await SeedAsync(s, s.OrganizationA, s.AdminA, s.PropertyA, "a");

        await using var store = s.Store(s.OrganizationA);
        var applicantRow = await store.ApplicationApplicants.SingleAsync();
        applicantRow.SetFinancials(6100m, "Employed full time");
        await store.SaveChangesAsync();
        Assert.Equal(6100m, (await store.ApplicationApplicants.SingleAsync()).MonthlyIncome);

        // UPDATE is granted on the application and its screening request — status transitions
        // and retry counters are working state, not evidence.
        Assert.Equal(1, await store.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE operations."RentalApplications" SET "Status" = 'Withdrawn' WHERE "Id" = {a.ApplicationId}"""));
        Assert.Equal(1, await store.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE operations."ScreeningRequests" SET "Attempts" = 2 WHERE "Id" = {a.RequestId}"""));
        // And DELETE is granted on ApplicationApplicants, which RemoveApplicant needs.
        Assert.Equal(1, await store.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM operations."ApplicationApplicants" WHERE "ApplicationId" = {a.ApplicationId}"""));
    }

    [Fact]
    public async Task A_replayed_idempotency_key_cannot_become_a_second_screening_request()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var a = await SeedAsync(s, s.OrganizationA, s.AdminA, s.PropertyA, "a");

        await using var store = s.Store(s.OrganizationA);
        store.ScreeningRequests.Add(new ScreeningRequest(s.OrganizationA, Guid.NewGuid(), a.ApplicationId, a.ApplicantId, a.IdempotencyKey));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)exception.InnerException!).SqlState);
    }

    // The same key in another organization is a different request — the index is composite.
    [Fact]
    public async Task The_idempotency_key_is_unique_per_organization_not_globally()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var a = await SeedAsync(s, s.OrganizationA, s.AdminA, s.PropertyA, "a");
        var b = await SeedAsync(s, s.OrganizationB, s.AdminB, s.PropertyB, "b");

        await using var store = s.Store(s.OrganizationB);
        store.ScreeningRequests.Add(new ScreeningRequest(s.OrganizationB, Guid.NewGuid(), b.ApplicationId, b.ApplicantId, a.IdempotencyKey));
        await store.SaveChangesAsync();
        Assert.Equal(2, await store.ScreeningRequests.CountAsync());
    }

    // A concurrent approve and deny must collide rather than last-write-wins. The xmin shadow
    // row version is what turns the loser into a 409 instead of a silent overwrite.
    [Fact]
    public async Task A_concurrent_decision_on_the_same_application_raises_a_concurrency_exception()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var a = await SeedAsync(s, s.OrganizationA, s.AdminA, s.PropertyA, "a");

        await using var first = s.Store(s.OrganizationA);
        await using var second = s.Store(s.OrganizationA);
        var one = await first.RentalApplications.SingleAsync(x => x.Id == a.ApplicationId);
        var two = await second.RentalApplications.SingleAsync(x => x.Id == a.ApplicationId);

        // Both readers hold the same xmin. Any update through the second context must now fail.
        first.Entry(one).Property(x => x.Status).CurrentValue = ApplicationStatus.Denied;
        await first.SaveChangesAsync();

        second.Entry(two).Property(x => x.Status).CurrentValue = ApplicationStatus.Withdrawn;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task The_composite_foreign_key_refuses_another_organizations_applicant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var a = await SeedAsync(s, s.OrganizationA, s.AdminA, s.PropertyA, "a");
        var b = await SeedAsync(s, s.OrganizationB, s.AdminB, s.PropertyB, "b");

        await using var store = s.Store(s.OrganizationA);
        // Tagged to organization A, so the tenant guard passes and the composite foreign key is
        // what stops the request reaching organization B's applicant.
        store.ScreeningRequests.Add(new ScreeningRequest(s.OrganizationA, Guid.NewGuid(), a.ApplicationId, b.ApplicantId, $"forged-{Guid.NewGuid():N}"));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)exception.InnerException!).SqlState);
    }

    [Fact]
    public async Task Rls_rejects_a_raw_cross_tenant_application_insert()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedAsync(s, s.OrganizationA, s.AdminA, s.PropertyA, "a");
        var b = await SeedAsync(s, s.OrganizationB, s.AdminB, s.PropertyB, "b");

        await using var store = s.Store(s.OrganizationA);
        var consent = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO operations."ApplicationConsents" ("OrganizationId", "Id", "ApplicationId", "ApplicantId", "ConsentType", "Decision", "RecordedAt", "RecordedBy", "Source") VALUES ({s.OrganizationB}, {Guid.NewGuid()}, {b.ApplicationId}, {b.ApplicantId}, 'BackgroundCheck', 'Granted', now(), {s.AdminB}, 'forged')"""));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, consent.SqlState);

        var decision = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO operations."ApplicationDecisions" ("OrganizationId", "Id", "ApplicationId", "Outcome", "Reason", "DecidedBy", "DecidedAt") VALUES ({s.OrganizationB}, {Guid.NewGuid()}, {b.ApplicationId}, 'Approved', 'forged', {s.AdminB}, now())"""));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, decision.SqlState);
    }
}
