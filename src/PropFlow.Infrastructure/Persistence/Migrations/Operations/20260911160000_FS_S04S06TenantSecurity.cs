using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations;

[DbContext(typeof(OperationsStore))]
[Migration("20260911160000_FS_S04S06TenantSecurity")]
public sealed class FS_S04S06TenantSecurity : Migration
{
    private static readonly string[] Tables = ["Listings", "Inquiries", "Showings", "Leases", "LeaseNotices"];

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
