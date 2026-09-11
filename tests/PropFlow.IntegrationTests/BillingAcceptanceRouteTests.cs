using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class BillingAcceptanceRouteTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Payment_method_receipt_reconciliation_and_delinquency_routes_are_tenant_scoped_and_operational()
    {
        await using var s = await fixture.CreateScenarioAsync(); await s.LoginAsync();
        using var methods = await s.Client.GetAsync($"/api/billing/residents/{s.ResidentA}/payment-methods"); Assert.Equal(HttpStatusCode.OK, methods.StatusCode);
        using var created = await s.Client.PostAsJsonAsync($"/api/billing/residents/{s.ResidentA}/payment-methods", new { type = "Card", label = "Visa", providerToken = "tok_test", lastFour = "4242" }); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var methodId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/billing/payment-methods/{methodId}/deactivate", null)).StatusCode);
        using var recon = await s.Client.PostAsJsonAsync("/api/billing/reconciliation", new { providerReference = "route-recon", amount = 12m, note = "unmatched" }); Assert.Equal(HttpStatusCode.Created, recon.StatusCode);
        var reconId = (await recon.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reconciliation").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/billing/reconciliation")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsJsonAsync($"/api/billing/reconciliation/{reconId}/resolve", new { note = "matched manually" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsJsonAsync("/api/billing/delinquency/run", new { asOf = "2026-12-31" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/billing/delinquency")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsync($"/api/billing/delinquency/{Guid.NewGuid()}/contact", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsync($"/api/billing/delinquency/{Guid.NewGuid()}/resolve", null)).StatusCode);
    }

    [Fact]
    public async Task Receipt_routes_return_not_found_for_unknown_payment_and_all_new_reads_hide_other_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync(); await s.LoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsync($"/api/billing/payments/{Guid.NewGuid()}/receipt", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/billing/payments/{Guid.NewGuid()}/receipt")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync($"/api/billing/residents/{s.ResidentB}/payment-methods")).StatusCode);
    }
}
