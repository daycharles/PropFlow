using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S03CustomFieldDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomFieldDefinitions",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AppliesTo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FieldType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Options = table.Column<string>(type: "jsonb", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFieldDefinitions", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomFieldDefinitions_OrganizationId_AppliesTo_Key",
                schema: "operations",
                table: "CustomFieldDefinitions",
                columns: new[] { "OrganizationId", "AppliesTo", "Key" },
                unique: true);

            migrationBuilder.Sql("""
            ALTER TABLE operations."CustomFieldDefinitions"
              ADD CONSTRAINT "FK_CustomFieldDefinitions_Organization" FOREIGN KEY ("OrganizationId")
              REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
            ALTER TABLE operations."CustomFieldDefinitions" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."CustomFieldDefinitions" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON operations."CustomFieldDefinitions"
              USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
              WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DROP POLICY tenant_isolation ON operations."CustomFieldDefinitions";
            ALTER TABLE operations."CustomFieldDefinitions" DISABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."CustomFieldDefinitions" DROP CONSTRAINT "FK_CustomFieldDefinitions_Organization";
            """);
            migrationBuilder.DropTable(
                name: "CustomFieldDefinitions",
                schema: "operations");
        }
    }
}
