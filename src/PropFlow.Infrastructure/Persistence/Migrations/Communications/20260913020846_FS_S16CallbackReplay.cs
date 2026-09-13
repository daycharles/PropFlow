using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Communications
{
    /// <inheritdoc />
    public partial class FS_S16CallbackReplay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Subject",
                schema: "communications",
                table: "Campaigns",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateTable(
                name: "ProviderCallbackReceipts",
                schema: "communications",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BodyDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCallbackReceipts", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCallbackReceipts_OrganizationId_BodyDigest",
                schema: "communications",
                table: "ProviderCallbackReceipts",
                columns: new[] { "OrganizationId", "BodyDigest" },
                unique: true);
            migrationBuilder.Sql("ALTER TABLE communications.\"ProviderCallbackReceipts\" ENABLE ROW LEVEL SECURITY; ALTER TABLE communications.\"ProviderCallbackReceipts\" FORCE ROW LEVEL SECURITY; CREATE POLICY tenant_isolation ON communications.\"ProviderCallbackReceipts\" USING (\"OrganizationId\" = current_setting('app.organization_id')::uuid) WITH CHECK (\"OrganizationId\" = current_setting('app.organization_id')::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON communications.\"ProviderCallbackReceipts\"; ALTER TABLE communications.\"ProviderCallbackReceipts\" DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.DropTable(
                name: "ProviderCallbackReceipts",
                schema: "communications");

            migrationBuilder.AlterColumn<string>(
                name: "Subject",
                schema: "communications",
                table: "Campaigns",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
