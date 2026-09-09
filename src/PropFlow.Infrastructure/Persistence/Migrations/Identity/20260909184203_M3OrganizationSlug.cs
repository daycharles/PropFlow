using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Identity
{
    /// <inheritdoc />
    public partial class M3OrganizationSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                schema: "identity",
                table: "Organizations",
                type: "character varying(63)",
                maxLength: 63,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Slug",
                schema: "identity",
                table: "Organizations",
                column: "Slug",
                unique: true,
                filter: "\"Slug\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_Slug",
                schema: "identity",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Slug",
                schema: "identity",
                table: "Organizations");
        }
    }
}
