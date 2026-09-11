using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S08Billing : Migration
    {
        private static readonly string[] TenantTables = ["RecurringCharges", "Credits", "LateFeeRules", "PaymentRefunds"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                schema: "operations",
                table: "ResidentPayments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                schema: "operations",
                table: "ResidentPayments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderReference",
                schema: "operations",
                table: "ResidentPayments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                schema: "operations",
                table: "ResidentPayments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                schema: "operations",
                table: "LeaseCharges",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountApplied",
                schema: "operations",
                table: "LeaseCharges",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LateFeeAppliedOn",
                schema: "operations",
                table: "LeaseCharges",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecurringChargeId",
                schema: "operations",
                table: "LeaseCharges",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Credits",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Credits", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Credits_Leases_OrganizationId_LeaseId",
                        columns: x => new { x.OrganizationId, x.LeaseId },
                        principalSchema: "operations",
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LateFeeRules",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GraceDays = table.Column<int>(type: "integer", nullable: false),
                    FlatAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PercentOfOutstanding = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    MaximumAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LateFeeRules", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_LateFeeRules_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRefunds",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefunds", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_ResidentPayments_OrganizationId_PaymentId",
                        columns: x => new { x.OrganizationId, x.PaymentId },
                        principalSchema: "operations",
                        principalTable: "ResidentPayments",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringCharges",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GeneratedThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringCharges", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_RecurringCharges_Leases_OrganizationId_LeaseId",
                        columns: x => new { x.OrganizationId, x.LeaseId },
                        principalSchema: "operations",
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResidentPayments_OrganizationId_ProviderReference",
                schema: "operations",
                table: "ResidentPayments",
                columns: new[] { "OrganizationId", "ProviderReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaseCharges_OrganizationId_RecurringChargeId_DueOn",
                schema: "operations",
                table: "LeaseCharges",
                columns: new[] { "OrganizationId", "RecurringChargeId", "DueOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Credits_OrganizationId_LeaseId_Status",
                schema: "operations",
                table: "Credits",
                columns: new[] { "OrganizationId", "LeaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LateFeeRules_OrganizationId_PropertyId",
                schema: "operations",
                table: "LateFeeRules",
                columns: new[] { "OrganizationId", "PropertyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_OrganizationId_PaymentId",
                schema: "operations",
                table: "PaymentRefunds",
                columns: new[] { "OrganizationId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_OrganizationId_ProviderReference",
                schema: "operations",
                table: "PaymentRefunds",
                columns: new[] { "OrganizationId", "ProviderReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCharges_OrganizationId_LeaseId_Status",
                schema: "operations",
                table: "RecurringCharges",
                columns: new[] { "OrganizationId", "LeaseId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_LeaseCharges_RecurringCharges_OrganizationId_RecurringCharg~",
                schema: "operations",
                table: "LeaseCharges",
                columns: new[] { "OrganizationId", "RecurringChargeId" },
                principalSchema: "operations",
                principalTable: "RecurringCharges",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE operations."{table}"
                      ADD CONSTRAINT "FK_{table}_Organization" FOREIGN KEY ("OrganizationId")
                      REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
                    ALTER TABLE operations."{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE operations."{table}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON operations."{table}"
                      USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
                      WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeaseCharges_RecurringCharges_OrganizationId_RecurringCharg~",
                schema: "operations",
                table: "LeaseCharges");

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"""
                    DROP POLICY tenant_isolation ON operations."{table}";
                    ALTER TABLE operations."{table}" DISABLE ROW LEVEL SECURITY;
                    ALTER TABLE operations."{table}" DROP CONSTRAINT "FK_{table}_Organization";
                    """);
            }

            migrationBuilder.DropTable(
                name: "Credits",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "LateFeeRules",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "PaymentRefunds",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "RecurringCharges",
                schema: "operations");

            migrationBuilder.DropIndex(
                name: "IX_ResidentPayments_OrganizationId_ProviderReference",
                schema: "operations",
                table: "ResidentPayments");

            migrationBuilder.DropIndex(
                name: "IX_LeaseCharges_OrganizationId_RecurringChargeId_DueOn",
                schema: "operations",
                table: "LeaseCharges");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                schema: "operations",
                table: "ResidentPayments");

            migrationBuilder.DropColumn(
                name: "ProviderReference",
                schema: "operations",
                table: "ResidentPayments");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                schema: "operations",
                table: "ResidentPayments");

            migrationBuilder.DropColumn(
                name: "AmountApplied",
                schema: "operations",
                table: "LeaseCharges");

            migrationBuilder.DropColumn(
                name: "LateFeeAppliedOn",
                schema: "operations",
                table: "LeaseCharges");

            migrationBuilder.DropColumn(
                name: "RecurringChargeId",
                schema: "operations",
                table: "LeaseCharges");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                schema: "operations",
                table: "ResidentPayments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                schema: "operations",
                table: "LeaseCharges",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);
        }
    }
}
