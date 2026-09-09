using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PropFlow.Infrastructure.Communications;

namespace PropFlow.Infrastructure.Persistence.Migrations.Communications;

[DbContext(typeof(CommunicationsStore))]
[Migration("20260909181505_CommunicationsTenantSecurity")]
public sealed class CommunicationsTenantSecurity : Migration
{
    private static readonly string[] Tables = ["MessageTemplates", "OutboxMessages"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.Sql($"""
                ALTER TABLE communications."{table}"
                  ADD CONSTRAINT "FK_{table}_Organization" FOREIGN KEY ("OrganizationId")
                  REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
                ALTER TABLE communications."{table}" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE communications."{table}" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON communications."{table}"
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
                DROP POLICY tenant_isolation ON communications."{table}";
                ALTER TABLE communications."{table}" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE communications."{table}" DROP CONSTRAINT "FK_{table}_Organization";
                """);
        }
    }
}
