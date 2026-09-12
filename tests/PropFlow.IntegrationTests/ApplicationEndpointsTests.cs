using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Domain.Marketing;
using PropFlow.Infrastructure.Identity;
using Xunit;

namespace PropFlow.IntegrationTests;

// PF-S05.08, the FS-S05 acceptance pass. Every fixture is built through the shipped HTTP routes
// inside the test — listing, publish, inquiry, applicant, application — rather than by adding
// seed ids to DatabaseFixture, which is shared with every concurrent track.
[Collection("PostgreSQL")]
public sealed class ApplicationEndpointsTests(DatabaseFixture fixture)
{
    private sealed record Applicant(Guid ListingId, Guid InquiryId, Guid ApplicantId);

    // --- fixtures built over HTTP ---------------------------------------------------------

    private static async Task<Applicant> CreateApplicantAsync(Scenario s, Guid propertyId, string email, string phone)
    {
        using var listing = await s.Client.PostAsJsonAsync("/api/marketing/listings/", new
        {
            propertyId, spaceId = (Guid?)null, headline = $"Unit for {email}", description = "Freshly updated",
            availableOn = "2026-10-01", monthlyRent = 1850
        });
        Assert.Equal(HttpStatusCode.Created, listing.StatusCode);
        var listingId = (await listing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/marketing/listings/{listingId}/publish", null)).StatusCode);

        using var inquiry = await s.Client.PostAsJsonAsync($"/api/marketing/listings/{listingId}/inquiries", new
        { prospectName = "Dana Reyes", email, phone, message = "Interested", leadSource = "Website" });
        Assert.Equal(HttpStatusCode.Created, inquiry.StatusCode);
        var inquiryId = (await inquiry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var applicant = await s.Client.PostAsJsonAsync($"/api/marketing/listings/{listingId}/applicants", new { inquiryId });
        Assert.Equal(HttpStatusCode.Created, applicant.StatusCode);
        return new Applicant(listingId, inquiryId, (await applicant.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    private static async Task<Guid> CreateApplicationAsync(Scenario s, Applicant applicant, decimal income = 5200m, string employment = "Employed full time")
    {
        using var created = await s.Client.PostAsJsonAsync("/api/applications", new { listingId = applicant.ListingId });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var applicationId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var added = await s.Client.PostAsJsonAsync($"/api/applications/{applicationId}/applicants",
            new { applicantId = applicant.ApplicantId, role = "Primary", monthlyIncome = income, employmentStatus = employment });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        return applicationId;
    }

    private static async Task SubmitAsync(Scenario s, Guid applicationId)
    {
        using var response = await s.Client.PostAsync($"/api/applications/{applicationId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> ConsentAsync(Scenario s, Guid applicationId, Guid applicantId,
        string consentType = "BackgroundCheck", string decision = "Granted") =>
        await s.Client.PostAsJsonAsync($"/api/applications/{applicationId}/consent",
            new { applicantId, consentType, decision, source = "203.0.113.7" });

    private static async Task<Guid> ReadyForScreeningAsync(Scenario s, Applicant applicant)
    {
        var applicationId = await CreateApplicationAsync(s, applicant);
        await SubmitAsync(s, applicationId);
        using var consent = await ConsentAsync(s, applicationId, applicant.ApplicantId);
        Assert.Equal(HttpStatusCode.Created, consent.StatusCode);
        Assert.Equal("ConsentGranted", (await consent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applicationStatus").GetString());
        return applicationId;
    }

    private static async Task<JsonElement> StatusOfAsync(Scenario s, Guid applicationId)
    {
        using var response = await s.Client.GetAsync($"/api/applications/{applicationId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // A Regional Manager holds Applications.Manage and NOT Applications.ReadPii. The role is not
    // in DatabaseFixture's seed and must not be added to it, so the membership is created here.
    private static async Task<string> AddRegionalManagerAsync(Scenario s)
    {
        var id = Guid.NewGuid();
        var email = $"{id:N}@example.test";
        await using var identity = s.Identity();
        var user = new ApplicationUser
        {
            Id = id, UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true
        };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, s.Password);
        identity.Users.Add(user);
        identity.Memberships.Add(new OrganizationMembership { OrganizationId = s.OrganizationA, UserId = id, Role = "Regional Manager" });
        await identity.SaveChangesAsync();
        return email;
    }

    // ==== 1. The masking rule, asserted on the raw JSON ====================================

    // Deserializing into a record with nullable properties cannot tell "absent" from "null" and
    // would pass either way, so this reads the payload as text and asks whether the property
    // exists at all. Absent is the control; null would be a different and wrong answer.
    [Fact]
    public async Task A_caller_without_ReadPii_gets_a_payload_in_which_the_pii_properties_are_absent_not_null()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Screening:ProviderMode", "Fail"));
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "mask@example.test", "+15550100999");
        var applicationId = await ReadyForScreeningAsync(s, applicant);
        using var screened = await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null);
        Assert.Equal(HttpStatusCode.OK, screened.StatusCode);

        var regional = await AddRegionalManagerAsync(s);
        Assert.Equal(HttpStatusCode.NoContent, (await s.AttemptLoginAsync(regional, s.OrganizationA)).StatusCode);
        await s.RefreshCsrfAsync();

        foreach (var route in new[] { "/api/applications", $"/api/applications/{applicationId}", $"/api/applications/{applicationId}/screening" })
        {
            using var response = await s.Client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(payload);
            var applicants = Descend(document.RootElement, "applicants");
            var screening = Descend(document.RootElement, "screening");
            var rows = route.EndsWith("/screening", StringComparison.Ordinal) ? Flatten(document.RootElement) : [.. applicants, .. screening];
            Assert.NotEmpty(rows);
            foreach (var row in rows)
            {
                foreach (var masked in new[] { "email", "phone", "monthlyIncome", "employmentStatus", "score", "summary" })
                    Assert.False(row.TryGetProperty(masked, out _), $"{route}: '{masked}' must be absent, not null. Payload: {payload}");
            }
            // The fields a queue needs are still there, so masking has not made the list useless.
            Assert.Contains(rows, row => row.TryGetProperty("name", out _) || row.TryGetProperty("recommendation", out _));
        }
    }

    [Fact]
    public async Task A_caller_with_ReadPii_sees_the_same_records_unmasked()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Screening:ProviderMode", "Fail"));
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "unmask@example.test", "+15550100888");
        var applicationId = await ReadyForScreeningAsync(s, applicant);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null)).StatusCode);

        // Organization Admin holds both capabilities, so list and detail unmask automatically.
        using var detail = await s.Client.GetAsync($"/api/applications/{applicationId}");
        var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        var person = Descend(detailJson.RootElement, "applicants").Single();
        Assert.Equal("unmask@example.test", person.GetProperty("email").GetString());
        Assert.Equal("+15550100888", person.GetProperty("phone").GetString());
        Assert.Equal(5200m, person.GetProperty("monthlyIncome").GetDecimal());
        Assert.Equal("Employed full time", person.GetProperty("employmentStatus").GetString());

        using var pii = await s.Client.GetAsync($"/api/applications/{applicationId}/pii");
        Assert.Equal(HttpStatusCode.OK, pii.StatusCode);
        var piiJson = JsonDocument.Parse(await pii.Content.ReadAsStringAsync());
        var screeningRow = Descend(piiJson.RootElement, "screening").Single();
        Assert.True(screeningRow.TryGetProperty("score", out _));
        Assert.True(screeningRow.TryGetProperty("summary", out _));
        Assert.Equal("Fail", screeningRow.GetProperty("recommendation").GetString());
    }

    // ==== 2. The capability split is real ==================================================

    [Fact]
    public async Task A_regional_manager_can_work_an_application_but_is_refused_the_pii_route()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "rm@example.test", "+15550100777");
        var applicationId = await CreateApplicationAsync(s, applicant);

        var regional = await AddRegionalManagerAsync(s);
        Assert.Equal(HttpStatusCode.NoContent, (await s.AttemptLoginAsync(regional, s.OrganizationA)).StatusCode);
        await s.RefreshCsrfAsync();

        // Holds Applications.Manage: the workflow works.
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync($"/api/applications/{applicationId}")).StatusCode);
        using var submitted = await s.Client.PostAsync($"/api/applications/{applicationId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        // Does not hold Applications.ReadPii: the unmasked route is refused, visibly.
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync($"/api/applications/{applicationId}/pii")).StatusCode);
    }

    [Fact]
    public async Task A_read_only_caller_reaches_no_application_route_at_all()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/api/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsJsonAsync("/api/applications", new { listingId = Guid.NewGuid() })).StatusCode);
    }

    // ==== 3. The consent gate over HTTP ====================================================

    [Fact]
    public async Task Screening_is_refused_before_consent_and_accepted_after()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "gate@example.test", "+15550100666");
        var applicationId = await CreateApplicationAsync(s, applicant);

        // Draft: no consent anywhere.
        using var beforeSubmit = await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null);
        Assert.Equal(HttpStatusCode.Conflict, beforeSubmit.StatusCode);

        await SubmitAsync(s, applicationId);
        using var afterSubmit = await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null);
        Assert.Equal(HttpStatusCode.Conflict, afterSubmit.StatusCode);
        Assert.Contains("consent", (await afterSubmit.Content.ReadAsStringAsync()).ToLowerInvariant());

        // Nothing was written on either refusal.
        await using (var store = s.Store(s.OrganizationA))
            Assert.Empty(await store.ScreeningRequests.Where(x => x.ApplicationId == applicationId).ToListAsync());

        using var consent = await ConsentAsync(s, applicationId, applicant.ApplicantId);
        Assert.Equal(HttpStatusCode.Created, consent.StatusCode);
        using var screened = await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null);
        Assert.Equal(HttpStatusCode.OK, screened.StatusCode);
        Assert.Equal("UnderReview", (await StatusOfAsync(s, applicationId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_revoked_consent_blocks_screening_even_though_the_status_still_says_consent_granted()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "revoke@example.test", "+15550100555");
        var applicationId = await ReadyForScreeningAsync(s, applicant);

        using var revoked = await ConsentAsync(s, applicationId, applicant.ApplicantId, decision: "Revoked");
        Assert.Equal(HttpStatusCode.Created, revoked.StatusCode);
        // The status transition has no reverse, so the application still reads ConsentGranted —
        // which is exactly why the endpoint re-checks the rows before calling the provider.
        Assert.Equal("ConsentGranted", (await StatusOfAsync(s, applicationId)).GetProperty("status").GetString());
        using var screened = await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null);
        Assert.Equal(HttpStatusCode.Conflict, screened.StatusCode);
    }

    // ==== 4. Outage: 503, no verdict, attempt counted ======================================

    [Fact]
    public async Task A_provider_outage_answers_503_writes_no_verdict_and_still_counts_the_attempt()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Screening:ProviderMode", "Unavailable"));
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "outage@example.test", "+15550100444");
        var applicationId = await ReadyForScreeningAsync(s, applicant);

        using var outage = await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, outage.StatusCode);

        await using var store = s.Store(s.OrganizationA);
        var requests = await store.ScreeningRequests.Where(x => x.ApplicationId == applicationId).ToListAsync();
        var request = Assert.Single(requests);
        // The retry ledger is written; the verdict is not.
        Assert.Equal(1, request.Attempts);
        Assert.Equal(ScreeningRequestStatus.Pending, request.Status);
        Assert.NotNull(request.LastError);
        Assert.Empty(await store.ScreeningResults.Where(x => x.ScreeningRequestId == request.Id).ToListAsync());
        Assert.Equal("Screening", (await StatusOfAsync(s, applicationId)).GetProperty("status").GetString());
    }

    // ==== 5. Retry exhaustion end to end ====================================================

    [Fact]
    public async Task Three_outages_abandon_the_request_return_the_application_to_consent_granted_and_leave_the_path_open()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder
            .UseSetting("Screening:ProviderMode", "Unavailable").UseSetting("Screening:MaxAttempts", "3"));
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "exhaust@example.test", "+15550100333");
        var applicationId = await ReadyForScreeningAsync(s, applicant);

        using var first = await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Guid requestId;
        await using (var store = s.Store(s.OrganizationA))
            requestId = (await store.ScreeningRequests.SingleAsync(x => x.ApplicationId == applicationId)).Id;

        // Attempts 2 and 3. The third reaches the ceiling and abandons.
        for (var attempt = 2; attempt <= 3; attempt++)
        {
            using var retry = await s.Client.PostAsync($"/api/applications/{applicationId}/screening/{requestId}/retry", null);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, retry.StatusCode);
        }

        await using (var store = s.Store(s.OrganizationA))
        {
            var request = await store.ScreeningRequests.SingleAsync(x => x.Id == requestId);
            Assert.Equal(3, request.Attempts);
            Assert.Equal(ScreeningRequestStatus.Abandoned, request.Status);
            Assert.Empty(await store.ScreeningResults.Where(x => x.ScreeningRequestId == requestId).ToListAsync());
        }
        Assert.Equal("ConsentGranted", (await StatusOfAsync(s, applicationId)).GetProperty("status").GetString());

        // A fourth retry of the abandoned request is refused: that request is finished.
        using var afterAbandon = await s.Client.PostAsync($"/api/applications/{applicationId}/screening/{requestId}/retry", null);
        Assert.Equal(HttpStatusCode.Conflict, afterAbandon.StatusCode);

        // The path is open: a fresh POST /screening raises a NEW request without any further
        // consent being recorded. A key fixed per (application, applicant) made this impossible.
        var consentsBefore = await ConsentCountAsync(s, applicationId);
        using var fresh = await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, fresh.StatusCode);
        await using (var store = s.Store(s.OrganizationA))
        {
            var requests = await store.ScreeningRequests.Where(x => x.ApplicationId == applicationId).ToListAsync();
            Assert.Equal(2, requests.Count);
            Assert.Single(requests, x => x.Id != requestId && x.Status == ScreeningRequestStatus.Pending && x.Attempts == 1);
        }
        Assert.Equal(consentsBefore, await ConsentCountAsync(s, applicationId));
    }

    // The provider mode is read per call, so flipping it mid-scenario proves recovery rather than
    // only proving that a permanently-down provider stays down.
    [Fact]
    public async Task A_request_abandoned_during_an_outage_can_be_screened_successfully_once_the_provider_returns()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder
            .UseSetting("Screening:ProviderMode", "Unavailable").UseSetting("Screening:MaxAttempts", "1"));
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "recover@example.test", "+15550100222");
        var applicationId = await ReadyForScreeningAsync(s, applicant);

        using var outage = await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, outage.StatusCode);
        Assert.Equal("ConsentGranted", (await StatusOfAsync(s, applicationId)).GetProperty("status").GetString());

        s.Factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["Screening:ProviderMode"] = "Pass";

        var consentsBefore = await ConsentCountAsync(s, applicationId);
        using var recovered = await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal("UnderReview", (await StatusOfAsync(s, applicationId)).GetProperty("status").GetString());
        Assert.Equal(consentsBefore, await ConsentCountAsync(s, applicationId));
        await using var store = s.Store(s.OrganizationA);
        Assert.Equal(2, await store.ScreeningRequests.CountAsync(x => x.ApplicationId == applicationId));
        Assert.Single(await store.ScreeningResults.Where(x => store.ScreeningRequests.Any(r => r.Id == x.ScreeningRequestId && r.ApplicationId == applicationId)).ToListAsync());
    }

    // ==== 6. Approve over Fail ==============================================================

    [Fact]
    public async Task Approving_over_a_failed_screening_needs_an_override_note_and_the_409_says_so()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Screening:ProviderMode", "Fail"));
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "fail@example.test", "+15550100111");
        var applicationId = await ReadyForScreeningAsync(s, applicant);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null)).StatusCode);

        using var refused = await s.Client.PostAsJsonAsync($"/api/applications/{applicationId}/approve",
            new { reason = "INDIVIDUAL_ASSESSMENT", note = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = await refused.Content.ReadAsStringAsync();
        // Actionable, not a bare "Conflict".
        Assert.Contains("override note", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("UnderReview", (await StatusOfAsync(s, applicationId)).GetProperty("status").GetString());

        using var blank = await s.Client.PostAsJsonAsync($"/api/applications/{applicationId}/approve",
            new { reason = "INDIVIDUAL_ASSESSMENT", note = "   " });
        Assert.Equal(HttpStatusCode.Conflict, blank.StatusCode);

        const string Note = "Guarantor covers 3x rent; the offence is nine years old.";
        using var approved = await s.Client.PostAsJsonAsync($"/api/applications/{applicationId}/approve",
            new { reason = "INDIVIDUAL_ASSESSMENT", note = Note });
        Assert.Equal(HttpStatusCode.Created, approved.StatusCode);
        var decision = (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decision");
        Assert.Equal(Note, decision.GetProperty("note").GetString());
        Assert.Equal("Approved", decision.GetProperty("outcome").GetString());
        Assert.Equal("Approved", (await StatusOfAsync(s, applicationId)).GetProperty("status").GetString());

        await using var store = s.Store(s.OrganizationA);
        Assert.Equal(Note, (await store.ApplicationDecisions.SingleAsync(x => x.ApplicationId == applicationId)).Note);
    }

    [Fact]
    public async Task A_clean_screening_approves_with_no_note_and_a_denial_records_its_reason()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Screening:ProviderMode", "Pass"));
        await s.LoginAsync();
        var clean = await CreateApplicantAsync(s, s.PropertyA, "clean@example.test", "+15550100121");
        var cleanApplication = await ReadyForScreeningAsync(s, clean);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/applications/{cleanApplication}/screening", null)).StatusCode);
        using var approved = await s.Client.PostAsJsonAsync($"/api/applications/{cleanApplication}/approve",
            new { reason = "MEETS_CRITERIA", note = (string?)null });
        Assert.Equal(HttpStatusCode.Created, approved.StatusCode);

        var denied = await CreateApplicantAsync(s, s.PropertyA, "denied@example.test", "+15550100122");
        var deniedApplication = await ReadyForScreeningAsync(s, denied);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/applications/{deniedApplication}/screening", null)).StatusCode);
        using var denial = await s.Client.PostAsJsonAsync($"/api/applications/{deniedApplication}/deny",
            new { reason = "INSUFFICIENT_INCOME", note = "Income is 2.1x rent against a 3x policy." });
        Assert.Equal(HttpStatusCode.Created, denial.StatusCode);

        using var trail = await s.Client.GetAsync($"/api/applications/{deniedApplication}/decisions");
        Assert.Equal(HttpStatusCode.OK, trail.StatusCode);
        var decisions = await trail.Content.ReadFromJsonAsync<JsonElement[]>();
        var entry = Assert.Single(decisions!);
        Assert.Equal("Denied", entry.GetProperty("outcome").GetString());
        Assert.Equal("INSUFFICIENT_INCOME", entry.GetProperty("reason").GetString());
    }

    // ==== 7. The trail is immutable through the API ==========================================

    [Fact]
    public async Task No_route_edits_or_deletes_a_consent_a_screening_result_or_a_decision()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Screening:ProviderMode", "Pass"));
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "immutable@example.test", "+15550100123");
        var applicationId = await ReadyForScreeningAsync(s, applicant);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await s.Client.PostAsJsonAsync($"/api/applications/{applicationId}/approve",
            new { reason = "MEETS_CRITERIA", note = (string?)null })).StatusCode);

        Guid consentId, resultId, decisionId;
        await using (var store = s.Store(s.OrganizationA))
        {
            consentId = (await store.ApplicationConsents.FirstAsync(x => x.ApplicationId == applicationId)).Id;
            decisionId = (await store.ApplicationDecisions.FirstAsync(x => x.ApplicationId == applicationId)).Id;
            resultId = (await store.ScreeningResults.FirstAsync()).Id;
        }

        // No PUT, PATCH or DELETE is mapped anywhere on this surface. 404/405 are both acceptable
        // answers; a 2xx on any of them would mean a mutation route exists.
        foreach (var (method, route) in new[]
        {
            (HttpMethod.Delete, $"/api/applications/{applicationId}/consent/{consentId}"),
            (HttpMethod.Put, $"/api/applications/{applicationId}/consent/{consentId}"),
            (HttpMethod.Delete, $"/api/applications/{applicationId}/decisions/{decisionId}"),
            (HttpMethod.Put, $"/api/applications/{applicationId}/decisions/{decisionId}"),
            (HttpMethod.Delete, $"/api/applications/{applicationId}/screening/results/{resultId}"),
            (HttpMethod.Delete, $"/api/applications/{applicationId}")
        })
        {
            using var request = new HttpRequestMessage(method, route);
            using var response = await s.Client.SendAsync(request);
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"{method} {route} answered {(int)response.StatusCode}; the decision trail must have no mutation route.");
        }

        // And the rows are still exactly as written.
        await using (var store = s.Store(s.OrganizationA))
        {
            Assert.Single(await store.ApplicationDecisions.Where(x => x.ApplicationId == applicationId).ToListAsync());
            Assert.Single(await store.ApplicationConsents.Where(x => x.ApplicationId == applicationId).ToListAsync());
        }
    }

    [Fact]
    public async Task A_terminal_application_refuses_every_further_transition_over_http()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "terminal@example.test", "+15550100124");
        var applicationId = await CreateApplicationAsync(s, applicant);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/applications/{applicationId}/withdraw", null)).StatusCode);
        Assert.Equal("Withdrawn", (await StatusOfAsync(s, applicationId)).GetProperty("status").GetString());

        foreach (var route in new[] { "submit", "withdraw", "screening" })
            Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsync($"/api/applications/{applicationId}/{route}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsJsonAsync($"/api/applications/{applicationId}/approve",
            new { reason = "MEETS_CRITERIA", note = (string?)null })).StatusCode);
    }

    // ==== 8. The legacy fence, both halves ==================================================

    [Fact]
    public async Task The_legacy_applicant_status_route_still_works_until_an_application_exists_and_then_refuses()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "legacy@example.test", "+15550100125");
        var route = $"/api/marketing/listings/{applicant.ListingId}/applicants/{applicant.ApplicantId}/status";

        // Half one: nothing that predates FS-S05 is broken.
        using var before = await s.Client.PutAsJsonAsync(route, new { status = "Screening" });
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal("Screening", (await before.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        await CreateApplicationAsync(s, applicant);

        // Half two: the shipped "Send to screening" / "Approve" buttons now fail loudly rather
        // than reaching Approved with no consent check and no decision row.
        using var after = await s.Client.PutAsJsonAsync(route, new { status = "Approved" });
        Assert.Equal(HttpStatusCode.Conflict, after.StatusCode);
        Assert.Contains("/api/applications", await after.Content.ReadAsStringAsync());
        await using var store = s.Store(s.OrganizationA);
        Assert.Equal(ApplicantStatus.Screening, (await store.Applicants.SingleAsync(x => x.Id == applicant.ApplicantId)).Status);
    }

    // ==== 9. Tenant isolation ================================================================

    [Fact]
    public async Task Every_application_route_is_tenant_scoped()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Screening:ProviderMode", "Pass"));
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "tenant@example.test", "+15550100126");
        var applicationId = await ReadyForScreeningAsync(s, applicant);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await s.AttemptLoginAsync(s.EmailB, s.OrganizationB)).StatusCode);
        await s.RefreshCsrfAsync();

        Assert.Empty((await s.Client.GetFromJsonAsync<JsonElement[]>("/api/applications"))!);
        foreach (var route in new[] { "", "/pii", "/consent", "/screening", "/decisions" })
            Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/applications/{applicationId}{route}")).StatusCode);
        foreach (var route in new[] { "submit", "withdraw", "screening" })
            Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsync($"/api/applications/{applicationId}/{route}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsJsonAsync($"/api/applications/{applicationId}/approve",
            new { reason = "MEETS_CRITERIA", note = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsJsonAsync($"/api/applications/{applicationId}/consent",
            new { applicantId = applicant.ApplicantId, consentType = "CreditCheck", decision = "Granted", source = "forged" })).StatusCode);
    }

    // ==== 10. The two LINQ shapes ============================================================

    // ApplicationEndpoints.ApplicantRowsAsync joins Inquiries with DefaultIfEmpty over a
    // composite key; RentalApplication.Applicants is mapped PropertyAccessMode.Field and is
    // reached with Include. Both are EF-translation risks that no unit test can reach.
    [Fact]
    public async Task The_applicant_projection_and_the_applicants_include_both_translate()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var primary = await CreateApplicantAsync(s, s.PropertyA, "join1@example.test", "+15550100127");
        var applicationId = await CreateApplicationAsync(s, primary);

        // A second person on the same application exercises the one-Primary rule through the
        // Include, and the list projection across more than one row.
        var co = await CreateApplicantAsync(s, s.PropertyA, "join2@example.test", "+15550100128");
        using var second = await s.Client.PostAsJsonAsync($"/api/applications/{applicationId}/applicants",
            new { applicantId = co.ApplicantId, role = "CoApplicant", monthlyIncome = 3100m, employmentStatus = "Part time" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var duplicatePrimary = await s.Client.PostAsJsonAsync($"/api/applications/{applicationId}/applicants",
            new { applicantId = co.ApplicantId, role = "Primary", monthlyIncome = (decimal?)null, employmentStatus = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, duplicatePrimary.StatusCode);

        var detail = await StatusOfAsync(s, applicationId);
        var people = Descend(detail, "applicants");
        Assert.Equal(2, people.Count);
        // The left join resolved a phone for both, which is the DefaultIfEmpty path returning a
        // match rather than null.
        Assert.Contains(people, x => x.GetProperty("phone").GetString() == "+15550100127");
        Assert.Contains(people, x => x.GetProperty("phone").GetString() == "+15550100128");

        // And the list route, which runs the same projection over many applications.
        var list = await s.Client.GetFromJsonAsync<JsonElement[]>($"/api/applications?listingId={primary.ListingId}");
        Assert.Single(list!);
        Assert.Equal(2, Descend(list![0], "applicants").Count);
    }

    // The null branch of the same left join: an applicant whose inquiry row is gone must still
    // project, with phone simply absent rather than the whole query failing.
    [Fact]
    public async Task The_applicant_projection_survives_an_applicant_whose_inquiry_was_removed()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "orphan@example.test", "+15550100129");
        var applicationId = await CreateApplicationAsync(s, applicant);

        await using (var store = s.AdminStore(s.OrganizationA))
        {
            // Applicants cascade from Inquiries, so the person would go too. Blank the phone
            // instead: the null branch of the projection is the same either way.
            await store.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE operations."Inquiries" SET "Phone" = NULL WHERE "Id" = {applicant.InquiryId}""");
        }

        var detail = await StatusOfAsync(s, applicationId);
        var person = Descend(detail, "applicants").Single();
        Assert.Equal(JsonValueKind.Null, person.GetProperty("phone").ValueKind);
        Assert.Equal("Dana Reyes", person.GetProperty("name").GetString());
    }

    // ==== 11. Replay ==========================================================================

    [Fact]
    public async Task A_replayed_screening_post_reuses_the_pending_request_rather_than_violating_the_unique_index()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Screening:ProviderMode", "Unavailable"));
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "replay@example.test", "+15550100130");
        var applicationId = await ReadyForScreeningAsync(s, applicant);

        // First POST leaves a Pending request behind (the outage path).
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null)).StatusCode);
        Guid requestId;
        await using (var store = s.Store(s.OrganizationA))
            requestId = (await store.ScreeningRequests.SingleAsync(x => x.ApplicationId == applicationId)).Id;

        // A replay finds the live request and attempts it again — same row, not a second one and
        // not a 23505.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null)).StatusCode);
        await using (var store = s.Store(s.OrganizationA))
        {
            var request = Assert.Single(await store.ScreeningRequests.Where(x => x.ApplicationId == applicationId).ToListAsync());
            Assert.Equal(requestId, request.Id);
            Assert.Equal(2, request.Attempts);
        }
    }

    [Fact]
    public async Task A_completed_applicant_is_not_screened_twice_by_a_replayed_post()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Screening:ProviderMode", "Pass"));
        await s.LoginAsync();
        var applicant = await CreateApplicantAsync(s, s.PropertyA, "once@example.test", "+15550100131");
        var applicationId = await ReadyForScreeningAsync(s, applicant);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null)).StatusCode);
        Assert.Equal("UnderReview", (await StatusOfAsync(s, applicationId)).GetProperty("status").GetString());

        // The application has left Screening, so the domain refuses a second BeginScreening — a
        // completed screening is not re-run, and no second credit pull is made.
        using var replay = await s.Client.PostAsync($"/api/applications/{applicationId}/screening", null);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        await using var store = s.Store(s.OrganizationA);
        Assert.Equal(1, await store.ScreeningRequests.CountAsync(x => x.ApplicationId == applicationId));
        Assert.Equal(1, await store.ScreeningResults.CountAsync());
    }

    // --- json helpers ------------------------------------------------------------------------

    private static List<JsonElement> Descend(JsonElement root, string property)
    {
        var items = new List<JsonElement>();
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray()) items.AddRange(Descend(element, property));
            return items;
        }
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var found) && found.ValueKind == JsonValueKind.Array)
            items.AddRange(found.EnumerateArray());
        return items;
    }

    private static List<JsonElement> Flatten(JsonElement root) =>
        root.ValueKind == JsonValueKind.Array ? [.. root.EnumerateArray()] : [root];

    private static async Task<int> ConsentCountAsync(Scenario s, Guid applicationId)
    {
        await using var store = s.Store(s.OrganizationA);
        return await store.ApplicationConsents.CountAsync(x => x.ApplicationId == applicationId);
    }
}
