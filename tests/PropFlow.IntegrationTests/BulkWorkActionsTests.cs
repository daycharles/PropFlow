using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Domain.Properties;
using PropFlow.Domain.People;
using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class BulkWorkActionsTests(DatabaseFixture fixture)
{
    // Two published, vendor-assigned work items in org A, plus their current versions.
    private static async Task<(Guid a, Guid b, Guid property)> SeedAsync(Scenario s)
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        await using var store = s.AdminStore(s.OrganizationA);
        var portfolio = Guid.NewGuid(); var property = Guid.NewGuid();
        store.Portfolios.Add(new Portfolio(s.OrganizationA, portfolio, "Bulk portfolio"));
        store.Properties.Add(new Property(s.OrganizationA, property, portfolio, "Bulk property", "America/New_York"));
        foreach (var id in new[] { a, b })
        {
            var work = new WorkItem(s.OrganizationA, id, $"Pest job {id:N}", property, s.AdminA);
            work.Publish(DateTimeOffset.UtcNow);
            work.AssignVendor(s.VendorA);
            store.WorkItems.Add(work);
        }
        await store.SaveChangesAsync();
        return (a, b, property);
    }

    private static async Task<uint> VersionAsync(Scenario s, Guid id) =>
        (await s.Client.GetFromJsonAsync<JsonElement>($"/api/work/{id}")).GetProperty("version").GetUInt32();

    private static object Ref(Guid id, uint version) => new { workId = id, version };

    [Fact]
    public async Task Bulk_status_change_is_all_or_nothing_and_writes_one_timeline_entry_per_item()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var (a, b, _) = await SeedAsync(s);

        var response = await s.Client.PostAsJsonAsync("/api/work/bulk/status", new
        {
            status = "OnHold",
            items = new[] { Ref(a, await VersionAsync(s, a)), Ref(b, await VersionAsync(s, b)) }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("changed").GetInt32());

        await using var verify = s.Store(s.OrganizationA);
        Assert.Equal(WorkStatus.OnHold, (await verify.WorkItems.SingleAsync(x => x.Id == a)).Status);
        Assert.Equal(2, await verify.Timeline.CountAsync(x => x.EventType == "StatusChanged"));
    }

    [Fact]
    public async Task A_stale_version_rolls_the_whole_batch_back()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var (a, b, _) = await SeedAsync(s);
        var goodVersion = await VersionAsync(s, a);

        var response = await s.Client.PostAsJsonAsync("/api/work/bulk/priority", new
        {
            priority = "Critical",
            items = new[] { Ref(a, goodVersion), Ref(b, 999u) }
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var verify = s.Store(s.OrganizationA);
        Assert.Equal(WorkPriority.Normal, (await verify.WorkItems.SingleAsync(x => x.Id == a)).Priority);
        Assert.Equal(0, await verify.Timeline.CountAsync(x => x.EventType == "PriorityChanged"));
    }

    [Fact]
    public async Task Bulk_employee_assignment_is_atomic_and_records_each_changed_item()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var (a, b, _) = await SeedAsync(s);
        var employee = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            store.Employees.Add(new Employee(s.OrganizationA, employee, "Casey Lee", null, null));
            await store.SaveChangesAsync();
        }

        var response = await s.Client.PostAsJsonAsync("/api/work/bulk/employee", new
        {
            employeeId = employee,
            items = new[] { Ref(a, await VersionAsync(s, a)), Ref(b, await VersionAsync(s, b)) }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verify = s.Store(s.OrganizationA);
        Assert.All(await verify.WorkItems.Where(x => x.Id == a || x.Id == b).ToListAsync(), x => Assert.Equal(employee, x.EmployeeId));
        Assert.Equal(2, await verify.Timeline.CountAsync(x => x.EventType == "EmployeeAssigned"));
    }

    [Fact]
    public async Task Bulk_schedule_requires_every_item_open_and_assigned()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var (a, _, property) = await SeedAsync(s);
        var unassigned = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            var work = new WorkItem(s.OrganizationA, unassigned, "No vendor yet", property, s.AdminA);
            work.Publish(DateTimeOffset.UtcNow);
            store.WorkItems.Add(work);
            await store.SaveChangesAsync();
        }

        var start = DateTimeOffset.UtcNow.AddDays(1);
        var refused = await s.Client.PostAsJsonAsync("/api/work/bulk/schedule", new
        {
            scheduledStart = start,
            items = new[] { Ref(a, await VersionAsync(s, a)), Ref(unassigned, await VersionAsync(s, unassigned)) }
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var ok = await s.Client.PostAsJsonAsync("/api/work/bulk/schedule", new
        {
            scheduledStart = start,
            items = new[] { Ref(a, await VersionAsync(s, a)) }
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await using var verify = s.Store(s.OrganizationA);
        Assert.Equal(WorkStatus.Scheduled, (await verify.WorkItems.SingleAsync(x => x.Id == a)).Status);
    }

    [Fact]
    public async Task Bulk_note_adds_a_visible_timeline_entry_even_on_completed_work()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var (a, _, _) = await SeedAsync(s);
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            var work = await store.WorkItems.SingleAsync(x => x.Id == a);
            work.ChangeStatus(WorkStatus.Completed, DateTimeOffset.UtcNow);
            await store.SaveChangesAsync();
        }

        var response = await s.Client.PostAsJsonAsync("/api/work/bulk/note", new
        {
            note = "Vendor confirmed follow-up visit", @internal = true,
            items = new[] { Ref(a, await VersionAsync(s, a)) }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verify = s.Store(s.OrganizationA);
        var entry = Assert.Single(await verify.Timeline.Where(x => x.EventType == "WorkNote").ToListAsync());
        Assert.Equal("Vendor confirmed follow-up visit", entry.NewValue);
        Assert.Contains("internal", entry.Changes);
    }

    [Fact]
    public async Task Timeline_resident_projection_returns_only_notes_marked_resident_visible()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var (a, _, _) = await SeedAsync(s);

        var visible = await s.Client.PostAsJsonAsync("/api/work/bulk/note", new
        {
            note = "The repair is scheduled for tomorrow", @internal = false,
            items = new[] { Ref(a, await VersionAsync(s, a)) }
        });
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);

        var internalNote = await s.Client.PostAsJsonAsync("/api/work/bulk/note", new
        {
            note = "Vendor gate code is stored in the dispatch system", @internal = true,
            items = new[] { Ref(a, await VersionAsync(s, a)) }
        });
        Assert.Equal(HttpStatusCode.OK, internalNote.StatusCode);

        var residentTimeline = await s.Client.GetFromJsonAsync<JsonElement>($"/api/work/{a}/timeline?residentVisibleOnly=true");
        var entry = Assert.Single(residentTimeline.EnumerateArray());
        Assert.Equal("The repair is scheduled for tomorrow", entry.GetProperty("newValue").GetString());
        Assert.True(entry.GetProperty("residentVisible").GetBoolean());
    }

    [Fact]
    public async Task Bulk_reopen_restores_terminal_work_and_refuses_a_live_item_in_the_batch()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var (a, b, _) = await SeedAsync(s);
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            (await store.WorkItems.SingleAsync(x => x.Id == a)).ChangeStatus(WorkStatus.Completed, DateTimeOffset.UtcNow);
            await store.SaveChangesAsync();
        }

        // b is still live -> whole batch refused
        var mixed = await s.Client.PostAsJsonAsync("/api/work/bulk/reopen", new
        {
            items = new[] { Ref(a, await VersionAsync(s, a)), Ref(b, await VersionAsync(s, b)) }
        });
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);

        var ok = await s.Client.PostAsJsonAsync("/api/work/bulk/reopen", new
        {
            items = new[] { Ref(a, await VersionAsync(s, a)) }
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        await using var verify = s.Store(s.OrganizationA);
        var work = await verify.WorkItems.SingleAsync(x => x.Id == a);
        Assert.Equal(WorkStatus.Assigned, work.Status);
        Assert.Null(work.CompletedAt);
        Assert.Equal(1, await verify.Timeline.CountAsync(x => x.EventType == nameof(PropFlow.Domain.Work.WorkReopened)));
    }

    [Fact]
    public async Task Bulk_endpoints_reject_a_draft_target_an_oversized_batch_and_a_read_only_user()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var (a, _, _) = await SeedAsync(s);
        var v = await VersionAsync(s, a);

        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PostAsJsonAsync("/api/work/bulk/status",
            new { status = "Draft", items = new[] { Ref(a, v) } })).StatusCode);

        var many = Enumerable.Range(0, 101).Select(_ => Ref(Guid.NewGuid(), 0u)).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PostAsJsonAsync("/api/work/bulk/priority",
            new { priority = "High", items = many })).StatusCode);

        await s.LoginAsync(reader: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsJsonAsync("/api/work/bulk/note",
            new { note = "x", items = new[] { Ref(a, v) } })).StatusCode);
    }

    [Fact]
    public async Task A_bulk_note_still_honours_the_per_item_version()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var (a, _, _) = await SeedAsync(s);

        var stale = await s.Client.PostAsJsonAsync("/api/work/bulk/note", new
        {
            note = "late note", items = new[] { Ref(a, 999u) }
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        await using var verify = s.Store(s.OrganizationA);
        Assert.Equal(0, await verify.Timeline.CountAsync(x => x.EventType == "WorkNote"));
    }

    [Fact]
    public async Task A_null_item_or_an_undefined_enum_is_a_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var (a, _, _) = await SeedAsync(s);
        var v = await VersionAsync(s, a);

        var withNull = await s.Client.PostAsJsonAsync("/api/work/bulk/priority", new
        {
            priority = "High", items = new object?[] { Ref(a, v), null }
        });
        Assert.Equal(HttpStatusCode.BadRequest, withNull.StatusCode);

        var badStatus = await s.Client.PostAsJsonAsync("/api/work/bulk/status", new
        {
            status = 999, items = new[] { Ref(a, v) }
        });
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
    }

    [Fact]
    public async Task Re_scheduling_to_the_same_window_is_a_no_op()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var (a, _, _) = await SeedAsync(s);
        // A sub-microsecond tick component: it survives the JSON round-trip but not the Postgres
        // timestamptz store, so the no-op check must tolerate the difference.
        var start = new DateTimeOffset(2026, 10, 1, 14, 0, 0, TimeSpan.Zero).AddTicks(3);

        var first = await s.Client.PostAsJsonAsync("/api/work/bulk/schedule",
            new { scheduledStart = start, items = new[] { Ref(a, await VersionAsync(s, a)) } });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var again = await s.Client.PostAsJsonAsync("/api/work/bulk/schedule",
            new { scheduledStart = start, items = new[] { Ref(a, await VersionAsync(s, a)) } });
        var body = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("changed").GetInt32());

        await using var verify = s.Store(s.OrganizationA);
        Assert.Equal(1, await verify.Timeline.CountAsync(x => x.EventType == "Scheduled"));
    }

    [Fact]
    public async Task Foreign_tenant_work_ids_are_a_not_found_and_change_nothing()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var (a, _, _) = await SeedAsync(s);

        var response = await s.Client.PostAsJsonAsync("/api/work/bulk/status", new
        {
            status = "OnHold",
            items = new[] { Ref(a, await VersionAsync(s, a)), Ref(s.WorkB, 0u) }
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var verify = s.Store(s.OrganizationA);
        Assert.Equal(WorkStatus.Assigned, (await verify.WorkItems.SingleAsync(x => x.Id == a)).Status);
    }
}
