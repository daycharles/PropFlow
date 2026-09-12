using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class FS_S05Applications : Migration
    {
        // One migration rather than the FS-S09 tables-then-security split: these six tables are
        // new and carry no data, and DatabaseHealth fails readiness for any operations table
        // without forced RLS, so splitting would create an intermediate migration state that is
        // by definition un-healthy. FS_S03NotificationPreferences made the same call.
        private static readonly string[] Tables =
        [
            "RentalApplications", "ApplicationApplicants", "ScreeningRequests",
            "ApplicationConsents", "ScreeningResults", "ApplicationDecisions"
        ];

        // Consent, screening verdicts and decisions are adverse-action evidence: consent is
        // revoked by recording a new row and a decision is superseded by a new decision, so
        // neither is ever edited. This is the third rung, under OperationsStore.GuardWrites and
        // the GRANT SELECT, INSERT in DatabaseProvisioner — the only one of the three that a
        // careless verb on a shared GRANT line cannot silently undo.
        private static readonly string[] AppendOnlyTables =
            ["ApplicationConsents", "ScreeningResults", "ApplicationDecisions"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RentalApplications",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalApplications", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_RentalApplications_Listings_OrganizationId_ListingId",
                        columns: x => new { x.OrganizationId, x.ListingId },
                        principalSchema: "operations",
                        principalTable: "Listings",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationApplicants",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MonthlyIncome = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    EmploymentStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationApplicants", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_ApplicationApplicants_Applicants_OrganizationId_ApplicantId",
                        columns: x => new { x.OrganizationId, x.ApplicantId },
                        principalSchema: "operations",
                        principalTable: "Applicants",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApplicationApplicants_RentalApplications_OrganizationId_App~",
                        columns: x => new { x.OrganizationId, x.ApplicationId },
                        principalSchema: "operations",
                        principalTable: "RentalApplications",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationConsents",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsentType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationConsents", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_ApplicationConsents_Applicants_OrganizationId_ApplicantId",
                        columns: x => new { x.OrganizationId, x.ApplicantId },
                        principalSchema: "operations",
                        principalTable: "Applicants",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApplicationConsents_RentalApplications_OrganizationId_Appli~",
                        columns: x => new { x.OrganizationId, x.ApplicationId },
                        principalSchema: "operations",
                        principalTable: "RentalApplications",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationDecisions",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationDecisions", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_ApplicationDecisions_RentalApplications_OrganizationId_Appl~",
                        columns: x => new { x.OrganizationId, x.ApplicationId },
                        principalSchema: "operations",
                        principalTable: "RentalApplications",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningRequests",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningRequests", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_ScreeningRequests_Applicants_OrganizationId_ApplicantId",
                        columns: x => new { x.OrganizationId, x.ApplicantId },
                        principalSchema: "operations",
                        principalTable: "Applicants",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScreeningRequests_RentalApplications_OrganizationId_Applica~",
                        columns: x => new { x.OrganizationId, x.ApplicationId },
                        principalSchema: "operations",
                        principalTable: "RentalApplications",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningResults",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScreeningRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Recommendation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningResults", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_ScreeningResults_Applicants_OrganizationId_ApplicantId",
                        columns: x => new { x.OrganizationId, x.ApplicantId },
                        principalSchema: "operations",
                        principalTable: "Applicants",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScreeningResults_ScreeningRequests_OrganizationId_Screening~",
                        columns: x => new { x.OrganizationId, x.ScreeningRequestId },
                        principalSchema: "operations",
                        principalTable: "ScreeningRequests",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationApplicants_OrganizationId_ApplicantId",
                schema: "operations",
                table: "ApplicationApplicants",
                columns: new[] { "OrganizationId", "ApplicantId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationApplicants_OrganizationId_ApplicationId_Applican~",
                schema: "operations",
                table: "ApplicationApplicants",
                columns: new[] { "OrganizationId", "ApplicationId", "ApplicantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationConsents_OrganizationId_ApplicantId",
                schema: "operations",
                table: "ApplicationConsents",
                columns: new[] { "OrganizationId", "ApplicantId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationConsents_OrganizationId_ApplicationId_ApplicantI~",
                schema: "operations",
                table: "ApplicationConsents",
                columns: new[] { "OrganizationId", "ApplicationId", "ApplicantId", "ConsentType", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDecisions_OrganizationId_ApplicationId_DecidedAt",
                schema: "operations",
                table: "ApplicationDecisions",
                columns: new[] { "OrganizationId", "ApplicationId", "DecidedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RentalApplications_OrganizationId_ListingId_Status",
                schema: "operations",
                table: "RentalApplications",
                columns: new[] { "OrganizationId", "ListingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningRequests_OrganizationId_ApplicantId",
                schema: "operations",
                table: "ScreeningRequests",
                columns: new[] { "OrganizationId", "ApplicantId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningRequests_OrganizationId_ApplicationId_Status",
                schema: "operations",
                table: "ScreeningRequests",
                columns: new[] { "OrganizationId", "ApplicationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningRequests_OrganizationId_IdempotencyKey",
                schema: "operations",
                table: "ScreeningRequests",
                columns: new[] { "OrganizationId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningResults_OrganizationId_ApplicantId",
                schema: "operations",
                table: "ScreeningResults",
                columns: new[] { "OrganizationId", "ApplicantId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningResults_OrganizationId_ScreeningRequestId_Received~",
                schema: "operations",
                table: "ScreeningResults",
                columns: new[] { "OrganizationId", "ScreeningRequestId", "ReceivedAt" });

            // ENABLE turns the policies on; FORCE makes them apply to the table owner too.
            // Both are required and neither is redundant — DatabaseHealth checks
            // relrowsecurity AND relforcerowsecurity and fails readiness if either is missing.
            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE operations."{table}"
                      ADD CONSTRAINT "FK_{table}_Organization" FOREIGN KEY ("OrganizationId")
                      REFERENCES identity."Organizations" ("Id") ON DELETE RESTRICT;
                    ALTER TABLE operations."{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE operations."{table}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON operations."{table}"
                      USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
                      WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
                    """);
            }

            migrationBuilder.Sql("""
                CREATE FUNCTION operations.reject_application_record_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Application consents, screening results and decisions are append-only' USING ERRCODE = '55000';
                END; $$;
                """);
            foreach (var table in AppendOnlyTables)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER application_record_append_only BEFORE UPDATE OR DELETE ON operations."{table}"
                      FOR EACH ROW EXECUTE FUNCTION operations.reject_application_record_mutation();
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in AppendOnlyTables)
                migrationBuilder.Sql($"""DROP TRIGGER application_record_append_only ON operations."{table}";""");
            migrationBuilder.Sql("DROP FUNCTION operations.reject_application_record_mutation();");
            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"""
                    DROP POLICY tenant_isolation ON operations."{table}";
                    ALTER TABLE operations."{table}" DISABLE ROW LEVEL SECURITY;
                    ALTER TABLE operations."{table}" DROP CONSTRAINT "FK_{table}_Organization";
                    """);
            }

            migrationBuilder.DropTable(
                name: "ApplicationApplicants",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "ApplicationConsents",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "ApplicationDecisions",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "ScreeningResults",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "ScreeningRequests",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "RentalApplications",
                schema: "operations");
        }
    }
}
