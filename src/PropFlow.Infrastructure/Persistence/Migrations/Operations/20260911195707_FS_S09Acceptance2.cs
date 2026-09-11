using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S09Acceptance2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankAccounts",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Institution = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastFour = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    AssetAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_BankAccounts_ChartOfAccounts_OrganizationId_AssetAccountId",
                        columns: x => new { x.OrganizationId, x.AssetAccountId },
                        principalSchema: "operations",
                        principalTable: "ChartOfAccounts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayableInvoices",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayableInvoices", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PayableInvoices_Vendors_OrganizationId_VendorId",
                        columns: x => new { x.OrganizationId, x.VendorId },
                        principalSchema: "operations",
                        principalTable: "Vendors",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivableInvoices",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivableInvoices", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_ReceivableInvoices_Residents_OrganizationId_ResidentId",
                        columns: x => new { x.OrganizationId, x.ResidentId },
                        principalSchema: "operations",
                        principalTable: "Residents",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankTransactions",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PostedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransactions", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_BankTransactions_BankAccounts_OrganizationId_BankAccountId",
                        columns: x => new { x.OrganizationId, x.BankAccountId },
                        principalSchema: "operations",
                        principalTable: "BankAccounts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankTransactions_JournalEntries_OrganizationId_JournalEntry~",
                        columns: x => new { x.OrganizationId, x.JournalEntryId },
                        principalSchema: "operations",
                        principalTable: "JournalEntries",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_OrganizationId_AssetAccountId",
                schema: "operations",
                table: "BankAccounts",
                columns: new[] { "OrganizationId", "AssetAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_OrganizationId_Name",
                schema: "operations",
                table: "BankAccounts",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_OrganizationId_BankAccountId_ExternalId",
                schema: "operations",
                table: "BankTransactions",
                columns: new[] { "OrganizationId", "BankAccountId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_OrganizationId_JournalEntryId",
                schema: "operations",
                table: "BankTransactions",
                columns: new[] { "OrganizationId", "JournalEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayableInvoices_OrganizationId_InvoiceNumber",
                schema: "operations",
                table: "PayableInvoices",
                columns: new[] { "OrganizationId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayableInvoices_OrganizationId_VendorId_Status",
                schema: "operations",
                table: "PayableInvoices",
                columns: new[] { "OrganizationId", "VendorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableInvoices_OrganizationId_InvoiceNumber",
                schema: "operations",
                table: "ReceivableInvoices",
                columns: new[] { "OrganizationId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableInvoices_OrganizationId_ResidentId_Status",
                schema: "operations",
                table: "ReceivableInvoices",
                columns: new[] { "OrganizationId", "ResidentId", "Status" });

            foreach (var table in new[] { "PayableInvoices", "ReceivableInvoices", "BankAccounts", "BankTransactions" })
                migrationBuilder.Sql($"ALTER TABLE operations.\"{table}\" ADD CONSTRAINT \"FK_{table}_Organization\" FOREIGN KEY (\"OrganizationId\") REFERENCES identity.\"Organizations\" (\"Id\") ON DELETE RESTRICT; ALTER TABLE operations.\"{table}\" ENABLE ROW LEVEL SECURITY; ALTER TABLE operations.\"{table}\" FORCE ROW LEVEL SECURITY; CREATE POLICY tenant_isolation ON operations.\"{table}\" USING (\"OrganizationId\" = nullif(current_setting('app.organization_id', true), '')::uuid) WITH CHECK (\"OrganizationId\" = nullif(current_setting('app.organization_id', true), '')::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "PayableInvoices", "ReceivableInvoices", "BankAccounts", "BankTransactions" })
                migrationBuilder.Sql($"DROP POLICY tenant_isolation ON operations.\"{table}\"; ALTER TABLE operations.\"{table}\" DISABLE ROW LEVEL SECURITY; ALTER TABLE operations.\"{table}\" DROP CONSTRAINT \"FK_{table}_Organization\";");
            migrationBuilder.DropTable(
                name: "BankTransactions",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "PayableInvoices",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "ReceivableInvoices",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "BankAccounts",
                schema: "operations");
        }
    }
}
