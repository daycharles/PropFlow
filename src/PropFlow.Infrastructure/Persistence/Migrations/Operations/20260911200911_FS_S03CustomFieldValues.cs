using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S03CustomFieldValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomFieldValues",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomFieldDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFieldValues", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_CustomFieldValues_CustomFieldDefinitions_OrganizationId_Cus~",
                        columns: x => new { x.OrganizationId, x.CustomFieldDefinitionId },
                        principalSchema: "operations",
                        principalTable: "CustomFieldDefinitions",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomFieldValues_WorkItems_OrganizationId_WorkId",
                        columns: x => new { x.OrganizationId, x.WorkId },
                        principalSchema: "operations",
                        principalTable: "WorkItems",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomFieldValues_OrganizationId_CustomFieldDefinitionId",
                schema: "operations",
                table: "CustomFieldValues",
                columns: new[] { "OrganizationId", "CustomFieldDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomFieldValues_OrganizationId_WorkId_CustomFieldDefiniti~",
                schema: "operations",
                table: "CustomFieldValues",
                columns: new[] { "OrganizationId", "WorkId", "CustomFieldDefinitionId" },
                unique: true);

            migrationBuilder.Sql("""
            ALTER TABLE operations."CustomFieldValues"
              ADD CONSTRAINT "FK_CustomFieldValues_Organization" FOREIGN KEY ("OrganizationId")
              REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
            ALTER TABLE operations."CustomFieldValues" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."CustomFieldValues" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON operations."CustomFieldValues"
              USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
              WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DROP POLICY tenant_isolation ON operations."CustomFieldValues";
            ALTER TABLE operations."CustomFieldValues" DISABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."CustomFieldValues" DROP CONSTRAINT "FK_CustomFieldValues_Organization";
            """);
            migrationBuilder.DropTable(
                name: "CustomFieldValues",
                schema: "operations");
        }
    }
}
