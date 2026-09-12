using System.Net;
using System.Net.Http.Json;
using PropFlow.Domain.Procurement;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class ProcurementEndpointsTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Expired_vendor_document_blocks_purchase_order_submission()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        await s.Client.PostAsJsonAsync($"/api/procurement/vendors/{s.VendorA}/documents", new { type = VendorDocumentType.Insurance, documentNumber = "expired-1", expiresOn = "2020-01-01" });
        var create = await s.Client.PostAsJsonAsync("/api/procurement/purchase-orders", new { vendorId = s.VendorA, propertyId = s.PropertyA, number = $"PO-{Guid.NewGuid():N}", amount = 1200, approvalThreshold = 5000 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var po = await create.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var submit = await s.Client.PostAsync($"/api/procurement/purchase-orders/{po.GetProperty("id").GetGuid()}/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
    }

    [Fact]
    public async Task Read_only_cannot_mutate_procurement()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);
        var response = await s.Client.PostAsJsonAsync("/api/procurement/bids", new { vendorId = s.VendorA, title = "Denied", amount = 10 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
