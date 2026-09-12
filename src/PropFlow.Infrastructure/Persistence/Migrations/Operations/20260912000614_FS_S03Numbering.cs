using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S03Numbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayNumber",
                schema: "operations",
                table: "WorkItems",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NumberingSequences",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliesTo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    NextValue = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberingSequences", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_OrganizationId_DisplayNumber",
                schema: "operations",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "DisplayNumber" },
                unique: true,
                filter: "\"DisplayNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NumberingSequences_OrganizationId_AppliesTo",
                schema: "operations",
                table: "NumberingSequences",
                columns: new[] { "OrganizationId", "AppliesTo" },
                unique: true);

            migrationBuilder.Sql("""
            ALTER TABLE operations."NumberingSequences"
              ADD CONSTRAINT "FK_NumberingSequences_Organization" FOREIGN KEY ("OrganizationId")
              REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
            ALTER TABLE operations."NumberingSequences" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."NumberingSequences" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON operations."NumberingSequences"
              USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
              WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DROP POLICY tenant_isolation ON operations."NumberingSequences";
            ALTER TABLE operations."NumberingSequences" DISABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."NumberingSequences" DROP CONSTRAINT "FK_NumberingSequences_Organization";
            """);
            migrationBuilder.DropTable(
                name: "NumberingSequences",
                schema: "operations");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_OrganizationId_DisplayNumber",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "DisplayNumber",
                schema: "operations",
                table: "WorkItems");
        }
    }
}
