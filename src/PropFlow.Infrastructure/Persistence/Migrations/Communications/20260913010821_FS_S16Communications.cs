using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Communications
{
    /// <inheritdoc />
    public partial class FS_S16Communications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Campaigns",
                schema: "communications",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ChannelUnsubscribes",
                schema: "communications",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    UnsubscribedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelUnsubscribes", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                schema: "communications",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParticipantAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastMessageAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ConversationMessages",
                schema: "communications",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IntegrityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessages", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_ConversationMessages_Conversations_OrganizationId_Conversat~",
                        columns: x => new { x.OrganizationId, x.ConversationId },
                        principalSchema: "communications",
                        principalTable: "Conversations",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_OrganizationId_Name",
                schema: "communications",
                table: "Campaigns",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelUnsubscribes_OrganizationId_Channel_Address",
                schema: "communications",
                table: "ChannelUnsubscribes",
                columns: new[] { "OrganizationId", "Channel", "Address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_OrganizationId_ConversationId_Occurred~",
                schema: "communications",
                table: "ConversationMessages",
                columns: new[] { "OrganizationId", "ConversationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_OrganizationId_LastMessageAt",
                schema: "communications",
                table: "Conversations",
                columns: new[] { "OrganizationId", "LastMessageAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Campaigns",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "ChannelUnsubscribes",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "ConversationMessages",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "Conversations",
                schema: "communications");
        }
    }
}
