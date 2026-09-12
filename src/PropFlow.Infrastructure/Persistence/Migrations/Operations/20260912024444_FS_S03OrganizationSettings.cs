using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S03OrganizationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationSettings",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultTimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BusinessHours = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationSettings", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSettings_OrganizationId",
                schema: "operations",
                table: "OrganizationSettings",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.Sql("""
            ALTER TABLE operations."OrganizationSettings"
              ADD CONSTRAINT "FK_OrganizationSettings_Organization" FOREIGN KEY ("OrganizationId")
              REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
            ALTER TABLE operations."OrganizationSettings" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."OrganizationSettings" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON operations."OrganizationSettings"
              USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
              WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DROP POLICY tenant_isolation ON operations."OrganizationSettings";
            ALTER TABLE operations."OrganizationSettings" DISABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."OrganizationSettings" DROP CONSTRAINT "FK_OrganizationSettings_Organization";
            """);
            migrationBuilder.DropTable(
                name: "OrganizationSettings",
                schema: "operations");
        }
    }
}
