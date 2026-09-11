using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S13S15Operations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetLifecycleCosts",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IncurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetLifecycleCosts", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_AssetLifecycleCosts_Assets_OrganizationId_AssetId",
                        columns: x => new { x.OrganizationId, x.AssetId },
                        principalSchema: "operations",
                        principalTable: "Assets",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetLifecycleCosts_WorkItems_OrganizationId_WorkItemId",
                        columns: x => new { x.OrganizationId, x.WorkItemId },
                        principalSchema: "operations",
                        principalTable: "WorkItems",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ComplianceObligations",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EscalationDays = table.Column<int>(type: "integer", nullable: false),
                    Recurrence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RecurrenceEndOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SatisfiedOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceObligations", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_ComplianceObligations_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Incidents",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Incidents_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MeterReadings",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeterName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Reading = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReadOn = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeterReadings", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_MeterReadings_Assets_OrganizationId_AssetId",
                        columns: x => new { x.OrganizationId, x.AssetId },
                        principalSchema: "operations",
                        principalTable: "Assets",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PreventiveMaintenancePlans",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Recurrence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FirstDueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreventiveMaintenancePlans", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PreventiveMaintenancePlans_Assets_OrganizationId_AssetId",
                        columns: x => new { x.OrganizationId, x.AssetId },
                        principalSchema: "operations",
                        principalTable: "Assets",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Remediations",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViolationId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CompletedOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Remediations", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Remediations_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Violations",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IdentifiedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResolvedOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Violations", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Violations_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncidentAudit",
                schema: "operations",
                columns: table => new
                {
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IncidentOrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentAudit", x => new { x.OrganizationId, x.IncidentId, x.OccurredAt, x.Action });
                    table.ForeignKey(
                        name: "FK_IncidentAudit_Incidents_IncidentOrganizationId_IncidentId",
                        columns: x => new { x.IncidentOrganizationId, x.IncidentId },
                        principalSchema: "operations",
                        principalTable: "Incidents",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetLifecycleCosts_OrganizationId_AssetId_IncurredOn",
                schema: "operations",
                table: "AssetLifecycleCosts",
                columns: new[] { "OrganizationId", "AssetId", "IncurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetLifecycleCosts_OrganizationId_WorkItemId",
                schema: "operations",
                table: "AssetLifecycleCosts",
                columns: new[] { "OrganizationId", "WorkItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceObligations_OrganizationId_PropertyId_Status_DueOn",
                schema: "operations",
                table: "ComplianceObligations",
                columns: new[] { "OrganizationId", "PropertyId", "Status", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAudit_IncidentOrganizationId_IncidentId",
                schema: "operations",
                table: "IncidentAudit",
                columns: new[] { "IncidentOrganizationId", "IncidentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_OrganizationId_PropertyId_Status_OccurredAt",
                schema: "operations",
                table: "Incidents",
                columns: new[] { "OrganizationId", "PropertyId", "Status", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MeterReadings_OrganizationId_AssetId_MeterName_ReadOn",
                schema: "operations",
                table: "MeterReadings",
                columns: new[] { "OrganizationId", "AssetId", "MeterName", "ReadOn" });

            migrationBuilder.CreateIndex(
                name: "IX_PreventiveMaintenancePlans_OrganizationId_AssetId_IsActive",
                schema: "operations",
                table: "PreventiveMaintenancePlans",
                columns: new[] { "OrganizationId", "AssetId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Remediations_OrganizationId_PropertyId_Status_DueOn",
                schema: "operations",
                table: "Remediations",
                columns: new[] { "OrganizationId", "PropertyId", "Status", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Violations_OrganizationId_PropertyId_Status",
                schema: "operations",
                table: "Violations",
                columns: new[] { "OrganizationId", "PropertyId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetLifecycleCosts",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "ComplianceObligations",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "IncidentAudit",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "MeterReadings",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "PreventiveMaintenancePlans",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Remediations",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Violations",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Incidents",
                schema: "operations");
        }
    }
}
