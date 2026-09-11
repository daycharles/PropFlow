using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S06LeaseDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaseDocuments",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DocumentUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ResidentVisible = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SignedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseDocuments", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaseDocuments_Leases_OrganizationId_LeaseId",
                        columns: x => new { x.OrganizationId, x.LeaseId },
                        principalSchema: "operations",
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseDocuments_OrganizationId_LeaseId_Status",
                schema: "operations",
                table: "LeaseDocuments",
                columns: new[] { "OrganizationId", "LeaseId", "Status" });

            migrationBuilder.Sql("""
            ALTER TABLE operations."LeaseDocuments"
              ADD CONSTRAINT "FK_LeaseDocuments_Organization" FOREIGN KEY ("OrganizationId")
              REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
            ALTER TABLE operations."LeaseDocuments" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."LeaseDocuments" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON operations."LeaseDocuments"
              USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
              WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DROP POLICY tenant_isolation ON operations."LeaseDocuments";
            ALTER TABLE operations."LeaseDocuments" DISABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."LeaseDocuments" DROP CONSTRAINT "FK_LeaseDocuments_Organization";
            """);
            migrationBuilder.DropTable(
                name: "LeaseDocuments",
                schema: "operations");
        }
    }
}
