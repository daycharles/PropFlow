using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class InspectionWorkflowSecurityTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Inspection_workflow_tables_have_forced_tenant_policies()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var store = s.Store(s.OrganizationA);
        var tables = await store.Database.SqlQueryRaw<string>("SELECT c.relname AS \"Value\" FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'operations' AND c.relname IN ('InspectionTemplates','Inspections','InspectionFindings','UnitTurns','UnitTurnTasks') AND c.relrowsecurity AND c.relforcerowsecurity ORDER BY c.relname").ToListAsync();
        Assert.Equal(["InspectionFindings", "InspectionTemplates", "Inspections", "UnitTurnTasks", "UnitTurns"], tables);
        var policies = await store.Database.SqlQueryRaw<string>("SELECT tablename AS \"Value\" FROM pg_policies WHERE schemaname = 'operations' AND policyname = 'tenant_isolation' AND tablename IN ('InspectionTemplates','Inspections','InspectionFindings','UnitTurns','UnitTurnTasks') ORDER BY tablename").ToListAsync();
        Assert.Equal(tables, policies);
    }

    [Fact]
    public async Task Read_only_cannot_create_or_mutate_inspection_workflows()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);
        var create = await s.Client.PostAsJsonAsync("/api/inspection-templates", new { name = "Move out", checklist = new[] { "Walls" } });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        var archive = await s.Client.PostAsync($"/api/inspection-templates/{Guid.NewGuid()}/archive", null);
        Assert.Equal(HttpStatusCode.Forbidden, archive.StatusCode);
    }
}
