using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations;

// pg_trgm powers the fuzzy global search. GIN trigram indexes back both the ILIKE substring
// match and the word_similarity ranking on each searched label column.
[DbContext(typeof(OperationsStore))]
[Migration("20260909215300_GlobalSearchTrigrams")]
public sealed class GlobalSearchTrigrams : Migration
{
    private static readonly (string Table, string Column)[] Targets =
    [
        ("Properties", "Name"),
        ("Buildings", "Name"),
        ("Spaces", "Code"),
        ("Residents", "FullName"),
        ("Vendors", "Name"),
        ("Employees", "DisplayName"),
        ("WorkCategories", "Name"),
        ("Assets", "Name"),
        ("WorkItems", "Title")
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        foreach (var (table, column) in Targets)
            migrationBuilder.Sql(
                $"""CREATE INDEX "IX_{table}_{column}_trgm" ON operations."{table}" USING gin ("{column}" gin_trgm_ops);""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var (table, column) in Targets)
            migrationBuilder.Sql($"""DROP INDEX operations."IX_{table}_{column}_trgm";""");
        migrationBuilder.Sql("DROP EXTENSION IF EXISTS pg_trgm;");
    }
}
