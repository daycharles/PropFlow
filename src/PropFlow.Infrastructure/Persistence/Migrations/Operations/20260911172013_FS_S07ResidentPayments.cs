using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S07ResidentPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResidentPayments",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResidentPayments", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_ResidentPayments_Leases_OrganizationId_LeaseId",
                        columns: x => new { x.OrganizationId, x.LeaseId },
                        principalSchema: "operations",
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ResidentPayments_Residents_OrganizationId_ResidentId",
                        columns: x => new { x.OrganizationId, x.ResidentId },
                        principalSchema: "operations",
                        principalTable: "Residents",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResidentPayments_OrganizationId_LeaseId",
                schema: "operations",
                table: "ResidentPayments",
                columns: new[] { "OrganizationId", "LeaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResidentPayments_OrganizationId_ResidentId_Status_DueOn",
                schema: "operations",
                table: "ResidentPayments",
                columns: new[] { "OrganizationId", "ResidentId", "Status", "DueOn" });

            migrationBuilder.Sql("""
                ALTER TABLE operations."ResidentPayments"
                  ADD CONSTRAINT "FK_ResidentPayments_Organization" FOREIGN KEY ("OrganizationId")
                  REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
                ALTER TABLE operations."ResidentPayments" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."ResidentPayments" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON operations."ResidentPayments"
                  USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
                  WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY tenant_isolation ON operations."ResidentPayments";
                ALTER TABLE operations."ResidentPayments" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."ResidentPayments" DROP CONSTRAINT "FK_ResidentPayments_Organization";
                """);
            migrationBuilder.DropTable(
                name: "ResidentPayments",
                schema: "operations");
        }
    }
}
