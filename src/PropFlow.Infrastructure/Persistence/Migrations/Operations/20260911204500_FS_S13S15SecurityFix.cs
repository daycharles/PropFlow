using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations;

// Hand-written security migration, no model change — see TenantSecurity.cs for the pattern.
//
// 20260911193555_FS_S13S15Operations created AssetLifecycleCosts, ComplianceObligations,
// Incidents, MeterReadings, PreventiveMaintenancePlans, Remediations, Violations and
// IncidentAudit with a GRANT (DatabaseProvisioner.cs) but no RLS at all — found via
// ZZDiagnosticTests during the FS-S03.02/develop merge (2026-09-11): 8 operations tables had
// relrowsecurity = false. The sibling follow-up migration
// (20260911195809_FS_S13S15Acceptance) enabled RLS for the three tables *it* added
// (ComplianceEvidence, ComplianceOccurrences, PreventiveMaintenanceOccurrences) but never went
// back for the original eight. Until this migration, the restricted propflow_app role had a
// GRANT on all eight with zero database-level tenant confinement — the EF query filter was the
// only thing standing between one organization's incident/compliance/preventive-maintenance
// data and another's; a raw query, a filter bypass, or a future refactor mistake would have
// leaked cross-tenant with no DB backstop at all.
[DbContext(typeof(OperationsStore))]
[Migration("20260911204500_FS_S13S15SecurityFix")]
public sealed class FS_S13S15SecurityFix : Migration
{
    private static readonly string[] Tables =
    [
        "AssetLifecycleCosts", "ComplianceObligations", "Incidents", "MeterReadings",
        "PreventiveMaintenancePlans", "Remediations", "Violations", "IncidentAudit",
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
            migrationBuilder.Sql($"""
            DROP POLICY IF EXISTS tenant_isolation ON operations."{table}";
            ALTER TABLE operations."{table}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE operations."{table}" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON operations."{table}"
              USING ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid)
              WITH CHECK ("OrganizationId" = nullif(current_setting('app.organization_id', true), '')::uuid);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
            migrationBuilder.Sql($"""
            DROP POLICY IF EXISTS tenant_isolation ON operations."{table}";
            ALTER TABLE operations."{table}" DISABLE ROW LEVEL SECURITY;
            """);
    }
}
