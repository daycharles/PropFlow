using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class AccountingAcceptanceRouteTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Payable_receivable_and_bank_account_workflows_are_operational()
    {
        await using var s = await fixture.CreateScenarioAsync(); await s.LoginAsync();
        using var payable = await s.Client.PostAsJsonAsync("/api/accounting/payables", new { vendorId = s.VendorA, invoiceNumber = "AP-route-1", amount = 125m, dueOn = "2026-12-01" }); Assert.Equal(HttpStatusCode.Created, payable.StatusCode);
        var payableId = (await payable.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/accounting/payables")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsJsonAsync($"/api/accounting/payables/{payableId}/pay", new { amount = 125m })).StatusCode);
        using var receivable = await s.Client.PostAsJsonAsync("/api/accounting/receivables", new { residentId = s.ResidentA, invoiceNumber = "AR-route-1", amount = 200m, dueOn = "2026-12-01" }); Assert.Equal(HttpStatusCode.Created, receivable.StatusCode);
        var receivableId = (await receivable.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/accounting/receivables")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsJsonAsync($"/api/accounting/receivables/{receivableId}/receive", new { amount = 200m })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsJsonAsync($"/api/accounting/payables/{Guid.NewGuid()}/void", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsJsonAsync($"/api/accounting/receivables/{Guid.NewGuid()}/void", new { })).StatusCode);
    }

    [Fact]
    public async Task Bank_account_and_transaction_routes_are_tenant_scoped()
    {
        await using var s = await fixture.CreateScenarioAsync(); await s.LoginAsync();
        using var account = await s.Client.PostAsJsonAsync("/api/accounting/bank-accounts", new { name = "Operating", institution = "Test Bank", lastFour = "1234", assetAccountId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, account.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/accounting/bank-accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync($"/api/accounting/bank-accounts/{Guid.NewGuid()}/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsJsonAsync($"/api/accounting/bank-transactions/{Guid.NewGuid()}/match", new { journalEntryId = Guid.NewGuid() })).StatusCode);
    }
}
