using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S02PropertyDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PropertyDocuments",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DocumentUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyDocuments", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PropertyDocuments_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PropertyDocuments_OrganizationId_PropertyId_CreatedAt",
                schema: "operations",
                table: "PropertyDocuments",
                columns: new[] { "OrganizationId", "PropertyId", "CreatedAt" });

            migrationBuilder.Sql("""
            ALTER TABLE operations."PropertyDocuments"
              ADD CONSTRAINT "FK_PropertyDocuments_Organization" FOREIGN KEY ("OrganizationId")
              REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
            ALTER TABLE operations."PropertyDocuments" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."PropertyDocuments" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON operations."PropertyDocuments"
              USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
              WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DROP POLICY tenant_isolation ON operations."PropertyDocuments";
            ALTER TABLE operations."PropertyDocuments" DISABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."PropertyDocuments" DROP CONSTRAINT "FK_PropertyDocuments_Organization";
            """);
            migrationBuilder.DropTable(
                name: "PropertyDocuments",
                schema: "operations");
        }
    }
}
