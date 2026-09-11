using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S02PropertyContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PropertyContacts",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyContacts", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PropertyContacts_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PropertyContacts_OrganizationId_PropertyId",
                schema: "operations",
                table: "PropertyContacts",
                columns: new[] { "OrganizationId", "PropertyId" });

            migrationBuilder.Sql("""
            ALTER TABLE operations."PropertyContacts"
              ADD CONSTRAINT "FK_PropertyContacts_Organization" FOREIGN KEY ("OrganizationId")
              REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
            ALTER TABLE operations."PropertyContacts" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."PropertyContacts" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON operations."PropertyContacts"
              USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
              WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DROP POLICY tenant_isolation ON operations."PropertyContacts";
            ALTER TABLE operations."PropertyContacts" DISABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."PropertyContacts" DROP CONSTRAINT "FK_PropertyContacts_Organization";
            """);
            migrationBuilder.DropTable(
                name: "PropertyContacts",
                schema: "operations");
        }
    }
}
