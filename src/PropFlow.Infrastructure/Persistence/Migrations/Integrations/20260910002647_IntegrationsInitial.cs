using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Integrations
{
    /// <inheritdoc />
    public partial class IntegrationsInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integrations");

            migrationBuilder.CreateTable(
                name: "Connections",
                schema: "integrations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastAttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSucceededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connections", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "RecordLinks",
                schema: "integrations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SyncState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordLinks", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_RecordLinks_Connections_OrganizationId_ConnectionId",
                        columns: x => new { x.OrganizationId, x.ConnectionId },
                        principalSchema: "integrations",
                        principalTable: "Connections",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Connections_OrganizationId_SourceSystem",
                schema: "integrations",
                table: "Connections",
                columns: new[] { "OrganizationId", "SourceSystem" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordLinks_OrganizationId_ConnectionId_Kind_ExternalId",
                schema: "integrations",
                table: "RecordLinks",
                columns: new[] { "OrganizationId", "ConnectionId", "Kind", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordLinks_OrganizationId_ConnectionId_SyncState",
                schema: "integrations",
                table: "RecordLinks",
                columns: new[] { "OrganizationId", "ConnectionId", "SyncState" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecordLinks",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "Connections",
                schema: "integrations");
        }
    }
}
