using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations;

// Hand-written security migration for the FS-S09 ledger tables: the organization foreign key,
// forced row-level security and the tenant_isolation policy for all four, plus the append-only
// trigger that makes operations."JournalEntries" / operations."JournalLines" behave like
// operations."Timeline" — a posted journal is corrected by a reversing entry, never edited.
[DbContext(typeof(OperationsStore))]
[Migration("20260911192500_FS_S09LedgerSecurity")]
public sealed class FS_S09LedgerSecurity : Migration
{
    private static readonly string[] Tables = ["ChartOfAccounts", "FiscalPeriods", "JournalEntries", "JournalLines"];
    private static readonly string[] AppendOnlyTables = ["JournalEntries", "JournalLines"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
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
            CREATE FUNCTION operations.reject_journal_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              RAISE EXCEPTION 'Journal entries are append-only' USING ERRCODE = '55000';
            END; $$;
            """);
        foreach (var table in AppendOnlyTables)
        {
            migrationBuilder.Sql($"""
                CREATE TRIGGER journal_append_only BEFORE UPDATE OR DELETE ON operations."{table}"
                  FOR EACH ROW EXECUTE FUNCTION operations.reject_journal_mutation();
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in AppendOnlyTables)
            migrationBuilder.Sql($"""DROP TRIGGER journal_append_only ON operations."{table}";""");
        migrationBuilder.Sql("DROP FUNCTION operations.reject_journal_mutation();");
        foreach (var table in Tables)
        {
            migrationBuilder.Sql($"""
                DROP POLICY tenant_isolation ON operations."{table}";
                ALTER TABLE operations."{table}" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE operations."{table}" DROP CONSTRAINT "FK_{table}_Organization";
                """);
        }
    }
}
