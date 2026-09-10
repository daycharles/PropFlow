using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class TimelineDropLegacyVendorColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviousVendorId",
                schema: "operations",
                table: "Timeline");

            migrationBuilder.DropColumn(
                name: "VendorId",
                schema: "operations",
                table: "Timeline");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreviousVendorId",
                schema: "operations",
                table: "Timeline",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VendorId",
                schema: "operations",
                table: "Timeline",
                type: "uuid",
                nullable: true);
        }
    }
}
