using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class TimelineResidentVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ResidentVisible",
                schema: "operations",
                table: "Timeline",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResidentVisible",
                schema: "operations",
                table: "Timeline");
        }
    }
}
