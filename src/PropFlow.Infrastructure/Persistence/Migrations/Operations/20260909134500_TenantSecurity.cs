using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations;

[DbContext(typeof(OperationsStore))]
[Migration("20260909134500_TenantSecurity")]
public sealed class TenantSecurity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "Vendors", "WorkItems", "Timeline" })
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
        migrationBuilder.Sql("""
            ALTER TABLE operations."Timeline" ADD CONSTRAINT "FK_Timeline_ActorMembership"
              FOREIGN KEY ("OrganizationId", "ActorId")
              REFERENCES identity."Memberships" ("OrganizationId", "UserId") ON DELETE RESTRICT;
            CREATE FUNCTION operations.reject_timeline_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              RAISE EXCEPTION 'Timeline entries are append-only' USING ERRCODE = '55000';
            END; $$;
            CREATE TRIGGER timeline_append_only BEFORE UPDATE OR DELETE ON operations."Timeline"
              FOR EACH ROW EXECUTE FUNCTION operations.reject_timeline_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER timeline_append_only ON operations."Timeline";
            DROP FUNCTION operations.reject_timeline_mutation();
            ALTER TABLE operations."Timeline" DROP CONSTRAINT "FK_Timeline_ActorMembership";
            """);
        foreach (var table in new[] { "Vendors", "WorkItems", "Timeline" })
            migrationBuilder.Sql($"""
                DROP POLICY tenant_isolation ON operations."{table}";
                ALTER TABLE operations."{table}" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."{table}" DROP CONSTRAINT "FK_{table}_Organization";
                """);
    }
}
