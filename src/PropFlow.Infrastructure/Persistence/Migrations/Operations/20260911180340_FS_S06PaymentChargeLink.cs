using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S06PaymentChargeLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChargeId",
                schema: "operations",
                table: "ResidentPayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResidentPayments_OrganizationId_ChargeId",
                schema: "operations",
                table: "ResidentPayments",
                columns: new[] { "OrganizationId", "ChargeId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ResidentPayments_LeaseCharges_OrganizationId_ChargeId",
                schema: "operations",
                table: "ResidentPayments",
                columns: new[] { "OrganizationId", "ChargeId" },
                principalSchema: "operations",
                principalTable: "LeaseCharges",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ResidentPayments_LeaseCharges_OrganizationId_ChargeId",
                schema: "operations",
                table: "ResidentPayments");

            migrationBuilder.DropIndex(
                name: "IX_ResidentPayments_OrganizationId_ChargeId",
                schema: "operations",
                table: "ResidentPayments");

            migrationBuilder.DropColumn(
                name: "ChargeId",
                schema: "operations",
                table: "ResidentPayments");
        }
    }
}
