using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations;

[DbContext(typeof(OperationsStore))]
[Migration("20260910150000_M5AutomationRules")]
public sealed class M5AutomationRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AutomationRules", schema: "operations",
            columns: table => new
            {
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Trigger = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Conditions = table.Column<string>(type: "jsonb", nullable: false),
                Actions = table.Column<string>(type: "jsonb", nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AutomationRules", x => new { x.OrganizationId, x.Id }));
        migrationBuilder.CreateIndex(
            name: "IX_AutomationRules_OrganizationId_IsEnabled_Trigger", schema: "operations",
            table: "AutomationRules", columns: new[] { "OrganizationId", "IsEnabled", "Trigger" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "AutomationRules", schema: "operations");
}
