using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S02ArchiveState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                schema: "operations",
                table: "Spaces",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                schema: "operations",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                schema: "operations",
                table: "Portfolios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                schema: "operations",
                table: "Buildings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsArchived",
                schema: "operations",
                table: "Spaces");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                schema: "operations",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                schema: "operations",
                table: "Portfolios");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                schema: "operations",
                table: "Buildings");
        }
    }
}
