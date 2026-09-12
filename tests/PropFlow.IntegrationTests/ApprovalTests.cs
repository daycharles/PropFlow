using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PropFlow.Domain.Approvals;
using Xunit;

namespace PropFlow.IntegrationTests;

// PF-S03.05 - the generic approval-request primitive.
[Collection("PostgreSQL")]
public sealed class ApprovalTests(DatabaseFixture fixture)
{
    // ApprovalRequest.Decide refuses a self-decision, and every logged-in test client in this
    // fixture is the same admin user - so tests that exercise a real decision seed the request
    // directly, as if requested by someone else, rather than going through the API to create it.
    private static async Task<Guid> SeedRequestAsync(Scenario s, string subjectType, Guid subjectId, string? note = null)
    {
        await using var admin = s.AdminStore(s.OrganizationA);
        var request = ApprovalRequest.Create(s.OrganizationA, Guid.NewGuid(), subjectType, subjectId, Guid.NewGuid(), note, DateTimeOffset.UtcNow);
        admin.ApprovalRequests.Add(request);
        await admin.SaveChangesAsync();
        return request.Id;
    }

    [Fact]
    public async Task Requesting_and_listing_an_approval_round_trips_it()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var subjectId = Guid.NewGuid();
        var created = await s.Client.PostAsJsonAsync("/api/approvals/", new { subjectType = "Budget", subjectId, note = "Q1 plan" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        var id = body.GetProperty("id").GetGuid();

        var list = await s.Client.GetFromJsonAsync<JsonElement[]>($"/api/approvals/?subjectType=Budget&subjectId={subjectId}");
        Assert.Contains(list!, x => x.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Approving_a_request_records_the_decider_and_status()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var id = await SeedRequestAsync(s, "Budget", Guid.NewGuid());

        var decided = await s.Client.PostAsJsonAsync($"/api/approvals/{id}/decide", new { approved = true, reason = "Looks good" });
        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        var body = await decided.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", body.GetProperty("status").GetString());
        Assert.Equal("Looks good", body.GetProperty("decisionReason").GetString());
    }

    [Fact]
    public async Task Rejecting_a_request_records_the_status()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var id = await SeedRequestAsync(s, "Budget", Guid.NewGuid());

        var decided = await s.Client.PostAsJsonAsync($"/api/approvals/{id}/decide", new { approved = false, reason = "Not this quarter" });
        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        Assert.Equal("Rejected", (await decided.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_decided_request_cannot_be_decided_again()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var id = await SeedRequestAsync(s, "Budget", Guid.NewGuid());
        await s.Client.PostAsJsonAsync($"/api/approvals/{id}/decide", new { approved = true, reason = (string?)null });

        var again = await s.Client.PostAsJsonAsync($"/api/approvals/{id}/decide", new { approved = true, reason = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task An_unknown_subject_type_is_still_accepted_and_an_empty_one_is_a_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        // v1 keeps SubjectType a free-text, validated string (like TimelineEntry.RelatedObjectType)
        // rather than a closed enum - there is no allow-list to reject an unrecognized value against.
        var anySubject = await s.Client.PostAsJsonAsync("/api/approvals/", new { subjectType = "SomethingNotYetBuilt", subjectId = Guid.NewGuid(), note = (string?)null });
        Assert.Equal(HttpStatusCode.Created, anySubject.StatusCode);

        var blank = await s.Client.PostAsJsonAsync("/api/approvals/", new { subjectType = "", subjectId = Guid.NewGuid(), note = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
    }

    [Fact]
    public async Task Read_only_users_can_request_and_read_but_not_decide()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);

        var list = await s.Client.GetAsync("/api/approvals/");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var created = await s.Client.PostAsJsonAsync("/api/approvals/", new { subjectType = "Budget", subjectId = Guid.NewGuid(), note = (string?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var decide = await s.Client.PostAsJsonAsync($"/api/approvals/{id}/decide", new { approved = true, reason = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, decide.StatusCode);
    }

    [Fact]
    public async Task Approvals_are_tenant_scoped()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var subjectId = Guid.NewGuid();
        await using (var admin = s.AdminStore(s.OrganizationA))
        {
            admin.ApprovalRequests.Add(PropFlow.Domain.Approvals.ApprovalRequest.Create(
                s.OrganizationA, Guid.NewGuid(), "Budget", subjectId, s.AdminA, null, DateTimeOffset.UtcNow));
            await admin.SaveChangesAsync();
        }
        await s.LoginAsync(); // OrganizationA
        var ownList = await s.Client.GetFromJsonAsync<JsonElement[]>($"/api/approvals/?subjectId={subjectId}");
        Assert.Single(ownList!);

        using (var logout = await s.Client.PostAsync("/api/auth/logout", null)) Assert.True(logout.IsSuccessStatusCode);
        using (var login = await s.AttemptLoginAsync(s.EmailB, s.OrganizationB)) Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await s.RefreshCsrfAsync();
        var otherList = await s.Client.GetFromJsonAsync<JsonElement[]>($"/api/approvals/?subjectId={subjectId}");
        Assert.Empty(otherList!);
    }
}
