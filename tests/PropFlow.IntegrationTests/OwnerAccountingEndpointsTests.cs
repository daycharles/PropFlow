using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class OwnerAccountingEndpointsTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Budget_approval_and_owner_statement_are_tenant_scoped_and_idempotent()
    {
        await using var s = await fixture.CreateScenarioAsync(); await s.LoginAsync();
        using var ownerResponse = await s.Client.PostAsJsonAsync("/api/owner-accounting/owners", new { name = "Dana Owner", email = $"owner-{Guid.NewGuid():N}@example.test" });
        Assert.Equal(HttpStatusCode.Created, ownerResponse.StatusCode); var owner = (await ownerResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var ownership = await s.Client.PostAsJsonAsync("/api/owner-accounting/ownerships", new { ownerId = owner, propertyId = s.PropertyA, percentage = 100m }); Assert.Equal(HttpStatusCode.Created, ownership.StatusCode);
        using var budget = await s.Client.PostAsJsonAsync("/api/owner-accounting/budgets", new { propertyId = s.PropertyA, year = 2026, name = "2026 plan", lines = Array.Empty<object>() }); Assert.Equal(HttpStatusCode.Created, budget.StatusCode); var budgetId = (await budget.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/owner-accounting/budgets/{budgetId}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/owner-accounting/budgets/{budgetId}/approve", null)).StatusCode);
        var cash = await CreateAccountAsync(s, "1100", "Owner cash", "Asset"); var revenue = await CreateAccountAsync(s, "4100", "Owner revenue", "Revenue"); var periodResponse = await s.Client.PostAsJsonAsync("/api/accounting/periods", new { name = "January 2026", startsOn = "2026-01-01", endsOn = "2026-01-31" }); var period = (await periodResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var journal = await s.Client.PostAsJsonAsync("/api/accounting/journal", new { periodId = period, entryDate = "2026-01-15", reference = "OWNER-1", lines = new[] { new { accountId = cash, propertyId = s.PropertyA, debit = 1000m, credit = 0m }, new { accountId = revenue, propertyId = s.PropertyA, debit = 0m, credit = 1000m } } }); Assert.Equal(HttpStatusCode.Created, journal.StatusCode);
        var request = new { ownerId = owner, propertyId = s.PropertyA, startsOn = "2026-01-01", endsOn = "2026-01-31" };
        using var statement = await s.Client.PostAsJsonAsync("/api/owner-accounting/statements", request); Assert.Equal(HttpStatusCode.Created, statement.StatusCode); var statementJson = await statement.Content.ReadFromJsonAsync<JsonElement>(); var id = statementJson.GetProperty("id").GetGuid(); Assert.Equal(1000m, statementJson.GetProperty("income").GetDecimal()); Assert.Equal(1000m, statementJson.GetProperty("netOwnerAmount").GetDecimal());
        using var repeat = await s.Client.PostAsJsonAsync("/api/owner-accounting/statements", request); Assert.Equal(HttpStatusCode.OK, repeat.StatusCode); Assert.Equal(id, (await repeat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    private static async Task<Guid> CreateAccountAsync(Scenario s, string code, string name, string type)
    {
        using var response = await s.Client.PostAsJsonAsync("/api/accounting/accounts", new { code, name, type }); Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Read_only_member_cannot_manage_owner_accounting()
    {
        await using var s = await fixture.CreateScenarioAsync(); await s.LoginAsync(reader: true);
        using var response = await s.Client.GetAsync("/api/owner-accounting/budgets"); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
