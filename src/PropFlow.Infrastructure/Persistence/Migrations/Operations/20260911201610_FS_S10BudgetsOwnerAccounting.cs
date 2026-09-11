using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S10BudgetsOwnerAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PropertyId",
                schema: "operations",
                table: "JournalLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Budgets",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Budgets_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManagementFeeRules",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Minimum = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagementFeeRules", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_ManagementFeeRules_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Owners",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Owners", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "BudgetLines",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetLines", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_BudgetLines_Budgets_OrganizationId_BudgetId",
                        columns: x => new { x.OrganizationId, x.BudgetId },
                        principalSchema: "operations",
                        principalTable: "Budgets",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetLines_ChartOfAccounts_OrganizationId_AccountId",
                        columns: x => new { x.OrganizationId, x.AccountId },
                        principalSchema: "operations",
                        principalTable: "ChartOfAccounts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Distributions",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Distributions", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Distributions_Owners_OrganizationId_OwnerId",
                        columns: x => new { x.OrganizationId, x.OwnerId },
                        principalSchema: "operations",
                        principalTable: "Owners",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Distributions_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OwnerStatements",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Income = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Expenses = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ManagementFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Distributions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerStatements", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_OwnerStatements_Owners_OrganizationId_OwnerId",
                        columns: x => new { x.OrganizationId, x.OwnerId },
                        principalSchema: "operations",
                        principalTable: "Owners",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnerStatements_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PropertyOwnerships",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyOwnerships", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PropertyOwnerships_Owners_OrganizationId_OwnerId",
                        columns: x => new { x.OrganizationId, x.OwnerId },
                        principalSchema: "operations",
                        principalTable: "Owners",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PropertyOwnerships_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                DO $$ DECLARE t text; BEGIN
                  FOREACH t IN ARRAY ARRAY['Budgets','BudgetLines','Owners','PropertyOwnerships','ManagementFeeRules','Distributions','OwnerStatements'] LOOP
                    EXECUTE format('ALTER TABLE operations."%s" ENABLE ROW LEVEL SECURITY', t);
                    EXECUTE format('ALTER TABLE operations."%s" FORCE ROW LEVEL SECURITY', t);
                    EXECUTE format('CREATE POLICY tenant_isolation ON operations."%s" USING ("OrganizationId" = nullif(current_setting(''app.organization_id'', true), '''')::uuid) WITH CHECK ("OrganizationId" = nullif(current_setting(''app.organization_id'', true), '''')::uuid)', t);
                  END LOOP;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLines_OrganizationId_AccountId",
                schema: "operations",
                table: "BudgetLines",
                columns: new[] { "OrganizationId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLines_OrganizationId_BudgetId_AccountId_Month",
                schema: "operations",
                table: "BudgetLines",
                columns: new[] { "OrganizationId", "BudgetId", "AccountId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_OrganizationId_PropertyId_Year",
                schema: "operations",
                table: "Budgets",
                columns: new[] { "OrganizationId", "PropertyId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Distributions_OrganizationId_OwnerId",
                schema: "operations",
                table: "Distributions",
                columns: new[] { "OrganizationId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Distributions_OrganizationId_PropertyId",
                schema: "operations",
                table: "Distributions",
                columns: new[] { "OrganizationId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_ManagementFeeRules_OrganizationId_PropertyId",
                schema: "operations",
                table: "ManagementFeeRules",
                columns: new[] { "OrganizationId", "PropertyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Owners_OrganizationId_Email",
                schema: "operations",
                table: "Owners",
                columns: new[] { "OrganizationId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerStatements_OrganizationId_OwnerId_PropertyId_StartsOn_~",
                schema: "operations",
                table: "OwnerStatements",
                columns: new[] { "OrganizationId", "OwnerId", "PropertyId", "StartsOn", "EndsOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerStatements_OrganizationId_PropertyId",
                schema: "operations",
                table: "OwnerStatements",
                columns: new[] { "OrganizationId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PropertyOwnerships_OrganizationId_OwnerId_PropertyId",
                schema: "operations",
                table: "PropertyOwnerships",
                columns: new[] { "OrganizationId", "OwnerId", "PropertyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropertyOwnerships_OrganizationId_PropertyId",
                schema: "operations",
                table: "PropertyOwnerships",
                columns: new[] { "OrganizationId", "PropertyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BudgetLines",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Distributions",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "ManagementFeeRules",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "OwnerStatements",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "PropertyOwnerships",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Budgets",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Owners",
                schema: "operations");

            migrationBuilder.DropColumn(
                name: "PropertyId",
                schema: "operations",
                table: "JournalLines");
        }
    }
}
