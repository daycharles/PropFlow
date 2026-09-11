using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S04Marketing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Listings",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Headline = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    AvailableOn = table.Column<DateOnly>(type: "date", nullable: true),
                    MonthlyRent = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Listings", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Listings_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Listings_Spaces_OrganizationId_SpaceId",
                        columns: x => new { x.OrganizationId, x.SpaceId },
                        principalSchema: "operations",
                        principalTable: "Spaces",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Inquiries",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProspectName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inquiries", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Inquiries_Listings_OrganizationId_ListingId",
                        columns: x => new { x.OrganizationId, x.ListingId },
                        principalSchema: "operations",
                        principalTable: "Listings",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Showings",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProspectName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Showings", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Showings_Listings_OrganizationId_ListingId",
                        columns: x => new { x.OrganizationId, x.ListingId },
                        principalSchema: "operations",
                        principalTable: "Listings",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Inquiries_OrganizationId_ListingId_Email_Status",
                schema: "operations",
                table: "Inquiries",
                columns: new[] { "OrganizationId", "ListingId", "Email", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Listings_OrganizationId_PropertyId",
                schema: "operations",
                table: "Listings",
                columns: new[] { "OrganizationId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Listings_OrganizationId_SpaceId",
                schema: "operations",
                table: "Listings",
                columns: new[] { "OrganizationId", "SpaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Listings_OrganizationId_Status_AvailableOn",
                schema: "operations",
                table: "Listings",
                columns: new[] { "OrganizationId", "Status", "AvailableOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Showings_OrganizationId_ListingId_ScheduledAt",
                schema: "operations",
                table: "Showings",
                columns: new[] { "OrganizationId", "ListingId", "ScheduledAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Inquiries",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Showings",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Listings",
                schema: "operations");
        }
    }
}
