using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class AttachmentsFinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Attachments",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ResidentVisible = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Attachments_WorkItems_OrganizationId_WorkId",
                        columns: x => new { x.OrganizationId, x.WorkId },
                        principalSchema: "operations",
                        principalTable: "WorkItems",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_OrganizationId_RetainUntil",
                schema: "operations",
                table: "Attachments",
                columns: new[] { "OrganizationId", "RetainUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_OrganizationId_WorkId_CreatedAt",
                schema: "operations",
                table: "Attachments",
                columns: new[] { "OrganizationId", "WorkId", "CreatedAt" });

            migrationBuilder.Sql("""
                ALTER TABLE operations."Attachments" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."Attachments" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON operations."Attachments"
                  USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
                  WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON operations.\"Attachments\"; ALTER TABLE operations.\"Attachments\" DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.DropTable(
                name: "Attachments",
                schema: "operations");
        }
    }
}
