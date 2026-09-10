using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations;

[DbContext(typeof(OperationsStore))]
[Migration("20260910150001_M5AutomationRulesTenantSecurity")]
public sealed class M5AutomationRulesTenantSecurity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE operations."AutomationRules"
          ADD CONSTRAINT "FK_AutomationRules_Organization" FOREIGN KEY ("OrganizationId")
          REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
        ALTER TABLE operations."AutomationRules" ENABLE ROW LEVEL SECURITY;
        ALTER TABLE operations."AutomationRules" FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_isolation ON operations."AutomationRules"
          USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
          WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP POLICY tenant_isolation ON operations."AutomationRules";
        ALTER TABLE operations."AutomationRules" DISABLE ROW LEVEL SECURITY;
        ALTER TABLE operations."AutomationRules" DROP CONSTRAINT "FK_AutomationRules_Organization";
        """);
}
