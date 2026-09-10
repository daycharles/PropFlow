using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PropFlow.Infrastructure.Integrations;

namespace PropFlow.Infrastructure.Persistence.Migrations.Integrations;

// Standard tenant isolation for the Integrations tables: an organization foreign key, forced
// RLS, and a policy bound to app.organization_id. Same pattern as every other business table.
[DbContext(typeof(IntegrationStore))]
[Migration("20260910002700_IntegrationsTenantSecurity")]
public sealed class IntegrationsTenantSecurity : Migration
{
    private static readonly string[] Tables = ["Connections", "RecordLinks"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.Sql($"""
                ALTER TABLE integrations."{table}"
                  ADD CONSTRAINT "FK_{table}_Organization" FOREIGN KEY ("OrganizationId")
                  REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
                ALTER TABLE integrations."{table}" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE integrations."{table}" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON integrations."{table}"
                  USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
                  WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.Sql($"""
                DROP POLICY tenant_isolation ON integrations."{table}";
                ALTER TABLE integrations."{table}" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE integrations."{table}" DROP CONSTRAINT "FK_{table}_Organization";
                """);
        }
    }
}
