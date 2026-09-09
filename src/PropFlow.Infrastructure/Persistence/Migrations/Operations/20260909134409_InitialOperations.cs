using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class InitialOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "operations");

            migrationBuilder.CreateTable(
                name: "Vendors",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "WorkItems",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItems", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_WorkItems_Vendors_OrganizationId_VendorId",
                        columns: x => new { x.OrganizationId, x.VendorId },
                        principalSchema: "operations",
                        principalTable: "Vendors",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Timeline",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PreviousVendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Timeline", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Timeline_WorkItems_OrganizationId_WorkId",
                        columns: x => new { x.OrganizationId, x.WorkId },
                        principalSchema: "operations",
                        principalTable: "WorkItems",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Timeline_OrganizationId_WorkId_OccurredAt",
                schema: "operations",
                table: "Timeline",
                columns: new[] { "OrganizationId", "WorkId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_OrganizationId_VendorId",
                schema: "operations",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "VendorId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Timeline",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "WorkItems",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Vendors",
                schema: "operations");
        }
    }
}
