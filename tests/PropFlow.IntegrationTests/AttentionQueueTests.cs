using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class AttentionQueueTests(DatabaseFixture fixture)
{
    private static WorkItem Published(Guid org, string title, Guid property, Guid creator, DateTimeOffset createdAt,
        WorkPriority priority = WorkPriority.Normal, WorkType type = WorkType.WorkOrder)
    {
        var work = new WorkItem(org, Guid.NewGuid(), title, property, creator, type);
        if (priority != WorkPriority.Normal) work.Edit(title, null, null, priority);
        work.Publish(createdAt);
        return work;
    }

    [Fact]
    public async Task The_queue_flags_open_work_grouped_by_severity_most_urgent_first()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var store = s.AdminStore(s.OrganizationA))
        {
            // Critical, unassigned -> unassigned emergency (Critical).
            store.WorkItems.Add(Published(s.OrganizationA, "Burst pipe", s.PropertyA, s.AdminA, now.AddHours(-1), WorkPriority.Critical));

            // High, still New for three days -> first-response SLA breach (Critical).
            store.WorkItems.Add(Published(s.OrganizationA, "No heat in 4B", s.PropertyA, s.AdminA, now.AddDays(-3), WorkPriority.High));

            // In progress but two days past due -> overdue (Warning).
            var overdue = Published(s.OrganizationA, "Repaint hallway", s.PropertyA, s.AdminA, now.AddDays(-10));
            overdue.SetDueDate(now.AddDays(-2));
            overdue.ChangeStatus(WorkStatus.InProgress, now.AddDays(-4));
            store.WorkItems.Add(overdue);

            // On hold with a vendor, untouched for ten days -> waiting on vendor (Warning).
            var stale = Published(s.OrganizationA, "Awaiting part", s.PropertyA, s.AdminA, now.AddDays(-10));
            stale.AssignVendor(s.VendorA);
            stale.ChangeStatus(WorkStatus.OnHold, now.AddDays(-10));
            store.WorkItems.Add(stale);

            // Fresh, not due -> nothing.
            store.WorkItems.Add(Published(s.OrganizationA, "Routine filter change", s.PropertyA, s.AdminA, now.AddHours(-2)));

            await store.SaveChangesAsync();
        }

        var queue = await s.Client.GetFromJsonAsync<JsonElement>("/api/attention");

        Assert.Equal(2, queue.GetProperty("criticalCount").GetInt32());
        Assert.Equal(2, queue.GetProperty("warningCount").GetInt32());
        Assert.Equal(0, queue.GetProperty("informationalCount").GetInt32());

        var items = queue.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(4, items.Length);
        Assert.Equal("Critical", items[0].GetProperty("severity").GetString());
        Assert.Equal("Critical", items[1].GetProperty("severity").GetString());
        Assert.Equal("Warning", items[2].GetProperty("severity").GetString());

        var reasons = items.Select(i => i.GetProperty("reason").GetString()).ToArray();
        Assert.Contains("UnassignedEmergency", reasons);
        Assert.Contains("SlaBreach", reasons);
        Assert.Contains("Overdue", reasons);
        Assert.Contains("WaitingOnVendor", reasons);

        foreach (var item in items)
        {
            Assert.NotEqual(Guid.Empty, item.GetProperty("workId").GetGuid());
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("detail").GetString()));
            Assert.Equal("Harbor Point Apartments", item.GetProperty("propertyName").GetString());
        }
    }

    [Fact]
    public async Task Completed_and_draft_work_is_never_in_the_queue()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var store = s.AdminStore(s.OrganizationA))
        {
            var done = Published(s.OrganizationA, "Old critical, resolved", s.PropertyA, s.AdminA, now.AddDays(-30), WorkPriority.Critical);
            done.SetDueDate(now.AddDays(-20));
            done.ChangeStatus(WorkStatus.Completed, now.AddDays(-19));
            store.WorkItems.Add(done);
            // A draft is not published work at all.
            store.WorkItems.Add(new WorkItem(s.OrganizationA, Guid.NewGuid(), "Draft critical", s.PropertyA, s.AdminA));
            await store.SaveChangesAsync();
        }

        var queue = await s.Client.GetFromJsonAsync<JsonElement>("/api/attention");
        Assert.Equal(0, queue.GetProperty("criticalCount").GetInt32());
        Assert.Empty(queue.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task The_queue_is_tenant_scoped()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var store = s.AdminStore(s.OrganizationB))
        {
            store.WorkItems.Add(Published(s.OrganizationB, "Org B emergency", s.PropertyB, s.AdminB, now.AddHours(-1), WorkPriority.Critical));
            await store.SaveChangesAsync();
        }

        await s.LoginAsync();
        var queue = await s.Client.GetFromJsonAsync<JsonElement>("/api/attention");
        Assert.Equal(0, queue.GetProperty("criticalCount").GetInt32());
        Assert.DoesNotContain(
            queue.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("title").GetString() == "Org B emergency");
    }

    [Fact]
    public async Task A_repeat_repair_asset_flags_its_open_work()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var store = s.AdminStore(s.OrganizationA))
        {
            for (var i = 0; i < 3; i++)
            {
                var repair = Published(s.OrganizationA, $"HVAC repair {i}", s.PropertyA, s.AdminA, now.AddDays(-5 * (i + 1)));
                repair.SetAsset(s.AssetA);
                repair.AssignVendor(s.VendorA);
                store.WorkItems.Add(repair);
            }
            await store.SaveChangesAsync();
        }

        var queue = await s.Client.GetFromJsonAsync<JsonElement>("/api/attention");
        Assert.Contains(
            queue.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("reason").GetString() == "RepeatRepair");
    }

    [Fact]
    public async Task The_queue_needs_an_authenticated_session()
    {
        await using var s = await fixture.CreateScenarioAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client.GetAsync("/api/attention")).StatusCode);

        await s.LoginAsync(reader: true);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/attention")).StatusCode);
    }
}
