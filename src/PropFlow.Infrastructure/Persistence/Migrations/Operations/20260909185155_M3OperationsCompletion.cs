using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class M3OperationsCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Cost",
                schema: "operations",
                table: "WorkItems",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DueDate",
                schema: "operations",
                table: "WorkItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalNotes",
                schema: "operations",
                table: "WorkItems",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResidentId",
                schema: "operations",
                table: "WorkItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResidentVisibleNotes",
                schema: "operations",
                table: "WorkItems",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                schema: "operations",
                table: "Vendors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Trade",
                schema: "operations",
                table: "Vendors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "WorkId",
                schema: "operations",
                table: "Timeline",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorId",
                schema: "operations",
                table: "Timeline",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "NewValue",
                schema: "operations",
                table: "Timeline",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OldValue",
                schema: "operations",
                table: "Timeline",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedObjectId",
                schema: "operations",
                table: "Timeline",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedObjectType",
                schema: "operations",
                table: "Timeline",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // M3 entities were added after the initial tenant-security migration.  Each
            // must have the same database-enforced tenant boundary as the original tables.
            foreach (var table in new[] { "Employees", "Portfolios", "Properties", "Buildings", "Spaces", "WorkCategories" })
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "Employees", "Portfolios", "Properties", "Buildings", "Spaces", "WorkCategories" })
            {
                migrationBuilder.Sql($"""
                    DROP POLICY tenant_isolation ON operations."{table}";
                    ALTER TABLE operations."{table}" DISABLE ROW LEVEL SECURITY;
                    ALTER TABLE operations."{table}" DROP CONSTRAINT "FK_{table}_Organization";
                    """);
            }

            migrationBuilder.DropColumn(
                name: "Cost",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "DueDate",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "InternalNotes",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "ResidentId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "ResidentVisibleNotes",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "Category",
                schema: "operations",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "Trade",
                schema: "operations",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "NewValue",
                schema: "operations",
                table: "Timeline");

            migrationBuilder.DropColumn(
                name: "OldValue",
                schema: "operations",
                table: "Timeline");

            migrationBuilder.DropColumn(
                name: "RelatedObjectId",
                schema: "operations",
                table: "Timeline");

            migrationBuilder.DropColumn(
                name: "RelatedObjectType",
                schema: "operations",
                table: "Timeline");

            migrationBuilder.AlterColumn<Guid>(
                name: "WorkId",
                schema: "operations",
                table: "Timeline",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorId",
                schema: "operations",
                table: "Timeline",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
