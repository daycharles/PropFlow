using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PropFlow.Domain.Marketing;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class MarketingEndpointsTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Listing_publish_inquiry_duplicate_and_showing_workflow_is_tenant_scoped()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        using var create = await s.Client.PostAsJsonAsync("/api/marketing/listings/", new
        {
            propertyId = s.PropertyA, spaceId = (Guid?)null, headline = "Sunny one bedroom",
            description = "Freshly updated", availableOn = "2026-10-01", monthlyRent = 1850
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var listing = await create.Content.ReadFromJsonAsync<JsonElement>();
        var listingId = listing.GetProperty("id").GetGuid();
        Assert.Equal("Draft", listing.GetProperty("status").GetString());

        using var inquiryBeforePublish = await s.Client.PostAsJsonAsync($"/api/marketing/listings/{listingId}/inquiries", new
        { prospectName = "Alex", email = "alex@example.test", message = "Interested" });
        Assert.Equal(HttpStatusCode.NotFound, inquiryBeforePublish.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/marketing/listings/{listingId}/publish", null)).StatusCode);

        using var inquiry = await s.Client.PostAsJsonAsync($"/api/marketing/listings/{listingId}/inquiries", new
        { prospectName = "Alex", email = "alex@example.test", message = "Interested", leadSource = "Website" });
        Assert.Equal(HttpStatusCode.Created, inquiry.StatusCode);
        var createdInquiry = await inquiry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Website", createdInquiry.GetProperty("leadSource").GetString());
        using var duplicate = await s.Client.PostAsJsonAsync($"/api/marketing/listings/{listingId}/inquiries", new
        { prospectName = "Alex", email = "alex@example.test", message = "Again" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var applicant = await s.Client.PostAsJsonAsync($"/api/marketing/listings/{listingId}/applicants", new { inquiryId = createdInquiry.GetProperty("id").GetGuid() });
        Assert.Equal(HttpStatusCode.Created, applicant.StatusCode);
        var createdApplicant = await applicant.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("New", createdApplicant.GetProperty("status").GetString());
        using var screening = await s.Client.PutAsJsonAsync($"/api/marketing/listings/{listingId}/applicants/{createdApplicant.GetProperty("id").GetGuid()}/status", new { status = "Screening" });
        Assert.Equal(HttpStatusCode.OK, screening.StatusCode);

        using var showing = await s.Client.PostAsJsonAsync($"/api/marketing/listings/{listingId}/showings", new
        { prospectName = "Alex", scheduledAt = "2026-09-20T14:00:00Z" });
        Assert.Equal(HttpStatusCode.Created, showing.StatusCode);
        var listings = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/marketing/listings/?status=Published");
        Assert.Contains(listings!, item => item.GetProperty("id").GetGuid() == listingId);

        using var occupiedCreate = await s.Client.PostAsJsonAsync("/api/marketing/listings/", new
        {
            propertyId = s.PropertyA, spaceId = s.SpaceA, headline = "Occupied listing",
            availableOn = "2026-10-01", monthlyRent = 1900
        });
        var occupied = await occupiedCreate.Content.ReadFromJsonAsync<JsonElement>();
        using var occupiedPublish = await s.Client.PostAsync($"/api/marketing/listings/{occupied.GetProperty("id").GetGuid()}/publish", null);
        Assert.Equal(HttpStatusCode.Conflict, occupiedPublish.StatusCode);

        using var otherTenant = await s.AttemptLoginAsync(s.EmailB, s.OrganizationB);
        Assert.Equal(HttpStatusCode.NoContent, otherTenant.StatusCode);
        await s.RefreshCsrfAsync();
        var hidden = await s.Client.GetFromJsonAsync<JsonElement[]>($"/api/marketing/listings/?propertyId={s.PropertyA}");
        Assert.Empty(hidden!);
    }
}
