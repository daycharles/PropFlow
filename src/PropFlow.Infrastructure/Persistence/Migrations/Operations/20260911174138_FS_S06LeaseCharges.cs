using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S06LeaseCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaseCharges",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseCharges", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaseCharges_Leases_OrganizationId_LeaseId",
                        columns: x => new { x.OrganizationId, x.LeaseId },
                        principalSchema: "operations",
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseCharges_OrganizationId_LeaseId_Status_DueOn",
                schema: "operations",
                table: "LeaseCharges",
                columns: new[] { "OrganizationId", "LeaseId", "Status", "DueOn" });

            migrationBuilder.Sql("""
                ALTER TABLE operations."LeaseCharges"
                  ADD CONSTRAINT "FK_LeaseCharges_Organization" FOREIGN KEY ("OrganizationId")
                  REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
                ALTER TABLE operations."LeaseCharges" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."LeaseCharges" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON operations."LeaseCharges"
                  USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
                  WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY tenant_isolation ON operations."LeaseCharges";
                ALTER TABLE operations."LeaseCharges" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."LeaseCharges" DROP CONSTRAINT "FK_LeaseCharges_Organization";
                """);
            migrationBuilder.DropTable(
                name: "LeaseCharges",
                schema: "operations");
        }
    }
}
