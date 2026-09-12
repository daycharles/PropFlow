using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations;

public partial class FS_S14OperationsSecurity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "InspectionTemplates", "Inspections", "InspectionFindings", "UnitTurns", "UnitTurnTasks", "VendorProfiles", "VendorDocuments", "VendorContracts", "VendorRateCards", "ProcurementBids", "PurchaseOrders", "WorkAuthorizations", "VendorPerformanceReviews", "PurchaseOrderInvoiceMatches" })
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

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "InspectionTemplates", "Inspections", "InspectionFindings", "UnitTurns", "UnitTurnTasks", "VendorProfiles", "VendorDocuments", "VendorContracts", "VendorRateCards", "ProcurementBids", "PurchaseOrders", "WorkAuthorizations", "VendorPerformanceReviews", "PurchaseOrderInvoiceMatches" })
            migrationBuilder.Sql($"""
                DROP POLICY tenant_isolation ON operations."{table}";
                ALTER TABLE operations."{table}" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."{table}" DROP CONSTRAINT "FK_{table}_Organization";
                """);
    }
}
