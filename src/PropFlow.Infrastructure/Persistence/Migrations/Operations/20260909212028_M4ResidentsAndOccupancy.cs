using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class M4ResidentsAndOccupancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Residents",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SmsConsent = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SmsConsentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EmailConsent = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EmailConsentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Residents", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Occupancies",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    MovedInOn = table.Column<DateOnly>(type: "date", nullable: false),
                    MovedOutOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Occupancies", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Occupancies_Residents_OrganizationId_ResidentId",
                        columns: x => new { x.OrganizationId, x.ResidentId },
                        principalSchema: "operations",
                        principalTable: "Residents",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Occupancies_Spaces_OrganizationId_SpaceId",
                        columns: x => new { x.OrganizationId, x.SpaceId },
                        principalSchema: "operations",
                        principalTable: "Spaces",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Occupancies_OrganizationId_ResidentId",
                schema: "operations",
                table: "Occupancies",
                columns: new[] { "OrganizationId", "ResidentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Occupancies_OrganizationId_SpaceId",
                schema: "operations",
                table: "Occupancies",
                columns: new[] { "OrganizationId", "SpaceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Occupancies",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Residents",
                schema: "operations");
        }
    }
}
