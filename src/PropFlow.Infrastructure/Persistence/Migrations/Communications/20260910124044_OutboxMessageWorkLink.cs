using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Communications
{
    /// <inheritdoc />
    public partial class OutboxMessageWorkLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ResidentVisible",
                schema: "communications",
                table: "OutboxMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkId",
                schema: "communications",
                table: "OutboxMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_OrganizationId_WorkId_CreatedAt",
                schema: "communications",
                table: "OutboxMessages",
                columns: new[] { "OrganizationId", "WorkId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_OrganizationId_WorkId_CreatedAt",
                schema: "communications",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ResidentVisible",
                schema: "communications",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "WorkId",
                schema: "communications",
                table: "OutboxMessages");
        }
    }
}
