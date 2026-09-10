using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PropFlow.Domain.People;
using PropFlow.Domain.Properties;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class ReferenceEndpointsTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Reference_reads_are_tenant_scoped_and_property_detail_includes_hierarchy()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var portfolio = Guid.NewGuid();
        var property = Guid.NewGuid();
        var building = Guid.NewGuid();
        var space = Guid.NewGuid();
        var employee = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            store.Portfolios.Add(new Portfolio(s.OrganizationA, portfolio, "East"));
            store.Properties.Add(new Property(s.OrganizationA, property, portfolio, "Harbor House", "America/New_York"));
            store.Buildings.Add(new Building(s.OrganizationA, building, property, "Tower A"));
            store.Spaces.Add(new Space(s.OrganizationA, space, property, building, "101"));
            store.Employees.Add(new Employee(s.OrganizationA, employee, "Casey Lee", "casey@example.test", null));
            await store.SaveChangesAsync();
        }

        await s.LoginAsync();
        var vendors = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/vendors/");
        Assert.Equal(s.VendorA, Assert.Single(vendors!).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/vendors/{s.VendorB}")).StatusCode);
        var employees = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/employees/");
        Assert.Equal(employee, Assert.Single(employees!).GetProperty("id").GetGuid());

        var detail = await s.Client.GetFromJsonAsync<JsonElement>($"/api/properties/{property}");
        Assert.Equal(property, detail.GetProperty("property").GetProperty("id").GetGuid());
        Assert.Equal(building, Assert.Single(detail.GetProperty("buildings").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(space, Assert.Single(detail.GetProperty("spaces").EnumerateArray()).GetProperty("id").GetGuid());
    }
}
