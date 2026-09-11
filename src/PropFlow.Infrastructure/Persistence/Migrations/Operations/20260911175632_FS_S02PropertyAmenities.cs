using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S02PropertyAmenities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PropertyAmenities",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyAmenities", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PropertyAmenities_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PropertyAmenities_OrganizationId_PropertyId_Name",
                schema: "operations",
                table: "PropertyAmenities",
                columns: new[] { "OrganizationId", "PropertyId", "Name" },
                unique: true);

            migrationBuilder.Sql("""
            ALTER TABLE operations."PropertyAmenities"
              ADD CONSTRAINT "FK_PropertyAmenities_Organization" FOREIGN KEY ("OrganizationId")
              REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
            ALTER TABLE operations."PropertyAmenities" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."PropertyAmenities" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON operations."PropertyAmenities"
              USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
              WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DROP POLICY tenant_isolation ON operations."PropertyAmenities";
            ALTER TABLE operations."PropertyAmenities" DISABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."PropertyAmenities" DROP CONSTRAINT "FK_PropertyAmenities_Organization";
            """);
            migrationBuilder.DropTable(
                name: "PropertyAmenities",
                schema: "operations");
        }
    }
}
