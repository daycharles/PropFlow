using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S03Approvals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalRequests",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRequests", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_OrganizationId_Status",
                schema: "operations",
                table: "ApprovalRequests",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_OrganizationId_SubjectType_SubjectId",
                schema: "operations",
                table: "ApprovalRequests",
                columns: new[] { "OrganizationId", "SubjectType", "SubjectId" });

            migrationBuilder.Sql("""
            ALTER TABLE operations."ApprovalRequests"
              ADD CONSTRAINT "FK_ApprovalRequests_Organization" FOREIGN KEY ("OrganizationId")
              REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
            ALTER TABLE operations."ApprovalRequests" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."ApprovalRequests" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON operations."ApprovalRequests"
              USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
              WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DROP POLICY tenant_isolation ON operations."ApprovalRequests";
            ALTER TABLE operations."ApprovalRequests" DISABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."ApprovalRequests" DROP CONSTRAINT "FK_ApprovalRequests_Organization";
            """);
            migrationBuilder.DropTable(
                name: "ApprovalRequests",
                schema: "operations");
        }
    }
}
