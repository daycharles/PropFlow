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

    private static IEnumerable<string> Reasons(JsonElement item) =>
        item.GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("reason").GetString()!);

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

        var reasons = items.SelectMany(Reasons).ToArray();
        Assert.Contains("UnassignedEmergency", reasons);
        Assert.Contains("SlaBreach", reasons);
        Assert.Contains("Overdue", reasons);
        Assert.Contains("WaitingOnVendor", reasons);

        foreach (var item in items)
        {
            Assert.NotEqual(Guid.Empty, item.GetProperty("workId").GetGuid());
            Assert.Equal("Harbor Point Apartments", item.GetProperty("propertyName").GetString());
            var findings = item.GetProperty("findings").EnumerateArray().ToArray();
            Assert.NotEmpty(findings);
            Assert.All(findings, f => Assert.False(string.IsNullOrWhiteSpace(f.GetProperty("detail").GetString())));
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
            i => Reasons(i).Contains("RepeatRepair"));
    }

    /// <summary>
    /// D3 (2026-09-10): the /attention Critical card read 6 while the filtered list read 8,
    /// because the counts were of distinct work items and the rows were one per tripped rule.
    /// The queue now carries one row per work item, and each count is exactly the number of rows
    /// the matching severity card filters to.
    /// </summary>
    [Fact]
    public async Task Each_severity_count_equals_the_rows_that_severity_filter_shows()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var store = s.AdminStore(s.OrganizationA))
        {
            // Critical, unassigned, still New for three days and past due: three Critical rules
            // on ONE work item — the shape that made the card and the list disagree.
            var emergency = Published(s.OrganizationA, "Gas odor investigation", s.PropertyA, s.AdminA,
                now.AddDays(-3), WorkPriority.Critical);
            emergency.SetDueDate(now.AddDays(-1));
            store.WorkItems.Add(emergency);

            // One Warning-only work item.
            var overdue = Published(s.OrganizationA, "Repaint hallway", s.PropertyA, s.AdminA, now.AddDays(-10));
            overdue.SetDueDate(now.AddDays(-2));
            overdue.ChangeStatus(WorkStatus.InProgress, now.AddDays(-4));
            store.WorkItems.Add(overdue);

            // One Informational-only work item: on hold pending the resident, untouched for ten days.
            var waiting = Published(s.OrganizationA, "Resident to confirm access", s.PropertyA, s.AdminA, now.AddDays(-20));
            waiting.SetLocation(s.PropertyA, null, null, s.ResidentA);
            waiting.ChangeStatus(WorkStatus.OnHold, now.AddDays(-10));
            store.WorkItems.Add(waiting);

            await store.SaveChangesAsync();
        }

        var queue = await s.Client.GetFromJsonAsync<JsonElement>("/api/attention");
        var items = queue.GetProperty("items").EnumerateArray().ToArray();

        int ShownUnder(string severity) => items.Count(i =>
            i.GetProperty("findings").EnumerateArray()
                .Any(f => f.GetProperty("severity").GetString() == severity));

        Assert.Equal(ShownUnder("Critical"), queue.GetProperty("criticalCount").GetInt32());
        Assert.Equal(ShownUnder("Warning"), queue.GetProperty("warningCount").GetInt32());
        Assert.Equal(ShownUnder("Informational"), queue.GetProperty("informationalCount").GetInt32());

        // Three work items, and the emergency really did trip three Critical rules at once.
        Assert.Equal(3, items.Length);
        Assert.Equal(1, queue.GetProperty("criticalCount").GetInt32());
        Assert.Equal(1, queue.GetProperty("warningCount").GetInt32());
        Assert.Equal(1, queue.GetProperty("informationalCount").GetInt32());

        var emergencyItem = items.Single(i => i.GetProperty("title").GetString() == "Gas odor investigation");
        Assert.Equal(
            new[] { "Overdue", "SlaBreach", "UnassignedEmergency" },
            Reasons(emergencyItem).Order().ToArray());
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
