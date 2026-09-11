using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class LeasingEndpointsTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Lease_documents_support_send_and_sign_lifecycle()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        using var createLease = await s.Client.PostAsJsonAsync("/api/leasing/leases/", new { residentId = s.ResidentA, spaceId = s.SpaceA, startsOn = "2026-01-15", endsOn = "2026-12-31", monthlyRent = 1650, securityDeposit = 1650 });
        var leaseId = (await createLease.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var createDocument = await s.Client.PostAsJsonAsync($"/api/leasing/leases/{leaseId}/documents", new { title = "Residential lease", documentUrl = "https://docs.example.test/leases/1", residentVisible = true });
        Assert.Equal(HttpStatusCode.Created, createDocument.StatusCode);
        var document = await createDocument.Content.ReadFromJsonAsync<JsonElement>();
        var documentId = document.GetProperty("id").GetGuid();
        Assert.Equal("Draft", document.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/leasing/leases/{leaseId}/documents/{documentId}/send", null)).StatusCode);
        using var sign = await s.Client.PostAsJsonAsync($"/api/leasing/leases/{leaseId}/documents/{documentId}/sign", new { signedBy = "Resident A" });
        Assert.Equal(HttpStatusCode.OK, sign.StatusCode);
        Assert.Equal("Signed", (await sign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        using var listed = await s.Client.GetAsync($"/api/leasing/leases/{leaseId}/documents");
        Assert.Contains("Residential lease", await listed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Active_lease_transfer_moves_occupancy_and_is_tenant_scoped()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var targetResident = Guid.NewGuid();
        var targetSpace = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            store.Residents.Add(new PropFlow.Domain.People.Resident(s.OrganizationA, targetResident, "Transfer Resident", "transfer@example.test", null));
            store.Spaces.Add(new PropFlow.Domain.Properties.Space(s.OrganizationA, targetSpace, s.PropertyA, null, "202"));
            await store.SaveChangesAsync();
        }
        await s.LoginAsync();
        using var create = await s.Client.PostAsJsonAsync("/api/leasing/leases/", new { residentId = s.ResidentA, spaceId = s.SpaceA, startsOn = "2026-01-15", endsOn = "2026-12-31", monthlyRent = 1650, securityDeposit = 1650 });
        var leaseId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/leasing/leases/{leaseId}/activate", null)).StatusCode);
        using var transfer = await s.Client.PostAsJsonAsync($"/api/leasing/leases/{leaseId}/transfer", new { residentId = targetResident, spaceId = targetSpace, effectiveOn = "2026-06-01" });
        Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);
        var transferred = await transfer.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(targetResident, transferred.GetProperty("residentId").GetGuid());
        await using var verify = s.AdminStore(s.OrganizationA);
        Assert.NotNull(await verify.Occupancies.SingleOrDefaultAsync(x => x.ResidentId == s.ResidentA && x.SpaceId == s.SpaceA && x.MovedOutOn == new DateOnly(2026, 6, 1)));
        Assert.NotNull(await verify.Occupancies.SingleOrDefaultAsync(x => x.ResidentId == targetResident && x.SpaceId == targetSpace && x.MovedOutOn == null));
    }

    [Fact]
    public async Task Lease_activation_renewal_notice_and_move_out_are_tenant_scoped()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        using var create = await s.Client.PostAsJsonAsync("/api/leasing/leases/", new
        {
            residentId = s.ResidentA, spaceId = s.SpaceA, startsOn = "2026-01-15", endsOn = "2026-12-31",
            monthlyRent = 1650, securityDeposit = 1650
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var lease = await create.Content.ReadFromJsonAsync<JsonElement>();
        var leaseId = lease.GetProperty("id").GetGuid();
        Assert.Equal("Draft", lease.GetProperty("status").GetString());
        using var charge = await s.Client.PostAsJsonAsync($"/api/leasing/leases/{leaseId}/charges", new { type = "Recurring", description = "Monthly rent", amount = 1650, dueOn = "2026-02-01" });
        Assert.Equal(HttpStatusCode.Created, charge.StatusCode);
        using var charges = await s.Client.GetAsync($"/api/leasing/leases/{leaseId}/charges");
        Assert.Contains("Monthly rent", await charges.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/leasing/leases/{leaseId}/activate", null)).StatusCode);
        using var renewal = await s.Client.PostAsJsonAsync($"/api/leasing/leases/{leaseId}/renew", new { endsOn = "2027-12-31", monthlyRent = 1725 });
        Assert.Equal(HttpStatusCode.OK, renewal.StatusCode);
        Assert.Equal("Renewed", (await renewal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        using var notice = await s.Client.PostAsJsonAsync($"/api/leasing/leases/{leaseId}/notice", new
        { type = "MoveOut", noticeDate = "2027-06-01", moveOutOn = "2027-07-01", notes = "Resident provided notice" });
        Assert.Equal(HttpStatusCode.OK, notice.StatusCode);
        using var moveOut = await s.Client.PostAsJsonAsync($"/api/leasing/leases/{leaseId}/move-out", new { moveOutOn = "2027-07-01" });
        Assert.Equal(HttpStatusCode.OK, moveOut.StatusCode);
        Assert.Equal("Ended", (await moveOut.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            var occupancy = await store.Occupancies.SingleAsync(x => x.ResidentId == s.ResidentA && x.SpaceId == s.SpaceA && x.MovedOutOn != null);
            Assert.Equal(new DateOnly(2027, 7, 1), occupancy.MovedOutOn);
        }

        using var otherTenant = await s.AttemptLoginAsync(s.EmailB, s.OrganizationB);
        Assert.Equal(HttpStatusCode.NoContent, otherTenant.StatusCode);
        await s.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/leasing/leases/{leaseId}")).StatusCode);
    }
}
