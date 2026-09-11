using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S07HouseholdMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HouseholdMembers",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Relationship = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdMembers", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_HouseholdMembers_Residents_OrganizationId_ResidentId",
                        columns: x => new { x.OrganizationId, x.ResidentId },
                        principalSchema: "operations",
                        principalTable: "Residents",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdMembers_OrganizationId_ResidentId",
                schema: "operations",
                table: "HouseholdMembers",
                columns: new[] { "OrganizationId", "ResidentId" });

            migrationBuilder.Sql("""
                ALTER TABLE operations."HouseholdMembers"
                  ADD CONSTRAINT "FK_HouseholdMembers_Organization" FOREIGN KEY ("OrganizationId")
                  REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
                ALTER TABLE operations."HouseholdMembers" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."HouseholdMembers" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON operations."HouseholdMembers"
                  USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
                  WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY tenant_isolation ON operations."HouseholdMembers";
                ALTER TABLE operations."HouseholdMembers" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."HouseholdMembers" DROP CONSTRAINT "FK_HouseholdMembers_Organization";
                """);
            migrationBuilder.DropTable(
                name: "HouseholdMembers",
                schema: "operations");
        }
    }
}
