using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Application.Automation;
using PropFlow.Application.Communications;
using PropFlow.Domain.Automation;
using PropFlow.Domain.Communications;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Communications;
using PropFlow.Infrastructure.Persistence;
using Xunit;

namespace PropFlow.IntegrationTests;

// PF-5.13: the automation runtime that consumes committed work events, matches the tenant's
// enabled rules, and applies the closed action set.
[Collection("PostgreSQL")]
public sealed class AutomationEngineTests(DatabaseFixture fixture)
{
    private static EfAutomationEngine Engine(OperationsStore store, CommunicationsStore comms) =>
        new(store,
            new EfResidentMessenger(store, comms, new EfOutbox(comms, TimeProvider.System), new TemplateRenderer()),
            TimeProvider.System, NullLogger<EfAutomationEngine>.Instance);

    private static async Task<Guid> SeedPublishedWorkAsync(Scenario s, Guid org, Guid property, Guid creator,
        Guid? categoryId = null, WorkPriority priority = WorkPriority.Normal)
    {
        var id = Guid.NewGuid();
        await using var store = s.AdminStore(org);
        var work = new WorkItem(org, id, "Automation subject", property, creator);
        work.Edit("Automation subject", null, categoryId, priority);
        work.Publish(DateTimeOffset.UtcNow);
        store.WorkItems.Add(work);
        await store.SaveChangesAsync();
        return id;
    }

    private static async Task SeedRuleAsync(Scenario s, Guid org, AutomationTrigger trigger,
        IReadOnlyList<AutomationCondition> conditions, IReadOnlyList<AutomationAction> actions, bool enabled = true)
    {
        await using var store = s.AdminStore(org);
        var rule = new AutomationRule(org, Guid.NewGuid(), "rule", trigger, conditions, actions, DateTimeOffset.UtcNow);
        if (!enabled) rule.Disable();
        store.AutomationRules.Add(rule);
        await store.SaveChangesAsync();
    }

    private static async Task<Guid> SeedSmsTemplateAsync(Scenario s, Guid org)
    {
        var id = Guid.NewGuid();
        await using var store = s.CommsAsAdmin(org);
        store.MessageTemplates.Add(new MessageTemplate(org, id, "auto", MessageChannel.Sms, null,
            "Hi {{ resident.name }}, {{ work.title }}."));
        await store.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task A_matching_rule_applies_its_action_and_records_an_audit_entry()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var category = Guid.NewGuid();
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            admin.Categories.Add(new WorkCategory(s.OrganizationA, category, "Emergencies", 1));
            await admin.SaveChangesAsync();
        }
        await SeedRuleAsync(s, s.OrganizationA, AutomationTrigger.WorkCreated,
            [new(AutomationConditionKind.CategoryEquals, CategoryId: category)],
            [new(AutomationActionKind.SetPriority, Priority: WorkPriority.Critical)]);
        var workId = await SeedPublishedWorkAsync(s, s.OrganizationA, s.PropertyA, s.AdminA, category);

        await using var store = s.Store(s.OrganizationA);
        await using var comms = s.Comms(s.OrganizationA);
        var summary = await Engine(store, comms).RunAsync(AutomationTrigger.WorkCreated, workId, Guid.NewGuid(), default);

        Assert.Equal(new AutomationRunSummary(1, 1, 0), summary);
        await using var verify = s.AdminStore(s.OrganizationA);
        Assert.Equal(WorkPriority.Critical, (await verify.WorkItems.SingleAsync(x => x.Id == workId)).Priority);
        Assert.Single(await verify.Timeline.Where(t => t.WorkId == workId && t.EventType == "AutomationApplied").ToListAsync());
        Assert.Single(await verify.Timeline.Where(t => t.WorkId == workId && t.EventType == "PriorityChanged").ToListAsync());
    }

    [Fact]
    public async Task A_rule_whose_condition_does_not_hold_does_nothing()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedRuleAsync(s, s.OrganizationA, AutomationTrigger.WorkCreated,
            [new(AutomationConditionKind.CategoryEquals, CategoryId: Guid.NewGuid())],
            [new(AutomationActionKind.SetPriority, Priority: WorkPriority.Critical)]);
        var workId = await SeedPublishedWorkAsync(s, s.OrganizationA, s.PropertyA, s.AdminA);

        await using var store = s.Store(s.OrganizationA);
        await using var comms = s.Comms(s.OrganizationA);
        var summary = await Engine(store, comms).RunAsync(AutomationTrigger.WorkCreated, workId, Guid.NewGuid(), default);

        Assert.Equal(AutomationRunSummary.Empty, summary);
        await using var verify = s.AdminStore(s.OrganizationA);
        Assert.Equal(WorkPriority.Normal, (await verify.WorkItems.SingleAsync(x => x.Id == workId)).Priority);
        Assert.Empty(await verify.Timeline.Where(t => t.EventType == "AutomationApplied").ToListAsync());
    }

    [Fact]
    public async Task A_disabled_rule_does_nothing()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedRuleAsync(s, s.OrganizationA, AutomationTrigger.WorkCreated, [],
            [new(AutomationActionKind.SetPriority, Priority: WorkPriority.Critical)], enabled: false);
        var workId = await SeedPublishedWorkAsync(s, s.OrganizationA, s.PropertyA, s.AdminA);

        await using var store = s.Store(s.OrganizationA);
        await using var comms = s.Comms(s.OrganizationA);
        var summary = await Engine(store, comms).RunAsync(AutomationTrigger.WorkCreated, workId, Guid.NewGuid(), default);

        Assert.Equal(AutomationRunSummary.Empty, summary);
        await using var verify = s.AdminStore(s.OrganizationA);
        Assert.Equal(WorkPriority.Normal, (await verify.WorkItems.SingleAsync(x => x.Id == workId)).Priority);
    }

    [Fact]
    public async Task Re_running_the_same_occurrence_is_a_no_op()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var templateId = await SeedSmsTemplateAsync(s, s.OrganizationA);
        await SeedRuleAsync(s, s.OrganizationA, AutomationTrigger.WorkCreated, [],
            [new(AutomationActionKind.SetPriority, Priority: WorkPriority.High),
             new(AutomationActionKind.SendResidentMessage, TemplateId: templateId)]);
        // WorkA has a resident with SMS consent (fixture).
        var workId = s.WorkA;
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            var work = await admin.WorkItems.SingleAsync(x => x.Id == workId);
            work.SetLocation(s.PropertyA, null, s.SpaceA, s.ResidentA);
            work.Publish(DateTimeOffset.UtcNow);
            await admin.SaveChangesAsync();
        }
        var occurrence = Guid.NewGuid();

        await using var store = s.Store(s.OrganizationA);
        await using var comms = s.Comms(s.OrganizationA);
        var engine = Engine(store, comms);
        var first = await engine.RunAsync(AutomationTrigger.WorkCreated, workId, occurrence, default);
        var second = await engine.RunAsync(AutomationTrigger.WorkCreated, workId, occurrence, default);

        Assert.Equal(1, first.RulesApplied);
        Assert.Equal(0, second.RulesApplied);
        await using var verify = s.AdminStore(s.OrganizationA);
        Assert.Single(await verify.Timeline.Where(t => t.WorkId == workId && t.EventType == "AutomationApplied").ToListAsync());
        Assert.Single(await verify.Timeline.Where(t => t.WorkId == workId && t.EventType == "PriorityChanged").ToListAsync());
        await using var verifyComms = s.CommsAsAdmin(s.OrganizationA);
        Assert.Equal(1, await verifyComms.OutboxMessages.CountAsync(x => x.WorkId == workId));
    }

    [Fact]
    public async Task Rules_are_tenant_scoped()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await SeedRuleAsync(s, s.OrganizationA, AutomationTrigger.WorkCreated, [],
            [new(AutomationActionKind.SetPriority, Priority: WorkPriority.Critical)]);
        var workB = await SeedPublishedWorkAsync(s, s.OrganizationB, s.PropertyB, s.AdminB);

        await using var store = s.Store(s.OrganizationB);
        await using var comms = s.Comms(s.OrganizationB);
        var summary = await Engine(store, comms).RunAsync(AutomationTrigger.WorkCreated, workB, Guid.NewGuid(), default);

        Assert.Equal(AutomationRunSummary.Empty, summary);
        await using var verify = s.AdminStore(s.OrganizationB);
        Assert.Equal(WorkPriority.Normal, (await verify.WorkItems.SingleAsync(x => x.Id == workB)).Priority);
    }

    [Fact]
    public async Task A_failing_rule_is_isolated_from_a_good_one()
    {
        await using var s = await fixture.CreateScenarioAsync();
        Guid badTemplate;
        await using (var store = s.CommsAsAdmin(s.OrganizationA))
        {
            badTemplate = Guid.NewGuid();
            store.MessageTemplates.Add(new MessageTemplate(s.OrganizationA, badTemplate, "broken", MessageChannel.Sms, null,
                "Hi {{ resident.missing }}"));
            await store.SaveChangesAsync();
        }
        await SeedRuleAsync(s, s.OrganizationA, AutomationTrigger.WorkStatusChanged, [],
            [new(AutomationActionKind.SendResidentMessage, TemplateId: badTemplate)]);
        await SeedRuleAsync(s, s.OrganizationA, AutomationTrigger.WorkStatusChanged, [],
            [new(AutomationActionKind.SetPriority, Priority: WorkPriority.High)]);
        var workId = s.WorkA;
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            var work = await admin.WorkItems.SingleAsync(x => x.Id == workId);
            work.SetLocation(s.PropertyA, null, s.SpaceA, s.ResidentA);
            work.Publish(DateTimeOffset.UtcNow);
            await admin.SaveChangesAsync();
        }

        await using var store2 = s.Store(s.OrganizationA);
        await using var comms = s.Comms(s.OrganizationA);
        var summary = await Engine(store2, comms).RunAsync(AutomationTrigger.WorkStatusChanged, workId, Guid.NewGuid(), default);

        Assert.Equal(2, summary.RulesMatched);
        Assert.Equal(1, summary.RulesApplied);
        Assert.Equal(1, summary.RulesFailed);
        await using var verify = s.AdminStore(s.OrganizationA);
        Assert.Equal(WorkPriority.High, (await verify.WorkItems.SingleAsync(x => x.Id == workId)).Priority);
    }

    [Fact]
    public async Task Creating_work_through_the_api_fires_the_WorkCreated_evaluator()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var category = Guid.NewGuid();
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            admin.Categories.Add(new WorkCategory(s.OrganizationA, category, "After hours", 1));
            await admin.SaveChangesAsync();
        }
        await SeedRuleAsync(s, s.OrganizationA, AutomationTrigger.WorkCreated,
            [new(AutomationConditionKind.CategoryEquals, CategoryId: category)],
            [new(AutomationActionKind.SetPriority, Priority: WorkPriority.Critical)]);

        var created = await s.Client.PostAsJsonAsync("/api/work/",
            new { title = "Leak in 3B", propertyId = s.PropertyA, categoryId = category });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var workId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("item").GetProperty("id").GetGuid();

        var detail = await s.Client.GetFromJsonAsync<JsonElement>($"/api/work/{workId}");
        Assert.Equal("Critical", detail.GetProperty("item").GetProperty("priority").GetString());
    }

    [Fact]
    public async Task Changing_status_through_the_api_fires_the_WorkStatusChanged_evaluator()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        await SeedRuleAsync(s, s.OrganizationA, AutomationTrigger.WorkStatusChanged,
            [new(AutomationConditionKind.WorkStatusEquals, Status: WorkStatus.OnHold)],
            [new(AutomationActionKind.SetPriority, Priority: WorkPriority.Low)]);
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            (await admin.WorkItems.SingleAsync(x => x.Id == s.WorkA)).Publish(DateTimeOffset.UtcNow);
            await admin.SaveChangesAsync();
        }
        var version = (await s.Client.GetFromJsonAsync<JsonElement>($"/api/work/{s.WorkA}")).GetProperty("version").GetUInt32();

        var updated = await s.Client.PutAsJsonAsync($"/api/work/{s.WorkA}", new
        {
            title = "Pest control", description = (string?)null, categoryId = (Guid?)null, priority = "Normal",
            propertyId = s.PropertyA, buildingId = (Guid?)null, spaceId = (Guid?)null, residentId = (Guid?)null,
            assetId = (Guid?)null, dueDate = (string?)null, cost = (decimal?)null, internalNotes = (string?)null,
            residentVisibleNotes = (string?)null, status = "OnHold", scheduledStart = (string?)null,
            scheduledEnd = (string?)null, version,
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var detail = await s.Client.GetFromJsonAsync<JsonElement>($"/api/work/{s.WorkA}");
        Assert.Equal("Low", detail.GetProperty("item").GetProperty("priority").GetString());
    }
}
