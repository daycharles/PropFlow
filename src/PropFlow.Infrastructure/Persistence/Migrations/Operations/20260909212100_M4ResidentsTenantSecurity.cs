using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations;

// Applies the standard tenant isolation to the resident/occupancy tables: an organization
// foreign key, forced RLS, and a policy bound to app.organization_id.
[DbContext(typeof(OperationsStore))]
[Migration("20260909212100_M4ResidentsTenantSecurity")]
public sealed class M4ResidentsTenantSecurity : Migration
{
    private static readonly string[] Tables = ["Residents", "Occupancies"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.Sql($"""
                ALTER TABLE operations."{table}"
                  ADD CONSTRAINT "FK_{table}_Organization" FOREIGN KEY ("OrganizationId")
                  REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
                ALTER TABLE operations."{table}" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."{table}" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON operations."{table}"
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
                DROP POLICY tenant_isolation ON operations."{table}";
                ALTER TABLE operations."{table}" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."{table}" DROP CONSTRAINT "FK_{table}_Organization";
                """);
        }
    }
}
