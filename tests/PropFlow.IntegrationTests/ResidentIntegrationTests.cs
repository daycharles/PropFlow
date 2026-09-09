using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class ResidentIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Residents_and_occupancies_are_confined_to_the_current_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var store = s.Store(s.OrganizationA);

        Assert.Equal(s.ResidentA, (await store.Residents.SingleAsync()).Id);
        Assert.Equal(s.ResidentA, (await store.Residents.IgnoreQueryFilters().SingleAsync()).Id);
        Assert.Equal(s.SpaceA, (await store.Occupancies.IgnoreQueryFilters().SingleAsync()).SpaceId);
    }

    [Fact]
    public async Task Runtime_role_without_a_tenant_context_sees_no_resident_rows()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var connection = new NpgsqlConnection(fixture.RuntimeConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM operations.\"Residents\"";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Rls_with_check_rejects_a_cross_tenant_resident_insert()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using var store = s.Store(s.OrganizationA);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => store.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO operations."Residents"
              ("OrganizationId", "Id", "FullName", "SmsConsent", "EmailConsent")
            VALUES ({s.OrganizationB}, {Guid.NewGuid()}, 'forged', 'Unknown', 'Unknown')
            """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task Resident_list_detail_and_occupancies_through_the_api()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var list = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/residents");
        Assert.Contains(list!, r => r.GetProperty("id").GetGuid() == s.ResidentA);

        var detail = await s.Client.GetFromJsonAsync<JsonElement>($"/api/residents/{s.ResidentA}");
        Assert.Equal("Dana Reyes", detail.GetProperty("fullName").GetString());
        Assert.Equal("Granted", detail.GetProperty("smsConsent").GetString());

        var occupancies = await s.Client.GetFromJsonAsync<JsonElement[]>($"/api/residents/{s.ResidentA}/occupancies");
        Assert.Equal(s.SpaceA, Assert.Single(occupancies!).GetProperty("spaceId").GetGuid());
    }

    [Fact]
    public async Task Api_conceals_another_organizations_resident()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/residents/{s.ResidentB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/residents/{s.ResidentB}/occupancies")).StatusCode);
    }

    [Fact]
    public async Task Read_only_role_can_list_but_not_write_residents()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/residents")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Client.PostAsJsonAsync("/api/residents", new { fullName = "Nope", email = (string?)null, phone = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task Create_update_and_set_consent_on_a_resident()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var created = await (await s.Client.PostAsJsonAsync("/api/residents",
            new { fullName = "Jordan Lee", email = "jordan@example.test", phone = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("Unknown", created.GetProperty("emailConsent").GetString());

        var updated = await (await s.Client.PutAsJsonAsync($"/api/residents/{id}",
            new { fullName = "Jordan Lee", email = "jordan@example.test", phone = "+15550109999" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("+15550109999", updated.GetProperty("phone").GetString());

        var consented = await (await s.Client.PutAsJsonAsync($"/api/residents/{id}/consent",
            new { channel = "Sms", granted = true })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Granted", consented.GetProperty("smsConsent").GetString());

        var revoked = await (await s.Client.PutAsJsonAsync($"/api/residents/{id}/consent",
            new { channel = "Sms", granted = false })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Revoked", revoked.GetProperty("smsConsent").GetString());
    }

    [Fact]
    public async Task Invalid_resident_input_is_a_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var response = await s.Client.PostAsJsonAsync("/api/residents",
            new { fullName = "   ", email = (string?)null, phone = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Start_and_end_an_occupancy()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var residentId = (await (await s.Client.PostAsJsonAsync("/api/residents",
            new { fullName = "Pat Quinn", email = (string?)null, phone = "+15550107777" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var start = await s.Client.PostAsJsonAsync($"/api/residents/{residentId}/occupancies",
            new { spaceId = s.SpaceA, movedInOn = "2026-03-01" });
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var occupancyId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var end = await s.Client.PutAsJsonAsync($"/api/residents/{residentId}/occupancies/{occupancyId}/end",
            new { movedOutOn = "2026-02-01" });
        Assert.Equal(HttpStatusCode.BadRequest, end.StatusCode); // before move-in

        var ok = await s.Client.PutAsJsonAsync($"/api/residents/{residentId}/occupancies/{occupancyId}/end",
            new { movedOutOn = "2026-09-01" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task An_occupancy_cannot_reference_another_organizations_space()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var residentId = (await (await s.Client.PostAsJsonAsync("/api/residents",
            new { fullName = "Robin Vale", email = (string?)null, phone = "+15550106666" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await s.Client.PostAsJsonAsync($"/api/residents/{residentId}/occupancies",
            new { spaceId = s.SpaceB, movedInOn = "2026-03-01" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
