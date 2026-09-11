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
            store.Occupancies.Add(new Occupancy(s.OrganizationA, Guid.NewGuid(), s.ResidentA, space, new DateOnly(2026, 1, 15)));
            store.Employees.Add(new Employee(s.OrganizationA, employee, "Casey Lee", "casey@example.test", null));
            await store.SaveChangesAsync();
        }

        await s.LoginAsync();
        var portfolios = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/portfolios/");
        Assert.Contains(portfolios!, item => item.GetProperty("id").GetGuid() == portfolio);
        using var createPortfolio = await s.Client.PostAsJsonAsync("/api/portfolios/", new { name = "North Portfolio" });
        Assert.Equal(HttpStatusCode.Created, createPortfolio.StatusCode);
        var createdPortfolio = (await createPortfolio.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var createProperty = await s.Client.PostAsJsonAsync("/api/properties/", new
        {
            portfolioId = createdPortfolio, name = "North House", timeZoneId = "America/New_York"
        });
        Assert.Equal(HttpStatusCode.Created, createProperty.StatusCode);
        var createdProperty = (await createProperty.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.PostAsync($"/api/properties/{createdProperty}/archive", null)).StatusCode);
        var activeProperties = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/properties/");
        Assert.DoesNotContain(activeProperties!, item => item.GetProperty("id").GetGuid() == createdProperty);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.PostAsync($"/api/properties/{createdProperty}/restore", null)).StatusCode);
        using var createBuilding = await s.Client.PostAsJsonAsync($"/api/properties/{createdProperty}/buildings", new { name = "Main Building" });
        Assert.Equal(HttpStatusCode.Created, createBuilding.StatusCode);
        var createdBuilding = (await createBuilding.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var createSpace = await s.Client.PostAsJsonAsync($"/api/properties/{createdProperty}/spaces", new { buildingId = createdBuilding, code = "101" });
        Assert.Equal(HttpStatusCode.Created, createSpace.StatusCode);
        using var foreignBuilding = await s.Client.PostAsJsonAsync($"/api/properties/{createdProperty}/spaces", new { buildingId = building, code = "102" });
        Assert.Equal(HttpStatusCode.BadRequest, foreignBuilding.StatusCode);
        using var foreignPortfolio = await s.Client.PostAsJsonAsync("/api/properties/", new
        {
            portfolioId = s.PortfolioB, name = "Cross Tenant", timeZoneId = "America/New_York"
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreignPortfolio.StatusCode);
        var vendors = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/vendors/");
        Assert.Equal(s.VendorA, Assert.Single(vendors!).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/vendors/{s.VendorB}")).StatusCode);
        var employees = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/employees/");
        Assert.Equal(employee, Assert.Single(employees!).GetProperty("id").GetGuid());

        var detail = await s.Client.GetFromJsonAsync<JsonElement>($"/api/properties/{property}");
        Assert.Equal(property, detail.GetProperty("property").GetProperty("id").GetGuid());
        Assert.Equal(building, Assert.Single(detail.GetProperty("buildings").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(space, Assert.Single(detail.GetProperty("spaces").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.True(Assert.Single(detail.GetProperty("spaces").EnumerateArray()).GetProperty("isOccupied").GetBoolean());
        using var contact = await s.Client.PostAsJsonAsync($"/api/properties/{property}/contacts", new { fullName = "Morgan Manager", role = "Property manager", email = "morgan@example.test", phone = "+15555550000" });
        Assert.Equal(HttpStatusCode.Created, contact.StatusCode);
        var detailWithContact = await s.Client.GetFromJsonAsync<JsonElement>($"/api/properties/{property}");
        Assert.Contains(detailWithContact.GetProperty("contacts").EnumerateArray(), item => item.GetProperty("fullName").GetString() == "Morgan Manager");
        using var document = await s.Client.PostAsJsonAsync($"/api/properties/{property}/documents", new { title = "Building rules", documentUrl = "https://docs.example.test/rules", documentType = "Policy" });
        Assert.Equal(HttpStatusCode.Created, document.StatusCode);
        var detailWithDocument = await s.Client.GetFromJsonAsync<JsonElement>($"/api/properties/{property}");
        Assert.Contains(detailWithDocument.GetProperty("documents").EnumerateArray(), item => item.GetProperty("title").GetString() == "Building rules");
    }
}
