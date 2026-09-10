using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class M3SavedViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavedViews",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Filters = table.Column<string>(type: "jsonb", nullable: false),
                    Columns = table.Column<string>(type: "jsonb", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedViews", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_OrganizationId_UserId_IsDefault",
                schema: "operations",
                table: "SavedViews",
                columns: new[] { "OrganizationId", "UserId", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_OrganizationId_UserId_Name",
                schema: "operations",
                table: "SavedViews",
                columns: new[] { "OrganizationId", "UserId", "Name" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE operations."SavedViews"
                  ADD CONSTRAINT "FK_SavedViews_Organization" FOREIGN KEY ("OrganizationId")
                  REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
                ALTER TABLE operations."SavedViews"
                  ADD CONSTRAINT "FK_SavedViews_OwnerMembership" FOREIGN KEY ("OrganizationId", "UserId")
                  REFERENCES identity."Memberships" ("OrganizationId", "UserId") ON DELETE RESTRICT;
                ALTER TABLE operations."SavedViews" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."SavedViews" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON operations."SavedViews"
                  USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
                  WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY tenant_isolation ON operations."SavedViews";
                ALTER TABLE operations."SavedViews" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."SavedViews" DROP CONSTRAINT "FK_SavedViews_OwnerMembership";
                ALTER TABLE operations."SavedViews" DROP CONSTRAINT "FK_SavedViews_Organization";
                """);
            migrationBuilder.DropTable(
                name: "SavedViews",
                schema: "operations");
        }
    }
}
