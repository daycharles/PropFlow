using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S08Acceptance2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DelinquencyCases",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpenedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelinquencyCases", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_DelinquencyCases_Leases_OrganizationId_LeaseId",
                        columns: x => new { x.OrganizationId, x.LeaseId },
                        principalSchema: "operations",
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethods",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderToken = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastFour = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethods", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PaymentMethods_Residents_OrganizationId_ResidentId",
                        columns: x => new { x.OrganizationId, x.ResidentId },
                        principalSchema: "operations",
                        principalTable: "Residents",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentReceipts",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReceipts", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PaymentReceipts_ResidentPayments_OrganizationId_PaymentId",
                        columns: x => new { x.OrganizationId, x.PaymentId },
                        principalSchema: "operations",
                        principalTable: "ResidentPayments",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentReconciliations",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReconciliations", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PaymentReconciliations_ResidentPayments_OrganizationId_Paym~",
                        columns: x => new { x.OrganizationId, x.PaymentId },
                        principalSchema: "operations",
                        principalTable: "ResidentPayments",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DelinquencyCases_OrganizationId_LeaseId_Status",
                schema: "operations",
                table: "DelinquencyCases",
                columns: new[] { "OrganizationId", "LeaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_OrganizationId_ResidentId_Status",
                schema: "operations",
                table: "PaymentMethods",
                columns: new[] { "OrganizationId", "ResidentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReceipts_OrganizationId_PaymentId",
                schema: "operations",
                table: "PaymentReceipts",
                columns: new[] { "OrganizationId", "PaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReceipts_OrganizationId_ReceiptNumber",
                schema: "operations",
                table: "PaymentReceipts",
                columns: new[] { "OrganizationId", "ReceiptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliations_OrganizationId_PaymentId",
                schema: "operations",
                table: "PaymentReconciliations",
                columns: new[] { "OrganizationId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliations_OrganizationId_ProviderReference",
                schema: "operations",
                table: "PaymentReconciliations",
                columns: new[] { "OrganizationId", "ProviderReference" },
                unique: true);

            foreach (var table in new[] { "PaymentMethods", "PaymentReceipts", "PaymentReconciliations", "DelinquencyCases" })
                migrationBuilder.Sql($"ALTER TABLE operations.\"{table}\" ADD CONSTRAINT \"FK_{table}_Organization\" FOREIGN KEY (\"OrganizationId\") REFERENCES identity.\"Organizations\" (\"Id\") ON DELETE RESTRICT; ALTER TABLE operations.\"{table}\" ENABLE ROW LEVEL SECURITY; ALTER TABLE operations.\"{table}\" FORCE ROW LEVEL SECURITY; CREATE POLICY tenant_isolation ON operations.\"{table}\" USING (\"OrganizationId\" = nullif(current_setting('app.organization_id', true), '')::uuid) WITH CHECK (\"OrganizationId\" = nullif(current_setting('app.organization_id', true), '')::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "PaymentMethods", "PaymentReceipts", "PaymentReconciliations", "DelinquencyCases" })
                migrationBuilder.Sql($"DROP POLICY tenant_isolation ON operations.\"{table}\"; ALTER TABLE operations.\"{table}\" DISABLE ROW LEVEL SECURITY; ALTER TABLE operations.\"{table}\" DROP CONSTRAINT \"FK_{table}_Organization\";");
            migrationBuilder.DropTable(
                name: "DelinquencyCases",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "PaymentMethods",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "PaymentReceipts",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "PaymentReconciliations",
                schema: "operations");
        }
    }
}
