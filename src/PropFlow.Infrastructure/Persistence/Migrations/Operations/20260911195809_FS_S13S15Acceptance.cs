using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S13S15Acceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComplianceEvidence",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttachmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ViolationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LegalHold = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceEvidence", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ComplianceOccurrences",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObligationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceOccurrences", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "PreventiveMaintenanceOccurrences",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurrenceKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreventiveMaintenanceOccurrences", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PreventiveMaintenanceOccurrences_Assets_OrganizationId_Asse~",
                        columns: x => new { x.OrganizationId, x.AssetId },
                        principalSchema: "operations",
                        principalTable: "Assets",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PreventiveMaintenanceOccurrences_PreventiveMaintenancePlans~",
                        columns: x => new { x.OrganizationId, x.PlanId },
                        principalSchema: "operations",
                        principalTable: "PreventiveMaintenancePlans",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PreventiveMaintenanceOccurrences_WorkItems_OrganizationId_W~",
                        columns: x => new { x.OrganizationId, x.WorkItemId },
                        principalSchema: "operations",
                        principalTable: "WorkItems",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PreventiveMaintenanceOccurrences_OrganizationId_AssetId_Due~",
                schema: "operations",
                table: "PreventiveMaintenanceOccurrences",
                columns: new[] { "OrganizationId", "AssetId", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_PreventiveMaintenanceOccurrences_OrganizationId_OccurrenceK~",
                schema: "operations",
                table: "PreventiveMaintenanceOccurrences",
                columns: new[] { "OrganizationId", "OccurrenceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PreventiveMaintenanceOccurrences_OrganizationId_PlanId",
                schema: "operations",
                table: "PreventiveMaintenanceOccurrences",
                columns: new[] { "OrganizationId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_PreventiveMaintenanceOccurrences_OrganizationId_WorkItemId",
                schema: "operations",
                table: "PreventiveMaintenanceOccurrences",
                columns: new[] { "OrganizationId", "WorkItemId" });
            foreach (var table in new[] { "ComplianceEvidence", "ComplianceOccurrences", "PreventiveMaintenanceOccurrences" })
                migrationBuilder.Sql($"ALTER TABLE operations.\"{table}\" ENABLE ROW LEVEL SECURITY; ALTER TABLE operations.\"{table}\" FORCE ROW LEVEL SECURITY; CREATE POLICY tenant_isolation ON operations.\"{table}\" USING (\"OrganizationId\" = nullif(current_setting('app.organization_id', true), '')::uuid) WITH CHECK (\"OrganizationId\" = nullif(current_setting('app.organization_id', true), '')::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "ComplianceEvidence", "ComplianceOccurrences", "PreventiveMaintenanceOccurrences" })
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON operations.\"{table}\";");
            migrationBuilder.DropTable(
                name: "ComplianceEvidence",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "ComplianceOccurrences",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "PreventiveMaintenanceOccurrences",
                schema: "operations");
        }
    }
}
