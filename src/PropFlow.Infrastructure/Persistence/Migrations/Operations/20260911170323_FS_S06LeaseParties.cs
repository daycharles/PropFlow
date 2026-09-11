using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S06LeaseParties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaseParties",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseParties", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaseParties_Leases_OrganizationId_LeaseId",
                        columns: x => new { x.OrganizationId, x.LeaseId },
                        principalSchema: "operations",
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseParties_OrganizationId_LeaseId",
                schema: "operations",
                table: "LeaseParties",
                columns: new[] { "OrganizationId", "LeaseId" });

            migrationBuilder.Sql("""
                ALTER TABLE operations."LeaseParties"
                  ADD CONSTRAINT "FK_LeaseParties_Organization" FOREIGN KEY ("OrganizationId")
                  REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
                ALTER TABLE operations."LeaseParties" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."LeaseParties" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON operations."LeaseParties"
                  USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
                  WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY tenant_isolation ON operations."LeaseParties";
                ALTER TABLE operations."LeaseParties" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."LeaseParties" DROP CONSTRAINT "FK_LeaseParties_Organization";
                """);
            migrationBuilder.DropTable(
                name: "LeaseParties",
                schema: "operations");
        }
    }
}
