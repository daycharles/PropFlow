using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S09GeneralLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChartOfAccounts",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChartOfAccounts", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "FiscalPeriods",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClosedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalPeriods", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Memo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PostedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReversalOfId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_JournalEntries_FiscalPeriods_OrganizationId_PeriodId",
                        columns: x => new { x.OrganizationId, x.PeriodId },
                        principalSchema: "operations",
                        principalTable: "FiscalPeriods",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntries_JournalEntries_OrganizationId_ReversalOfId",
                        columns: x => new { x.OrganizationId, x.ReversalOfId },
                        principalSchema: "operations",
                        principalTable: "JournalEntries",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalLines",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Debit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Memo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalLines", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_JournalLines_ChartOfAccounts_OrganizationId_AccountId",
                        columns: x => new { x.OrganizationId, x.AccountId },
                        principalSchema: "operations",
                        principalTable: "ChartOfAccounts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalLines_JournalEntries_OrganizationId_EntryId",
                        columns: x => new { x.OrganizationId, x.EntryId },
                        principalSchema: "operations",
                        principalTable: "JournalEntries",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChartOfAccounts_OrganizationId_Code",
                schema: "operations",
                table: "ChartOfAccounts",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_OrganizationId_Name",
                schema: "operations",
                table: "FiscalPeriods",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_OrganizationId_StartsOn_EndsOn",
                schema: "operations",
                table: "FiscalPeriods",
                columns: new[] { "OrganizationId", "StartsOn", "EndsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_OrganizationId_PeriodId_EntryDate",
                schema: "operations",
                table: "JournalEntries",
                columns: new[] { "OrganizationId", "PeriodId", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_OrganizationId_ReversalOfId",
                schema: "operations",
                table: "JournalEntries",
                columns: new[] { "OrganizationId", "ReversalOfId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_OrganizationId_AccountId",
                schema: "operations",
                table: "JournalLines",
                columns: new[] { "OrganizationId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_OrganizationId_EntryId",
                schema: "operations",
                table: "JournalLines",
                columns: new[] { "OrganizationId", "EntryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JournalLines",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "ChartOfAccounts",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "JournalEntries",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "FiscalPeriods",
                schema: "operations");
        }
    }
}
