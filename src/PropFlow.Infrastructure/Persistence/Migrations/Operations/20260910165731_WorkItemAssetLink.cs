using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class WorkItemAssetLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssetId",
                schema: "operations",
                table: "WorkItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_OrganizationId_AssetId",
                schema: "operations",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "AssetId" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_Assets_OrganizationId_AssetId",
                schema: "operations",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "AssetId" },
                principalSchema: "operations",
                principalTable: "Assets",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_Assets_OrganizationId_AssetId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_OrganizationId_AssetId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "AssetId",
                schema: "operations",
                table: "WorkItems");
        }
    }
}
