using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S04Applicants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Applicants",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    InquiryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProspectName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applicants", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Applicants_Inquiries_OrganizationId_InquiryId",
                        columns: x => new { x.OrganizationId, x.InquiryId },
                        principalSchema: "operations",
                        principalTable: "Inquiries",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Applicants_Listings_OrganizationId_ListingId",
                        columns: x => new { x.OrganizationId, x.ListingId },
                        principalSchema: "operations",
                        principalTable: "Listings",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Applicants_OrganizationId_InquiryId",
                schema: "operations",
                table: "Applicants",
                columns: new[] { "OrganizationId", "InquiryId" },
                unique: true);

            migrationBuilder.Sql("""
            ALTER TABLE operations."Applicants"
              ADD CONSTRAINT "FK_Applicants_Organization" FOREIGN KEY ("OrganizationId")
              REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
            ALTER TABLE operations."Applicants" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."Applicants" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON operations."Applicants"
              USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
              WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
            """);

            migrationBuilder.CreateIndex(
                name: "IX_Applicants_OrganizationId_ListingId_Status",
                schema: "operations",
                table: "Applicants",
                columns: new[] { "OrganizationId", "ListingId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DROP POLICY tenant_isolation ON operations."Applicants";
            ALTER TABLE operations."Applicants" DISABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."Applicants" DROP CONSTRAINT "FK_Applicants_Organization";
            """);
            migrationBuilder.DropTable(
                name: "Applicants",
                schema: "operations");
        }
    }
}
