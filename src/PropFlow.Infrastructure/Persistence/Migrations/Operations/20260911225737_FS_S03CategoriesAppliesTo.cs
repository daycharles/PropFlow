using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S03CategoriesAppliesTo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkCategories_OrganizationId_Name",
                schema: "operations",
                table: "WorkCategories");

            migrationBuilder.AddColumn<string>(
                name: "AppliesTo",
                schema: "operations",
                table: "WorkCategories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "WorkItem");

            migrationBuilder.CreateIndex(
                name: "IX_WorkCategories_OrganizationId_AppliesTo_Name",
                schema: "operations",
                table: "WorkCategories",
                columns: new[] { "OrganizationId", "AppliesTo", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkCategories_OrganizationId_AppliesTo_Name",
                schema: "operations",
                table: "WorkCategories");

            migrationBuilder.DropColumn(
                name: "AppliesTo",
                schema: "operations",
                table: "WorkCategories");

            migrationBuilder.CreateIndex(
                name: "IX_WorkCategories_OrganizationId_Name",
                schema: "operations",
                table: "WorkCategories",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);
        }
    }
}
