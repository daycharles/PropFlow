using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class TimelineDropLegacyVendorColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Timeline is FORCE RLS + append-only by trigger, and a migration has no
            // app.organization_id set — so both have to be lifted or this UPDATE silently
            // matches zero rows. Both are restored below; DatabaseReadiness fails /health/ready
            // if relforcerowsecurity is left off (DatabaseHealth.cs:32,36). ENABLE ROW LEVEL
            // SECURITY is never touched, only the FORCE flag, so relrowsecurity stays true.
            // Rows written before OldValue/NewValue existed (pre-20260909185155) are the ones
            // this recovers; later rows already carry both.
            migrationBuilder.Sql("""
                ALTER TABLE operations."Timeline" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE operations."Timeline" DISABLE TRIGGER timeline_append_only;
                UPDATE operations."Timeline"
                   SET "NewValue" = COALESCE("NewValue", "VendorId"::text),
                       "OldValue" = COALESCE("OldValue", "PreviousVendorId"::text)
                 WHERE "VendorId" IS NOT NULL AND "NewValue" IS NULL;
                ALTER TABLE operations."Timeline" ENABLE TRIGGER timeline_append_only;
                ALTER TABLE operations."Timeline" FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropColumn(
                name: "PreviousVendorId",
                schema: "operations",
                table: "Timeline");

            migrationBuilder.DropColumn(
                name: "VendorId",
                schema: "operations",
                table: "Timeline");
        }

        /// <inheritdoc />
        /// <remarks>
        /// This does not round-trip. The two columns come back empty: Up() dropped them and the
        /// values are gone. What Up() backfilled into OldValue/NewValue stays there, so the
        /// information survives — but under the generalized column names, not the legacy ones.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreviousVendorId",
                schema: "operations",
                table: "Timeline",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VendorId",
                schema: "operations",
                table: "Timeline",
                type: "uuid",
                nullable: true);
        }
    }
}
