using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S07Announcements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Announcements",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Announcements", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_OrganizationId_Status_ExpiresAt",
                schema: "operations",
                table: "Announcements",
                columns: new[] { "OrganizationId", "Status", "ExpiresAt" });

            migrationBuilder.Sql("""
                ALTER TABLE operations."Announcements"
                  ADD CONSTRAINT "FK_Announcements_Organization" FOREIGN KEY ("OrganizationId")
                  REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
                ALTER TABLE operations."Announcements" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."Announcements" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON operations."Announcements"
                  USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
                  WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY tenant_isolation ON operations."Announcements";
                ALTER TABLE operations."Announcements" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."Announcements" DROP CONSTRAINT "FK_Announcements_Organization";
                """);
            migrationBuilder.DropTable(
                name: "Announcements",
                schema: "operations");
        }
    }
}
