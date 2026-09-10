using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Domain.People;
using PropFlow.Domain.Properties;
using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class WorkEndpointsTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Assignment_rejects_a_stale_client_version_with_409()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var otherVendor = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            store.Vendors.Add(new Vendor(s.OrganizationA, otherVendor, "Other vendor"));
            await store.SaveChangesAsync();
        }
        await s.LoginAsync();
        var staleVersion = await VersionAsync(s, s.WorkA);
        Assert.Equal(HttpStatusCode.OK, (await s.AssignAsync(s.WorkA, s.VendorA)).StatusCode);
        var response = await s.Client.PostAsJsonAsync($"/api/work/{s.WorkA}/vendor", new { vendorId = otherVendor, version = staleVersion });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_assignment_is_atomic_when_one_item_has_a_stale_version()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var second = Guid.NewGuid();
        var otherVendor = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            var propertyId = AddProperty(store, s.OrganizationA);
            store.WorkItems.Add(new WorkItem(s.OrganizationA, second, "Second pest control", propertyId, s.AdminA));
            store.Vendors.Add(new Vendor(s.OrganizationA, otherVendor, "Other vendor"));
            await store.SaveChangesAsync();
        }
        await s.LoginAsync();
        var firstVersion = await VersionAsync(s, s.WorkA); var secondVersion = await VersionAsync(s, second);
        Assert.Equal(HttpStatusCode.OK, (await s.AssignAsync(s.WorkA, s.VendorA)).StatusCode);
        var response = await s.Client.PostAsJsonAsync("/api/work/bulk/vendor", new
        {
            vendorId = otherVendor,
            items = new[] { new { workId = s.WorkA, version = firstVersion }, new { workId = second, version = secondVersion } }
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verify = s.Store(s.OrganizationA);
        Assert.Equal(s.VendorA, (await verify.WorkItems.SingleAsync(x => x.Id == s.WorkA)).VendorId);
        Assert.Null((await verify.WorkItems.SingleAsync(x => x.Id == second)).VendorId);
        Assert.Single(await verify.Timeline.ToListAsync());
    }

    [Fact]
    public async Task Work_list_filters_searches_sorts_and_paginates_without_cross_tenant_data()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            var propertyId = AddProperty(store, s.OrganizationA);
            var first = new WorkItem(s.OrganizationA, Guid.NewGuid(), "Alpha plumbing", propertyId, s.AdminA); first.Edit("Alpha plumbing", "leak", null, WorkPriority.High);
            var second = new WorkItem(s.OrganizationA, Guid.NewGuid(), "Zulu pest", propertyId, s.AdminA); second.Edit("Zulu pest", "pest control", null, WorkPriority.Low);
            store.WorkItems.AddRange(first, second); await store.SaveChangesAsync();
        }
        await s.LoginAsync();
        var result = await s.Client.GetFromJsonAsync<JsonElement>("/api/work/?search=pest&sort=title&page=1&pageSize=1");
        Assert.Equal(2, result.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(result.GetProperty("items").EnumerateArray());
        Assert.Equal("Pest control", item.GetProperty("title").GetString());
        Assert.True(item.TryGetProperty("version", out _));
    }

    [Fact]
    public async Task Work_list_carries_vendor_property_and_category_names()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var categoryId = Guid.NewGuid(); var bare = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            store.Categories.Add(new WorkCategory(s.OrganizationA, categoryId, "Pest and vermin", 1));
            var work = await store.WorkItems.SingleAsync(x => x.Id == s.WorkA);
            work.Edit(work.Title, "roaches", categoryId, WorkPriority.Normal);
            store.WorkItems.Add(new WorkItem(s.OrganizationA, bare, "Zulu unassigned", s.PropertyA, s.AdminA));
            await store.SaveChangesAsync();
        }
        await s.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, (await s.AssignAsync(s.WorkA, s.VendorA)).StatusCode);
        var result = await s.Client.GetFromJsonAsync<JsonElement>("/api/work/");
        var items = result.GetProperty("items").EnumerateArray().ToList();
        var assigned = items.Single(x => x.GetProperty("id").GetGuid() == s.WorkA);
        Assert.Equal("Tidewater Pest Services", assigned.GetProperty("vendorName").GetString());
        Assert.Equal("Harbor Point Apartments", assigned.GetProperty("propertyName").GetString());
        Assert.Equal("Pest and vermin", assigned.GetProperty("categoryName").GetString());
        Assert.Equal(await VersionAsync(s, s.WorkA), assigned.GetProperty("version").GetUInt32());
        var unassigned = items.Single(x => x.GetProperty("id").GetGuid() == bare);
        Assert.Equal(JsonValueKind.Null, unassigned.GetProperty("vendorName").ValueKind);
        Assert.Equal(JsonValueKind.Null, unassigned.GetProperty("categoryName").ValueKind);
        Assert.Equal("Harbor Point Apartments", unassigned.GetProperty("propertyName").GetString());
    }

    [Fact]
    public async Task Bulk_assignment_reports_changed_unchanged_and_total_counts()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var second = Guid.NewGuid(); var otherVendor = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            store.WorkItems.Add(new WorkItem(s.OrganizationA, second, "Second pest control", s.PropertyA, s.AdminA));
            store.Vendors.Add(new Vendor(s.OrganizationA, otherVendor, "Other vendor"));
            await store.SaveChangesAsync();
        }
        await s.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, (await s.AssignAsync(s.WorkA, s.VendorA)).StatusCode);
        var response = await s.Client.PostAsJsonAsync("/api/work/bulk/vendor", new
        {
            vendorId = s.VendorA,
            items = new[]
            {
                new { workId = s.WorkA, version = await VersionAsync(s, s.WorkA) },
                new { workId = second, version = await VersionAsync(s, second) }
            }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("changed").GetInt32());
        Assert.Equal(1, body.GetProperty("unchanged").GetInt32());
        Assert.Equal(2, body.GetProperty("total").GetInt32());
        var stale = await s.Client.PostAsJsonAsync("/api/work/bulk/vendor", new
        {
            vendorId = otherVendor,
            items = new[] { new { workId = second, version = 1u } }
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await using var verify = s.Store(s.OrganizationA);
        Assert.Equal(s.VendorA, (await verify.WorkItems.SingleAsync(x => x.Id == second)).VendorId);
    }

    [Fact]
    public async Task Work_list_sorts_by_due_date_in_both_directions()
    {
        await using var s = await fixture.CreateScenarioAsync();
        Guid propertyId;
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            propertyId = AddProperty(store, s.OrganizationA);
            // Due dates run opposite to the titles, so a title sort and a due-date sort cannot agree.
            foreach (var (title, day) in new[] { ("Alpha roof", 3), ("Bravo roof", 2), ("Charlie roof", 1) })
            {
                var work = new WorkItem(s.OrganizationA, Guid.NewGuid(), title, propertyId, s.AdminA);
                work.SetDueDate(new DateTimeOffset(2026, 10, day, 12, 0, 0, TimeSpan.Zero));
                store.WorkItems.Add(work);
            }
            await store.SaveChangesAsync();
        }
        await s.LoginAsync();
        Assert.Equal(new[] { "Alpha roof", "Bravo roof", "Charlie roof" }, await TitlesAsync(s, $"/api/work/?propertyId={propertyId}&sort=title"));
        Assert.Equal(new[] { "Charlie roof", "Bravo roof", "Alpha roof" }, await TitlesAsync(s, $"/api/work/?propertyId={propertyId}&sort=dueDate"));
        Assert.Equal(new[] { "Alpha roof", "Bravo roof", "Charlie roof" }, await TitlesAsync(s, $"/api/work/?propertyId={propertyId}&sort=dueDate&descending=true"));
        Assert.Equal(new[] { "Charlie roof", "Bravo roof", "Alpha roof" }, await TitlesAsync(s, $"/api/work/?propertyId={propertyId}&sort=due"));
        var ascending = await s.Client.GetFromJsonAsync<JsonElement>($"/api/work/?propertyId={propertyId}&sort=dueDate");
        var dueDates = ascending.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("dueDate").GetDateTimeOffset()).ToList();
        Assert.Equal(dueDates.Order(), dueDates);
    }

    [Fact]
    public async Task Employee_assignment_records_a_timeline_entry_and_rejects_an_unknown_employee()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var employeeId = Guid.NewGuid(); var otherEmployee = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            store.Employees.Add(new Employee(s.OrganizationA, employeeId, "Pat Reyes", null, null));
            store.Employees.Add(new Employee(s.OrganizationA, otherEmployee, "Sam Okafor", null, null));
            await store.SaveChangesAsync();
        }
        await s.LoginAsync();
        var staleVersion = await VersionAsync(s, s.WorkA);
        var assigned = await s.Client.PostAsJsonAsync($"/api/work/{s.WorkA}/employee", new { employeeId });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        Assert.True((await assigned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetBoolean());
        var repeated = await s.Client.PostAsJsonAsync($"/api/work/{s.WorkA}/employee", new { employeeId });
        Assert.False((await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsJsonAsync($"/api/work/{s.WorkA}/employee", new { employeeId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsJsonAsync($"/api/work/{s.WorkA}/employee", new { employeeId = otherEmployee, version = staleVersion })).StatusCode);
        await using var verify = s.Store(s.OrganizationA);
        var entry = Assert.Single(await verify.Timeline.Where(x => x.EventType == "EmployeeAssigned").ToListAsync());
        Assert.Equal(s.WorkA, entry.WorkId);
        Assert.Equal(s.AdminA, entry.ActorId);
        Assert.Null(entry.OldValue);
        Assert.Equal(employeeId.ToString(), entry.NewValue);
        var list = await s.Client.GetFromJsonAsync<JsonElement>("/api/work/");
        var item = list.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == s.WorkA);
        Assert.Equal(employeeId, item.GetProperty("employeeId").GetGuid());
        // Enums serialize as strings, not ordinals. The seeded item is never published, so it stays Draft.
        Assert.Equal(JsonValueKind.String, item.GetProperty("status").ValueKind);
        Assert.Equal("Draft", item.GetProperty("status").GetString());
    }

    private static async Task<string[]> TitlesAsync(Scenario s, string url)
    {
        var result = await s.Client.GetFromJsonAsync<JsonElement>(url);
        return [.. result.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("title").GetString()!)];
    }

    [Fact]
    public async Task Read_only_user_cannot_bulk_assign_and_foreign_work_is_concealed()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);
        var denied = await s.Client.PostAsJsonAsync("/api/work/bulk/vendor", new { vendorId = s.VendorA, items = new[] { new { workId = s.WorkA, version = 0u } } });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/work/{s.WorkB}")).StatusCode);
    }

    [Fact]
    public async Task Bulk_assignment_refuses_the_whole_batch_when_any_item_is_terminal()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var cancelled = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            var work = new WorkItem(s.OrganizationA, cancelled, "Duplicate report", s.PropertyA, s.AdminA);
            work.Publish(DateTimeOffset.UtcNow);
            work.ChangeStatus(WorkStatus.Cancelled, DateTimeOffset.UtcNow);
            store.WorkItems.Add(work);
            await store.SaveChangesAsync();
        }
        await s.LoginAsync();

        var response = await s.Client.PostAsJsonAsync("/api/work/bulk/vendor", new
        {
            vendorId = s.VendorA,
            items = new[]
            {
                new { workId = s.WorkA, version = await VersionAsync(s, s.WorkA) },
                new { workId = cancelled, version = await VersionAsync(s, cancelled) }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed and cancelled work cannot be assigned", problem.GetProperty("title").GetString());

        // All-or-nothing: the live item in the same batch must be untouched, not partially applied.
        await using var verify = s.Store(s.OrganizationA);
        Assert.Null((await verify.WorkItems.SingleAsync(x => x.Id == s.WorkA)).VendorId);
        Assert.Null((await verify.WorkItems.SingleAsync(x => x.Id == cancelled)).VendorId);
        Assert.Empty(await verify.Timeline.Where(x => x.WorkId == cancelled).ToListAsync());
    }

    [Fact]
    public async Task Assigning_a_vendor_or_employee_to_terminal_work_returns_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var completed = Guid.NewGuid(); var employee = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            var work = new WorkItem(s.OrganizationA, completed, "Finished repair", s.PropertyA, s.AdminA);
            work.Publish(DateTimeOffset.UtcNow);
            work.ChangeStatus(WorkStatus.Completed, DateTimeOffset.UtcNow);
            store.WorkItems.Add(work);
            store.Employees.Add(new Employee(s.OrganizationA, employee, "Jordan Lee", "jordan@example.test", null));
            await store.SaveChangesAsync();
        }
        await s.LoginAsync();
        var version = await VersionAsync(s, completed);

        var vendor = await s.Client.PostAsJsonAsync($"/api/work/{completed}/vendor", new { vendorId = s.VendorA, version });
        Assert.Equal(HttpStatusCode.BadRequest, vendor.StatusCode);
        var employeeResponse = await s.Client.PostAsJsonAsync($"/api/work/{completed}/employee", new { employeeId = employee, version });
        Assert.Equal(HttpStatusCode.BadRequest, employeeResponse.StatusCode);

        await using var verify = s.Store(s.OrganizationA);
        var stored = await verify.WorkItems.SingleAsync(x => x.Id == completed);
        Assert.Null(stored.VendorId);
        Assert.Null(stored.EmployeeId);
        Assert.Equal(WorkStatus.Completed, stored.Status);
    }

    [Fact]
    public async Task Creating_or_updating_work_rejects_a_child_reference_outside_the_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        Guid categoryId = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            store.Categories.Add(new WorkCategory(s.OrganizationA, categoryId, "Pests", 1));
            await store.SaveChangesAsync();
        }
        await s.LoginAsync();

        // A category id that belongs to no one -> 400, nothing created.
        var bad = await s.Client.PostAsJsonAsync("/api/work/", new
        {
            title = "Roach treatment", propertyId = s.PropertyA, categoryId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // Org B's space id -> also 400 (tenant filter makes it read as unknown).
        var foreign = await s.Client.PostAsJsonAsync("/api/work/", new
        {
            title = "Roach treatment", propertyId = s.PropertyA, spaceId = s.SpaceB
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);

        // A real category from this tenant -> created.
        var ok = await s.Client.PostAsJsonAsync("/api/work/", new
        {
            title = "Roach treatment", propertyId = s.PropertyA, categoryId
        });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        await using var verify = s.Store(s.OrganizationA);
        Assert.Equal(1, await verify.WorkItems.CountAsync(x => x.Title == "Roach treatment"));
    }

    [Fact]
    public async Task Updating_terminal_work_returns_400_and_cannot_reopen_it()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var completed = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            var work = new WorkItem(s.OrganizationA, completed, "Finished repair", s.PropertyA, s.AdminA);
            work.Publish(DateTimeOffset.UtcNow);
            work.AssignVendor(s.VendorA);
            work.ChangeStatus(WorkStatus.Completed, DateTimeOffset.UtcNow);
            store.WorkItems.Add(work);
            await store.SaveChangesAsync();
        }
        await s.LoginAsync();
        var version = await VersionAsync(s, completed);

        var response = await s.Client.PutAsJsonAsync($"/api/work/{completed}", new
        {
            title = "Finished repair", description = (string?)null, categoryId = (Guid?)null,
            priority = "Normal", propertyId = s.PropertyA, buildingId = (Guid?)null, spaceId = (Guid?)null,
            residentId = (Guid?)null, dueDate = (string?)null, cost = (decimal?)null,
            internalNotes = (string?)null, residentVisibleNotes = (string?)null, status = (string?)null,
            scheduledStart = DateTimeOffset.UtcNow.AddDays(1), scheduledEnd = (DateTimeOffset?)null, version
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var verify = s.Store(s.OrganizationA);
        Assert.Equal(WorkStatus.Completed, (await verify.WorkItems.SingleAsync(x => x.Id == completed)).Status);
    }

    [Fact]
    public async Task Work_list_search_treats_like_metacharacters_literally()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            var propertyId = AddProperty(store, s.OrganizationA);
            store.WorkItems.Add(new WorkItem(s.OrganizationA, Guid.NewGuid(), "Unit a_b inspection", propertyId, s.AdminA));
            store.WorkItems.Add(new WorkItem(s.OrganizationA, Guid.NewGuid(), "Unit axb inspection", propertyId, s.AdminA));
            await store.SaveChangesAsync();
        }
        await s.LoginAsync();

        var result = await s.Client.GetFromJsonAsync<JsonElement>("/api/work/?search=a_b");
        var titles = result.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("title").GetString()).ToList();
        Assert.Equal(["Unit a_b inspection"], titles);
    }

    private static async Task<uint> VersionAsync(Scenario s, Guid workId)
    {
        var json = await s.Client.GetFromJsonAsync<JsonElement>($"/api/work/{workId}");
        return json.GetProperty("version").GetUInt32();
    }

    private static Guid AddProperty(PropFlow.Infrastructure.Persistence.OperationsStore store, Guid organization)
    {
        var portfolioId = Guid.NewGuid(); var propertyId = Guid.NewGuid();
        store.Portfolios.Add(new Portfolio(organization, portfolioId, "Test portfolio"));
        store.Properties.Add(new Property(organization, propertyId, portfolioId, "Test property", "America/New_York"));
        return propertyId;
    }
}
