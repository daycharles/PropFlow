using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class AssetIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Assets_are_confined_to_the_current_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var store = s.Store(s.OrganizationA);

        Assert.Equal(s.AssetA, (await store.Assets.SingleAsync()).Id);
        Assert.Equal(s.AssetA, (await store.Assets.IgnoreQueryFilters().SingleAsync()).Id);
    }

    [Fact]
    public async Task Runtime_role_without_a_tenant_context_sees_no_asset_rows()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var connection = new NpgsqlConnection(fixture.RuntimeConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM operations.\"Assets\"";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Rls_with_check_rejects_a_cross_tenant_asset_insert()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var store = s.Store(s.OrganizationA);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO operations."Assets"
              ("OrganizationId", "Id", "PropertyId", "Kind", "Name", "Condition")
            VALUES ({s.OrganizationB}, {Guid.NewGuid()}, {s.PropertyB}, 'Hvac', 'forged', 'Unknown')
            """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task List_detail_and_property_filter_through_the_api()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var all = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/assets");
        Assert.Contains(all!, a => a.GetProperty("id").GetGuid() == s.AssetA);

        var filtered = await s.Client.GetFromJsonAsync<JsonElement[]>($"/api/assets?propertyId={s.PropertyA}");
        Assert.Equal(s.AssetA, Assert.Single(filtered!).GetProperty("id").GetGuid());

        var detail = await s.Client.GetFromJsonAsync<JsonElement>($"/api/assets/{s.AssetA}");
        Assert.Equal("Rooftop HVAC 1", detail.GetProperty("name").GetString());
        Assert.Equal("Hvac", detail.GetProperty("kind").GetString());
        Assert.Equal("Good", detail.GetProperty("condition").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/assets/{s.AssetB}")).StatusCode);
    }

    [Fact]
    public async Task Create_and_update_an_asset()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var create = await s.Client.PostAsJsonAsync("/api/assets", new
        {
            kind = "WaterHeater", name = "Unit 101 water heater", propertyId = s.PropertyA, spaceId = s.SpaceA,
            manufacturer = "Rheem", model = "XE50", serialNumber = "RH-77",
            installedOn = "2022-03-15", warrantyExpiresOn = "2032-03-15", expectedServiceLifeYears = 12,
            condition = "Good", replacementCostEstimate = 1450.00, notes = "In the hall closet"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var update = await s.Client.PutAsJsonAsync($"/api/assets/{id}", new
        {
            kind = "WaterHeater", name = "Unit 101 water heater", propertyId = s.PropertyA, spaceId = s.SpaceA,
            manufacturer = "Rheem", model = "XE50", serialNumber = "RH-77",
            installedOn = "2022-03-15", warrantyExpiresOn = "2032-03-15", expectedServiceLifeYears = 12,
            condition = "Fair", replacementCostEstimate = 1600.50, notes = "Showing rust at the base"
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Fair", updated.GetProperty("condition").GetString());
        Assert.Equal(1600.50m, updated.GetProperty("replacementCostEstimate").GetDecimal());
    }

    [Fact]
    public async Task Invalid_asset_input_and_a_foreign_property_are_400s()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var badName = await s.Client.PostAsJsonAsync("/api/assets", new
        {
            kind = "Hvac", name = "   ", propertyId = s.PropertyA, spaceId = (Guid?)null,
            condition = "Unknown"
        });
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);

        var foreignProperty = await s.Client.PostAsJsonAsync("/api/assets", new
        {
            kind = "Hvac", name = "Forged", propertyId = s.PropertyB, spaceId = (Guid?)null,
            condition = "Unknown"
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreignProperty.StatusCode);
    }

    [Fact]
    public async Task Read_only_role_can_list_but_not_write_assets()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/assets")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsJsonAsync("/api/assets", new
        {
            kind = "Hvac", name = "Nope", propertyId = s.PropertyA, spaceId = (Guid?)null, condition = "Unknown"
        })).StatusCode);
    }
}
