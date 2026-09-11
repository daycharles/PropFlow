using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using PropFlow.Domain.Leasing;
using PropFlow.Domain.Communications;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Identity;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class ResidentPortalTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Bound_resident_can_read_portal_and_create_a_service_request_only_for_current_space()
    {
        await using var s = await fixture.CreateScenarioAsync();
        Guid chargeId;
        await using (var seedStore = s.AdminStore(s.OrganizationA))
        {
            var residentLease = new Lease(s.OrganizationA, Guid.NewGuid(), s.ResidentA, s.SpaceA, new DateOnly(2026, 1, 15), new DateOnly(2026, 12, 31), 1650, 1650);
            residentLease.Activate();
            seedStore.Leases.Add(residentLease);
            chargeId = Guid.NewGuid();
            seedStore.LeaseCharges.Add(new LeaseCharge(s.OrganizationA, chargeId, residentLease.Id, LeaseChargeType.Recurring, "Monthly rent", 1650, new DateOnly(2026, 2, 1)));
            var announcement = new Announcement(s.OrganizationA, Guid.NewGuid(), "Planned water shutdown", "Water will be unavailable Saturday morning.", null);
            announcement.Publish();
            seedStore.Announcements.Add(announcement);
            await seedStore.SaveChangesAsync();
        }
        var userId = Guid.NewGuid(); var email = $"portal-{userId:N}@example.test";
        await using (var identity = s.Identity())
        {
            var user = new ApplicationUser { Id = userId, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true };
            user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, s.Password);
            identity.Users.Add(user); identity.Memberships.Add(new OrganizationMembership { OrganizationId = s.OrganizationA, UserId = userId, Role = "Resident", ResidentId = s.ResidentA }); await identity.SaveChangesAsync();
        }
        using var login = await s.AttemptLoginAsync(email, s.OrganizationA);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode); await s.RefreshCsrfAsync();
        var portal = await s.Client.GetFromJsonAsync<JsonElement>("/api/portal/me");
        Assert.Equal(s.ResidentA, portal.GetProperty("resident").GetProperty("id").GetGuid());
        Assert.NotEmpty(portal.GetProperty("leases").EnumerateArray());
        using var charges = await s.Client.GetAsync("/api/portal/charges");
        Assert.Equal(HttpStatusCode.OK, charges.StatusCode);
        Assert.Contains("Monthly rent", await charges.Content.ReadAsStringAsync());
        using var announcements = await s.Client.GetAsync("/api/portal/announcements");
        Assert.Equal(HttpStatusCode.OK, announcements.StatusCode);
        var announcementList = await announcements.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(announcementList.EnumerateArray(), item => item.GetProperty("title").GetString() == "Planned water shutdown");
        using var profile = await s.Client.PutAsJsonAsync("/api/portal/profile", new { fullName = "Updated Resident", email = "updated@example.test", phone = "+15555550123" });
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var preferences = await s.Client.PutAsJsonAsync("/api/portal/preferences", new { emailEnabled = true, smsEnabled = false });
        Assert.Equal(HttpStatusCode.OK, preferences.StatusCode);
        await using var profileStore = s.AdminStore(s.OrganizationA);
        var updatedResident = await profileStore.Residents.FindAsync(s.OrganizationA, s.ResidentA);
        Assert.Equal("Updated Resident", updatedResident!.FullName);
        Assert.Equal("Granted", updatedResident.EmailConsent.ToString());
        using var household = await s.Client.PostAsJsonAsync("/api/portal/household-members", new { fullName = "Jamie Resident", relationship = "Child", email = "jamie@example.test" });
        Assert.Equal(HttpStatusCode.Created, household.StatusCode);
        var refreshedPortal = await s.Client.GetFromJsonAsync<JsonElement>("/api/portal/me");
        Assert.Contains(refreshedPortal.GetProperty("householdMembers").EnumerateArray(), item => item.GetProperty("fullName").GetString() == "Jamie Resident");
        using var request = await s.Client.PostAsJsonAsync("/api/portal/service-requests", new { spaceId = s.SpaceA, title = "Kitchen faucet is leaking", description = "Please schedule a repair." });
        Assert.Equal(HttpStatusCode.Created, request.StatusCode);
        var created = await request.Content.ReadFromJsonAsync<JsonElement>();
        await using var store = s.AdminStore(s.OrganizationA);
        var work = await store.WorkItems.FindAsync(s.OrganizationA, created.GetProperty("id").GetGuid());
        Assert.Equal(s.ResidentA, work!.ResidentId); Assert.Equal("New", work.Status.ToString());
        store.Attachments.Add(Attachment.Create(s.OrganizationA, Guid.NewGuid(), work.Id, "lease-packet.pdf", "application/pdf", 10, $"{s.OrganizationA:N}/{work.Id:N}/lease-packet.pdf", true, DateTimeOffset.UtcNow, null));
        await store.SaveChangesAsync();
        using var documents = await s.Client.GetAsync("/api/portal/documents");
        Assert.Equal(HttpStatusCode.OK, documents.StatusCode);
        var documentList = await documents.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(documentList.EnumerateArray(), item => item.GetProperty("fileName").GetString() == "lease-packet.pdf");
        var leaseId = portal.GetProperty("leases").EnumerateArray().First().GetProperty("id").GetGuid();
        using var payment = await s.Client.PostAsJsonAsync("/api/portal/payments", new { leaseId, chargeId, amount = 1650, dueOn = "2026-03-01", reference = "March rent" });
        Assert.Equal(HttpStatusCode.Created, payment.StatusCode);
        var paymentJson = await payment.Content.ReadFromJsonAsync<JsonElement>();
        var paymentId = paymentJson.GetProperty("id").GetGuid();
        Assert.Equal(chargeId, paymentJson.GetProperty("chargeId").GetGuid());
        using var payments = await s.Client.GetAsync("/api/portal/payments");
        Assert.Contains("March rent", await payments.Content.ReadAsStringAsync());
        var managerPayment = new ResidentPayment(s.OrganizationA, Guid.NewGuid(), leaseId, s.ResidentA, 100, new DateOnly(2026, 4, 1), "manager-settlement");
        store.ResidentPayments.Add(managerPayment);
        await store.SaveChangesAsync();
        await s.LoginAsync();
        using var settledChargePayment = await s.Client.PostAsync($"/api/leasing/leases/payments/{paymentId}/settle", null);
        Assert.Equal(HttpStatusCode.OK, settledChargePayment.StatusCode);
        await using var chargeStore = s.AdminStore(s.OrganizationA);
        Assert.Equal(LeaseChargeStatus.Paid, (await chargeStore.LeaseCharges.FindAsync(s.OrganizationA, chargeId))!.Status);
        using var settled = await s.Client.PostAsync($"/api/leasing/leases/payments/{managerPayment.Id}/settle", null);
        Assert.Equal(HttpStatusCode.OK, settled.StatusCode);
        Assert.Equal("Settled", (await settled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        using var residentLogin = await s.AttemptLoginAsync(email, s.OrganizationA);
        Assert.Equal(HttpStatusCode.NoContent, residentLogin.StatusCode);
        await s.RefreshCsrfAsync();
        using var forbidden = await s.Client.PostAsJsonAsync("/api/portal/service-requests", new { spaceId = s.SpaceB, title = "Cross tenant request", description = "No" });
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
    }
}
