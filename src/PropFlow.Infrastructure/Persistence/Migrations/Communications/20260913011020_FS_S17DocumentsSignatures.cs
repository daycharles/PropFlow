using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Communications
{
    /// <inheritdoc />
    public partial class FS_S17DocumentsSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTemplates",
                schema: "communications",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTemplates", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "DocumentPackets",
                schema: "communications",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RenderedContent = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                    IntegrityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentPackets", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_DocumentPackets_DocumentTemplates_OrganizationId_TemplateId",
                        columns: x => new { x.OrganizationId, x.TemplateId },
                        principalSchema: "communications",
                        principalTable: "DocumentTemplates",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SignatureRequests",
                schema: "communications",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PacketId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SignerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EvidenceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureRequests", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_SignatureRequests_DocumentPackets_OrganizationId_PacketId",
                        columns: x => new { x.OrganizationId, x.PacketId },
                        principalSchema: "communications",
                        principalTable: "DocumentPackets",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPackets_OrganizationId_Status_ExpiresAt",
                schema: "communications",
                table: "DocumentPackets",
                columns: new[] { "OrganizationId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPackets_OrganizationId_TemplateId",
                schema: "communications",
                table: "DocumentPackets",
                columns: new[] { "OrganizationId", "TemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplates_OrganizationId_Name",
                schema: "communications",
                table: "DocumentTemplates",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_OrganizationId_PacketId_SignerId",
                schema: "communications",
                table: "SignatureRequests",
                columns: new[] { "OrganizationId", "PacketId", "SignerId" },
                unique: true);
            foreach (var table in new[] { "DocumentTemplates", "DocumentPackets", "SignatureRequests" })
                migrationBuilder.Sql($"ALTER TABLE communications.\"{table}\" ENABLE ROW LEVEL SECURITY; ALTER TABLE communications.\"{table}\" FORCE ROW LEVEL SECURITY; CREATE POLICY tenant_isolation ON communications.\"{table}\" USING (\"OrganizationId\" = current_setting('app.current_organization')::uuid) WITH CHECK (\"OrganizationId\" = current_setting('app.current_organization')::uuid);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "DocumentTemplates", "DocumentPackets", "SignatureRequests" })
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON communications.\"{table}\"; ALTER TABLE communications.\"{table}\" DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.DropTable(
                name: "SignatureRequests",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "DocumentPackets",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "DocumentTemplates",
                schema: "communications");
        }
    }
}
