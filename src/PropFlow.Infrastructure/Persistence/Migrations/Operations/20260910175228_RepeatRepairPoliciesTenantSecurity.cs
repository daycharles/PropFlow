using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations;

[DbContext(typeof(OperationsStore))]
[Migration("20260910175228_RepeatRepairPoliciesTenantSecurity")]
public sealed class RepeatRepairPoliciesTenantSecurity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE operations."RepeatRepairPolicies"
          ADD CONSTRAINT "FK_RepeatRepairPolicies_Organization" FOREIGN KEY ("OrganizationId")
          REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
        ALTER TABLE operations."RepeatRepairPolicies" ENABLE ROW LEVEL SECURITY;
        ALTER TABLE operations."RepeatRepairPolicies" FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_isolation ON operations."RepeatRepairPolicies"
          USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
          WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP POLICY tenant_isolation ON operations."RepeatRepairPolicies";
        ALTER TABLE operations."RepeatRepairPolicies" DISABLE ROW LEVEL SECURITY;
        ALTER TABLE operations."RepeatRepairPolicies" DROP CONSTRAINT "FK_RepeatRepairPolicies_Organization";
        """);
}
