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
        Assert.Equal("Pest control", item.GetProperty("item").GetProperty("title").GetString());
        Assert.True(item.TryGetProperty("version", out _));
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
