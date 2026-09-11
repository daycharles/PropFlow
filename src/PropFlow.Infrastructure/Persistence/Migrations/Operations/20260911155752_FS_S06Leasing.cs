using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S06Leasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Leases",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    MonthlyRent = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    SecurityDeposit = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NoticeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MoveOutOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leases", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Leases_Residents_OrganizationId_ResidentId",
                        columns: x => new { x.OrganizationId, x.ResidentId },
                        principalSchema: "operations",
                        principalTable: "Residents",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Leases_Spaces_OrganizationId_SpaceId",
                        columns: x => new { x.OrganizationId, x.SpaceId },
                        principalSchema: "operations",
                        principalTable: "Spaces",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaseNotices",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseNotices", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaseNotices_Leases_OrganizationId_LeaseId",
                        columns: x => new { x.OrganizationId, x.LeaseId },
                        principalSchema: "operations",
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseNotices_OrganizationId_LeaseId",
                schema: "operations",
                table: "LeaseNotices",
                columns: new[] { "OrganizationId", "LeaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseNotices_OrganizationId_Status_DueOn",
                schema: "operations",
                table: "LeaseNotices",
                columns: new[] { "OrganizationId", "Status", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Leases_OrganizationId_ResidentId_Status",
                schema: "operations",
                table: "Leases",
                columns: new[] { "OrganizationId", "ResidentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Leases_OrganizationId_SpaceId_Status",
                schema: "operations",
                table: "Leases",
                columns: new[] { "OrganizationId", "SpaceId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaseNotices",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Leases",
                schema: "operations");
        }
    }
}
