using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations;

// Standard tenant isolation for the assets table: an organization foreign key, forced RLS,
// and a policy bound to app.organization_id.
[DbContext(typeof(OperationsStore))]
[Migration("20260909215200_M6AssetsTenantSecurity")]
public sealed class M6AssetsTenantSecurity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE operations."Assets"
          ADD CONSTRAINT "FK_Assets_Organization" FOREIGN KEY ("OrganizationId")
          REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
        ALTER TABLE operations."Assets" ENABLE ROW LEVEL SECURITY;
        ALTER TABLE operations."Assets" FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_isolation ON operations."Assets"
          USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
          WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP POLICY tenant_isolation ON operations."Assets";
        ALTER TABLE operations."Assets" DISABLE ROW LEVEL SECURITY;
        ALTER TABLE operations."Assets" DROP CONSTRAINT "FK_Assets_Organization";
        """);
}
