using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Integrations
{
    /// <inheritdoc />
    /// <remarks>
    /// PF-S19.04. The reconciliation tables, plus the RecordLinks columns the reconciler needs.
    /// <para>
    /// The scaffolder warns about possible data loss for the ExternalId AlterColumn. It is a
    /// widening, character varying(200) -&gt; character varying(256), so nothing can be truncated.
    /// The widening exists because SyntheticExternalId.ForResident appends "#resident" to a source
    /// id, and a full-width id would otherwise become unstorable. Only the Down direction narrows,
    /// and it would fail on a synthetic resident id longer than 200 characters — correctly, since
    /// the old column genuinely cannot hold one.
    /// </para>
    /// <para>
    /// Two of the indexes below are partial uniques and neither is decoration.
    /// IX_Conflicts_OpenDivergence is what makes a re-detected divergence update the existing open
    /// row instead of inserting a second one; IX_SyncRuns_ActiveClaim is what makes SyncRun.Begin's
    /// insert-then-save an actual claim rather than a hopeful one.
    /// </para>
    /// </remarks>
    public partial class IntegrationsReconciliation : Migration
    {
        // Standard tenant isolation, identical to IntegrationsTenantSecurity: an organization
        // foreign key, ENABLE plus FORCE row level security, and a policy bound to
        // app.organization_id. ENABLE turns policies on; FORCE applies them to the table owner as
        // well. Both are required, neither is redundant, and DatabaseReadiness fails the readiness
        // check if either is missing.
        private static readonly string[] NewTables =
            ["MappingProfiles", "MappingRules", "SyncRuns", "Conflicts"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ExternalId",
                schema: "integrations",
                table: "RecordLinks",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<Guid>(
                name: "InternalId",
                schema: "integrations",
                table: "RecordLinks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastReconciledAt",
                schema: "integrations",
                table: "RecordLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastRunId",
                schema: "integrations",
                table: "RecordLinks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReconciledHash",
                schema: "integrations",
                table: "RecordLinks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetiredAt",
                schema: "integrations",
                table: "RecordLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RetiredByUserId",
                schema: "integrations",
                table: "RecordLinks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Conflicts",
                schema: "integrations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ObservedValue = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CurrentValue = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FirstSeenInRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSeenInRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ObservationCount = table.Column<int>(type: "integer", nullable: false),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conflicts", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Conflicts_Connections_OrganizationId_ConnectionId",
                        columns: x => new { x.OrganizationId, x.ConnectionId },
                        principalSchema: "integrations",
                        principalTable: "Connections",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MappingProfiles",
                schema: "integrations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetPortfolioId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultCreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultTimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingProfiles", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_MappingProfiles_Connections_OrganizationId_ConnectionId",
                        columns: x => new { x.OrganizationId, x.ConnectionId },
                        principalSchema: "integrations",
                        principalTable: "Connections",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncRuns",
                schema: "integrations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HeartbeatAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Seen = table.Column<int>(type: "integer", nullable: false),
                    Added = table.Column<int>(type: "integer", nullable: false),
                    Updated = table.Column<int>(type: "integer", nullable: false),
                    Failed = table.Column<int>(type: "integer", nullable: false),
                    Conflicted = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRuns", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_SyncRuns_Connections_OrganizationId_ConnectionId",
                        columns: x => new { x.OrganizationId, x.ConnectionId },
                        principalSchema: "integrations",
                        principalTable: "Connections",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MappingRules",
                schema: "integrations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceField = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TargetValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingRules", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_MappingRules_MappingProfiles_OrganizationId_ProfileId",
                        columns: x => new { x.OrganizationId, x.ProfileId },
                        principalSchema: "integrations",
                        principalTable: "MappingProfiles",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecordLinks_OrganizationId_ConnectionId_LastRunId",
                schema: "integrations",
                table: "RecordLinks",
                columns: new[] { "OrganizationId", "ConnectionId", "LastRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecordLinks_OrganizationId_Kind_InternalId",
                schema: "integrations",
                table: "RecordLinks",
                columns: new[] { "OrganizationId", "Kind", "InternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Conflicts_OpenDivergence",
                schema: "integrations",
                table: "Conflicts",
                columns: new[] { "OrganizationId", "ConnectionId", "Kind", "ExternalId", "Reason", "Field" },
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_Conflicts_OrganizationId_ConnectionId_Status_LastSeenAt",
                schema: "integrations",
                table: "Conflicts",
                columns: new[] { "OrganizationId", "ConnectionId", "Status", "LastSeenAt" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Conflicts_OrganizationId_LastSeenInRunId",
                schema: "integrations",
                table: "Conflicts",
                columns: new[] { "OrganizationId", "LastSeenInRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_MappingProfiles_OrganizationId_ConnectionId_Kind",
                schema: "integrations",
                table: "MappingProfiles",
                columns: new[] { "OrganizationId", "ConnectionId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MappingRules_OrganizationId_ProfileId_SourceField",
                schema: "integrations",
                table: "MappingRules",
                columns: new[] { "OrganizationId", "ProfileId", "SourceField" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncRuns_ActiveClaim",
                schema: "integrations",
                table: "SyncRuns",
                columns: new[] { "OrganizationId", "ConnectionId" },
                unique: true,
                filter: "\"Status\" = 'Running'");

            migrationBuilder.CreateIndex(
                name: "IX_SyncRuns_OrganizationId_ConnectionId_StartedAt",
                schema: "integrations",
                table: "SyncRuns",
                columns: new[] { "OrganizationId", "ConnectionId", "StartedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SyncRuns_OrganizationId_Status_HeartbeatAt",
                schema: "integrations",
                table: "SyncRuns",
                columns: new[] { "OrganizationId", "Status", "HeartbeatAt" });

            foreach (var table in NewTables)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE integrations."{table}"
                      ADD CONSTRAINT "FK_{table}_Organization" FOREIGN KEY ("OrganizationId")
                      REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
                    ALTER TABLE integrations."{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE integrations."{table}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON integrations."{table}"
                      USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
                      WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in NewTables)
            {
                migrationBuilder.Sql($"""
                    DROP POLICY tenant_isolation ON integrations."{table}";
                    ALTER TABLE integrations."{table}" DISABLE ROW LEVEL SECURITY;
                    ALTER TABLE integrations."{table}" DROP CONSTRAINT "FK_{table}_Organization";
                    """);
            }

            migrationBuilder.DropTable(
                name: "Conflicts",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "MappingRules",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "SyncRuns",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "MappingProfiles",
                schema: "integrations");

            migrationBuilder.DropIndex(
                name: "IX_RecordLinks_OrganizationId_ConnectionId_LastRunId",
                schema: "integrations",
                table: "RecordLinks");

            migrationBuilder.DropIndex(
                name: "IX_RecordLinks_OrganizationId_Kind_InternalId",
                schema: "integrations",
                table: "RecordLinks");

            migrationBuilder.DropColumn(
                name: "InternalId",
                schema: "integrations",
                table: "RecordLinks");

            migrationBuilder.DropColumn(
                name: "LastReconciledAt",
                schema: "integrations",
                table: "RecordLinks");

            migrationBuilder.DropColumn(
                name: "LastRunId",
                schema: "integrations",
                table: "RecordLinks");

            migrationBuilder.DropColumn(
                name: "ReconciledHash",
                schema: "integrations",
                table: "RecordLinks");

            migrationBuilder.DropColumn(
                name: "RetiredAt",
                schema: "integrations",
                table: "RecordLinks");

            migrationBuilder.DropColumn(
                name: "RetiredByUserId",
                schema: "integrations",
                table: "RecordLinks");

            migrationBuilder.AlterColumn<string>(
                name: "ExternalId",
                schema: "integrations",
                table: "RecordLinks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);
        }
    }
}
