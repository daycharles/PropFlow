using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S03NotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_OrganizationId_UserId_EventType",
                schema: "operations",
                table: "NotificationPreferences",
                columns: new[] { "OrganizationId", "UserId", "EventType" },
                unique: true);

            migrationBuilder.Sql("""
            ALTER TABLE operations."NotificationPreferences"
              ADD CONSTRAINT "FK_NotificationPreferences_Organization" FOREIGN KEY ("OrganizationId")
              REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
            ALTER TABLE operations."NotificationPreferences" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."NotificationPreferences" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON operations."NotificationPreferences"
              USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
              WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DROP POLICY tenant_isolation ON operations."NotificationPreferences";
            ALTER TABLE operations."NotificationPreferences" DISABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."NotificationPreferences" DROP CONSTRAINT "FK_NotificationPreferences_Organization";
            """);
            migrationBuilder.DropTable(
                name: "NotificationPreferences",
                schema: "operations");
        }
    }
}
