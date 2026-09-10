using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Domain.Automation;
using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.IntegrationTests;

// The unit suite constructs AutomationRule in memory, which never runs the EF materialization
// constructor. Only a real read out of PostgreSQL does, so the regression lives here.
[Collection("PostgreSQL")]
public sealed class AutomationRuleIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Saved_rule_materialises_back_out_of_postgres()
    {
        await using var scenario = await fixture.CreateScenarioAsync();
        var ruleId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

        await using (var writer = scenario.AdminStore(scenario.OrganizationA))
        {
            writer.AutomationRules.Add(new AutomationRule(
                scenario.OrganizationA, ruleId, "Emergency work priority", AutomationTrigger.WorkCreated,
                [new AutomationCondition(AutomationConditionKind.WorkStatusEquals, WorkStatus.New)],
                [new AutomationAction(AutomationActionKind.SendResidentMessage, TemplateId: templateId)],
                createdAt));
            await writer.SaveChangesAsync();
        }

        // A fresh store, so this is a materialization and not the identity map handing the
        // written instance back.
        await using var reader = scenario.Store(scenario.OrganizationA);
        var loaded = await reader.AutomationRules.AsNoTracking().SingleAsync(x => x.Id == ruleId);
        Assert.Equal(scenario.OrganizationA, loaded.OrganizationId);
        Assert.Equal(ruleId, loaded.Id);
        Assert.Equal("Emergency work priority", loaded.Name);
        Assert.Equal(AutomationTrigger.WorkCreated, loaded.Trigger);
        Assert.True(loaded.IsEnabled);
        Assert.Equal<WorkStatus?>(WorkStatus.New, Assert.Single(loaded.ReadConditions()).Status);
        Assert.Equal<Guid?>(templateId, Assert.Single(loaded.ReadActions()).TemplateId);
    }

    [Fact]
    public async Task Rule_list_endpoint_still_reads_after_a_rule_exists()
    {
        await using var scenario = await fixture.CreateScenarioAsync();
        await scenario.LoginAsync();

        Assert.Empty(await scenario.Client.GetFromJsonAsync<JsonElement[]>("/api/automation/rules/") ?? []);

        var create = await scenario.Client.PostAsJsonAsync("/api/automation/rules/", new
        {
            name = "Emergency work priority",
            trigger = "WorkCreated",
            conditions = new[] { new { kind = "WorkStatusEquals", status = "New" } },
            actions = new[] { new { kind = "SetPriority", priority = "Critical" } }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // The defect: the write succeeded but every later read threw on materialization, so the
        // list came back 400 for the rest of that organization's life.
        var list = await scenario.Client.GetAsync("/api/automation/rules/");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var rules = await list.Content.ReadFromJsonAsync<JsonElement[]>() ?? [];
        var only = Assert.Single(rules);
        Assert.Equal("Emergency work priority", only.GetProperty("name").GetString());
        Assert.Equal("WorkCreated", only.GetProperty("trigger").GetString());
        Assert.True(only.GetProperty("isEnabled").GetBoolean());

        // Enable/disable loads the tracked entity through the same constructor.
        var disable = await scenario.Client.PostAsJsonAsync(
            $"/api/automation/rules/{only.GetProperty("id").GetGuid()}/enabled", new { enabled = false });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.False((await scenario.Client.GetFromJsonAsync<JsonElement[]>("/api/automation/rules/"))![0]
            .GetProperty("isEnabled").GetBoolean());
    }
}
