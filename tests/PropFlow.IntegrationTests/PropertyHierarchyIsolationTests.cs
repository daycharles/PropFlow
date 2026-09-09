using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Application;
using PropFlow.Domain.Properties;
using Xunit;

namespace PropFlow.IntegrationTests;

// The M3 property-hierarchy, employee and category tables must carry the same tenant isolation
// as the original operations tables (see 20260909184300_M3TenantSecurity).
[Collection("PostgreSQL")]
public sealed class PropertyHierarchyIsolationTests(DatabaseFixture fixture)
{
    private static readonly string[] M3Tables =
        ["Portfolios", "Properties", "Buildings", "Spaces", "Employees", "WorkCategories"];

    [Fact]
    public async Task Query_filter_and_rls_confine_properties_to_the_current_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var store = s.Store(s.OrganizationA);

        // Both the EF query filter and, bypassing it, database RLS return only this tenant's row
        // even though the runtime role holds SELECT on the whole table.
        Assert.Equal(s.PropertyA, (await store.Properties.SingleAsync()).Id);
        Assert.Equal(s.PropertyA, (await store.Properties.IgnoreQueryFilters().SingleAsync()).Id);
    }

    [Fact]
    public async Task Every_m3_table_has_forced_row_level_security()
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
        command.Parameters.AddWithValue("tables", M3Tables);
        Assert.Equal((long)M3Tables.Length, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Runtime_role_without_a_tenant_context_sees_no_property_rows()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var connection = new NpgsqlConnection(fixture.RuntimeConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM operations.\"Properties\"";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Rls_with_check_rejects_a_cross_tenant_category_insert()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var store = s.Store(s.OrganizationA);
        // WorkCategories is the one new table the runtime role may write; the RLS WITH CHECK
        // clause still blocks writing another organization's row.
        var exception = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO operations."WorkCategories" ("OrganizationId", "Id", "Name", "SortOrder", "IsArchived") VALUES ({s.OrganizationB}, {Guid.NewGuid()}, 'forged', 0, false)"""));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task Runtime_role_does_not_own_the_new_tables()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var runtime = new NpgsqlConnection(fixture.RuntimeConnection);
        await runtime.OpenAsync();
        Assert.True(await PropFlow.Infrastructure.Persistence.DatabaseSafety.HasSafeRuntimeRoleAsync(runtime, default));
    }

    [Fact]
    public async Task Readiness_reports_healthy_once_every_business_table_is_protected()
    {
        await using var s = await fixture.CreateScenarioAsync();
        using var response = await s.Client.GetAsync("/health/ready");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
