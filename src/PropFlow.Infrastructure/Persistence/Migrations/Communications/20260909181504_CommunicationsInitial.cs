using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Communications
{
    /// <inheritdoc />
    public partial class CommunicationsInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "communications");

            migrationBuilder.CreateTable(
                name: "MessageTemplates",
                schema: "communications",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageTemplates", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "communications",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RecipientAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageTemplates_OrganizationId_Name",
                schema: "communications",
                table: "MessageTemplates",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_OrganizationId_IdempotencyKey",
                schema: "communications",
                table: "OutboxMessages",
                columns: new[] { "OrganizationId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_OrganizationId_Status",
                schema: "communications",
                table: "OutboxMessages",
                columns: new[] { "OrganizationId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageTemplates",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "communications");
        }
    }
}
