using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S11S12Operations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InspectionTemplates",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ChecklistJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionTemplates", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ProcurementBids",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsSelected = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcurementBids", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_ProcurementBids_Vendors_OrganizationId_VendorId",
                        columns: x => new { x.OrganizationId, x.VendorId },
                        principalSchema: "operations",
                        principalTable: "Vendors",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementBids_WorkItems_OrganizationId_WorkItemId",
                        columns: x => new { x.OrganizationId, x.WorkItemId },
                        principalSchema: "operations",
                        principalTable: "WorkItems",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ApprovalThreshold = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Vendors_OrganizationId_VendorId",
                        columns: x => new { x.OrganizationId, x.VendorId },
                        principalSchema: "operations",
                        principalTable: "Vendors",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VendorContracts",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorContracts", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_VendorContracts_Vendors_OrganizationId_VendorId",
                        columns: x => new { x.OrganizationId, x.VendorId },
                        principalSchema: "operations",
                        principalTable: "Vendors",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendorDocuments",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorDocuments", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_VendorDocuments_Vendors_OrganizationId_VendorId",
                        columns: x => new { x.OrganizationId, x.VendorId },
                        principalSchema: "operations",
                        principalTable: "Vendors",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendorPerformanceReviews",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorPerformanceReviews", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_VendorPerformanceReviews_Vendors_OrganizationId_VendorId",
                        columns: x => new { x.OrganizationId, x.VendorId },
                        principalSchema: "operations",
                        principalTable: "Vendors",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendorProfiles",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorProfiles", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_VendorProfiles_Vendors_OrganizationId_VendorId",
                        columns: x => new { x.OrganizationId, x.VendorId },
                        principalSchema: "operations",
                        principalTable: "Vendors",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendorRateCards",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UnitRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorRateCards", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_VendorRateCards_Vendors_OrganizationId_VendorId",
                        columns: x => new { x.OrganizationId, x.VendorId },
                        principalSchema: "operations",
                        principalTable: "Vendors",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkAuthorizations",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkAuthorizations", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_WorkAuthorizations_Vendors_OrganizationId_VendorId",
                        columns: x => new { x.OrganizationId, x.VendorId },
                        principalSchema: "operations",
                        principalTable: "Vendors",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkAuthorizations_WorkItems_OrganizationId_WorkItemId",
                        columns: x => new { x.OrganizationId, x.WorkItemId },
                        principalSchema: "operations",
                        principalTable: "WorkItems",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Inspections",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inspections", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Inspections_InspectionTemplates_OrganizationId_TemplateId",
                        columns: x => new { x.OrganizationId, x.TemplateId },
                        principalSchema: "operations",
                        principalTable: "InspectionTemplates",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Inspections_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Inspections_Spaces_OrganizationId_SpaceId",
                        columns: x => new { x.OrganizationId, x.SpaceId },
                        principalSchema: "operations",
                        principalTable: "Spaces",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderInvoiceMatches",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayableInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderInvoiceMatches", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PurchaseOrderInvoiceMatches_PayableInvoices_OrganizationId_~",
                        columns: x => new { x.OrganizationId, x.PayableInvoiceId },
                        principalSchema: "operations",
                        principalTable: "PayableInvoices",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderInvoiceMatches_PurchaseOrders_OrganizationId_P~",
                        columns: x => new { x.OrganizationId, x.PurchaseOrderId },
                        principalSchema: "operations",
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InspectionFindings",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Area = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PhotoAttachmentIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionFindings", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_InspectionFindings_Inspections_OrganizationId_InspectionId",
                        columns: x => new { x.OrganizationId, x.InspectionId },
                        principalSchema: "operations",
                        principalTable: "Inspections",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UnitTurns",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    MoveOutInspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetReadyOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadyAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitTurns", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_UnitTurns_Inspections_OrganizationId_MoveOutInspectionId",
                        columns: x => new { x.OrganizationId, x.MoveOutInspectionId },
                        principalSchema: "operations",
                        principalTable: "Inspections",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnitTurns_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UnitTurns_Spaces_OrganizationId_SpaceId",
                        columns: x => new { x.OrganizationId, x.SpaceId },
                        principalSchema: "operations",
                        principalTable: "Spaces",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnitTurnTasks",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitTurnTasks", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_UnitTurnTasks_UnitTurns_OrganizationId_TurnId",
                        columns: x => new { x.OrganizationId, x.TurnId },
                        principalSchema: "operations",
                        principalTable: "UnitTurns",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UnitTurnTasks_WorkItems_OrganizationId_WorkId",
                        columns: x => new { x.OrganizationId, x.WorkId },
                        principalSchema: "operations",
                        principalTable: "WorkItems",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionFindings_OrganizationId_InspectionId_Status",
                schema: "operations",
                table: "InspectionFindings",
                columns: new[] { "OrganizationId", "InspectionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_OrganizationId_PropertyId_Status_CreatedAt",
                schema: "operations",
                table: "Inspections",
                columns: new[] { "OrganizationId", "PropertyId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_OrganizationId_SpaceId",
                schema: "operations",
                table: "Inspections",
                columns: new[] { "OrganizationId", "SpaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_OrganizationId_TemplateId",
                schema: "operations",
                table: "Inspections",
                columns: new[] { "OrganizationId", "TemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionTemplates_OrganizationId_Name_Version",
                schema: "operations",
                table: "InspectionTemplates",
                columns: new[] { "OrganizationId", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementBids_OrganizationId_VendorId",
                schema: "operations",
                table: "ProcurementBids",
                columns: new[] { "OrganizationId", "VendorId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementBids_OrganizationId_WorkItemId",
                schema: "operations",
                table: "ProcurementBids",
                columns: new[] { "OrganizationId", "WorkItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderInvoiceMatches_OrganizationId_PayableInvoiceId",
                schema: "operations",
                table: "PurchaseOrderInvoiceMatches",
                columns: new[] { "OrganizationId", "PayableInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderInvoiceMatches_OrganizationId_PurchaseOrderId_~",
                schema: "operations",
                table: "PurchaseOrderInvoiceMatches",
                columns: new[] { "OrganizationId", "PurchaseOrderId", "PayableInvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_OrganizationId_Number",
                schema: "operations",
                table: "PurchaseOrders",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_OrganizationId_PropertyId",
                schema: "operations",
                table: "PurchaseOrders",
                columns: new[] { "OrganizationId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_OrganizationId_VendorId",
                schema: "operations",
                table: "PurchaseOrders",
                columns: new[] { "OrganizationId", "VendorId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitTurns_OrganizationId_MoveOutInspectionId",
                schema: "operations",
                table: "UnitTurns",
                columns: new[] { "OrganizationId", "MoveOutInspectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitTurns_OrganizationId_PropertyId",
                schema: "operations",
                table: "UnitTurns",
                columns: new[] { "OrganizationId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitTurns_OrganizationId_SpaceId_Status",
                schema: "operations",
                table: "UnitTurns",
                columns: new[] { "OrganizationId", "SpaceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitTurnTasks_OrganizationId_TurnId_Sequence",
                schema: "operations",
                table: "UnitTurnTasks",
                columns: new[] { "OrganizationId", "TurnId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitTurnTasks_OrganizationId_WorkId",
                schema: "operations",
                table: "UnitTurnTasks",
                columns: new[] { "OrganizationId", "WorkId" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorContracts_OrganizationId_VendorId",
                schema: "operations",
                table: "VendorContracts",
                columns: new[] { "OrganizationId", "VendorId" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorDocuments_OrganizationId_VendorId_ExpiresOn",
                schema: "operations",
                table: "VendorDocuments",
                columns: new[] { "OrganizationId", "VendorId", "ExpiresOn" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorPerformanceReviews_OrganizationId_VendorId_ReviewedAt",
                schema: "operations",
                table: "VendorPerformanceReviews",
                columns: new[] { "OrganizationId", "VendorId", "ReviewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorProfiles_OrganizationId_VendorId",
                schema: "operations",
                table: "VendorProfiles",
                columns: new[] { "OrganizationId", "VendorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorRateCards_OrganizationId_VendorId_ServiceCode_Effecti~",
                schema: "operations",
                table: "VendorRateCards",
                columns: new[] { "OrganizationId", "VendorId", "ServiceCode", "EffectiveOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkAuthorizations_OrganizationId_VendorId",
                schema: "operations",
                table: "WorkAuthorizations",
                columns: new[] { "OrganizationId", "VendorId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkAuthorizations_OrganizationId_WorkItemId",
                schema: "operations",
                table: "WorkAuthorizations",
                columns: new[] { "OrganizationId", "WorkItemId" },
                unique: true);

            migrationBuilder.Sql("""
                DO $$
                DECLARE t text;
                BEGIN
                    FOREACH t IN ARRAY ARRAY['InspectionTemplates','Inspections','InspectionFindings','UnitTurns','UnitTurnTasks','VendorProfiles','VendorDocuments','VendorContracts','VendorRateCards','ProcurementBids','PurchaseOrders','WorkAuthorizations','VendorPerformanceReviews','PurchaseOrderInvoiceMatches'] LOOP
                        EXECUTE format('ALTER TABLE operations."%s" ENABLE ROW LEVEL SECURITY', t);
                        EXECUTE format('ALTER TABLE operations."%s" FORCE ROW LEVEL SECURITY', t);
                        EXECUTE format('CREATE POLICY tenant_isolation ON operations."%s" USING ("OrganizationId" = nullif(current_setting(''app.organization_id'', true), '''')::uuid) WITH CHECK ("OrganizationId" = nullif(current_setting(''app.organization_id'', true), '''')::uuid)', t);
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InspectionFindings",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "ProcurementBids",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "PurchaseOrderInvoiceMatches",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "UnitTurnTasks",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "VendorContracts",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "VendorDocuments",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "VendorPerformanceReviews",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "VendorProfiles",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "VendorRateCards",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "WorkAuthorizations",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "PurchaseOrders",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "UnitTurns",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Inspections",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "InspectionTemplates",
                schema: "operations");
        }
    }
}
