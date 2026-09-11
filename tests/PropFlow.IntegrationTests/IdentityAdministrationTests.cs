using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using PropFlow.Infrastructure.Identity;
using Xunit;

namespace PropFlow.IntegrationTests;

// PF-S01.08: negative-authorization and tenant-isolation coverage for the FS-S01 identity
// administration surface (invitations, the role-capability matrix, teams, membership management,
// sessions, and the audit trail) added in PF-S01.02-.07. AuthenticationTests/CategoryEndpointTests
// already cover the general login/CSRF/RLS story - this file is specific to the endpoints under
// /api/organizations/current/{invitations,roles,teams,members} and /api/sessions,
// /api/organizations/current/audit.
[Collection("PostgreSQL")]
public sealed class IdentityAdministrationTests(DatabaseFixture fixture)
{
    // A second, independently cookied client against the same running app - used whenever a test
    // needs two authenticated identities live at once (e.g. proving a role-capability change takes
    // effect on someone else's already-issued cookie, or that one user cannot touch another's
    // session). Mirrors Logout_revokes_copied_cookie's use of a second Factory.CreateClient.
    private static async Task<HttpClient> LoggedInClientAsync(Scenario s, string email, Guid organization)
    {
        var client = s.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { organizationSlug = organization == s.OrganizationA ? s.SlugA : s.SlugB, email, password = s.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        csrf = (await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        return client;
    }

    [Fact]
    public async Task Read_only_role_is_forbidden_from_every_administration_endpoint()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/api/organizations/current/invitations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsJsonAsync("/api/organizations/current/invitations",
            new { email = "nope@example.test", role = "Read Only" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/api/organizations/current/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/api/organizations/current/teams")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/api/organizations/current/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/api/organizations/current/audit")).StatusCode);
        // Sessions are self-service, not ManageMembers-gated - a reader can see their own.
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/sessions")).StatusCode);
    }

    [Fact]
    public async Task Invite_create_list_and_accept_flow_creates_a_working_login()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var email = "new.hire@example.test";
        var created = await (await s.Client.PostAsJsonAsync("/api/organizations/current/invitations",
            new { email, role = "Property Manager" })).Content.ReadFromJsonAsync<JsonElement>();
        var token = created.GetProperty("token").GetString()!;

        var pending = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/organizations/current/invitations");
        Assert.Contains(pending!, x => x.GetProperty("email").GetString() == email);

        using var anonymous = s.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        // The accept endpoint is AllowAnonymous (no session yet), but the app's CSRF middleware
        // still applies to every non-GET /api/* route except provider-callback, so a token has to
        // be fetched first even here.
        async Task RefreshAnonymousCsrfAsync()
        {
            var csrfToken = (await anonymous.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("token").GetString()!;
            anonymous.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            anonymous.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrfToken);
        }
        await RefreshAnonymousCsrfAsync();
        var accept = await anonymous.PostAsJsonAsync($"/api/invitations/{token}/accept", new { password = s.Password });
        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);

        await RefreshAnonymousCsrfAsync();
        var login = await anonymous.PostAsJsonAsync("/api/auth/login", new { organizationSlug = s.SlugA, email, password = s.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var session = await anonymous.GetFromJsonAsync<JsonElement>("/api/session");
        Assert.Equal("Property Manager", session.GetProperty("role").GetString());

        var auditForA = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/organizations/current/audit");
        Assert.Contains(auditForA!, x => x.GetProperty("eventType").GetString() == "InvitationAccepted");
    }

    [Fact]
    public async Task Invitation_accept_rejects_unknown_expired_and_already_accepted_tokens()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var created = await (await s.Client.PostAsJsonAsync("/api/organizations/current/invitations",
            new { email = "expired@example.test", role = "Read Only" })).Content.ReadFromJsonAsync<JsonElement>();
        var token = created.GetProperty("token").GetString()!;

        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsJsonAsync("/api/invitations/does-not-exist/accept",
            new { password = s.Password })).StatusCode);

        await using (var identity = s.Identity())
        {
            (await identity.Invitations.SingleAsync(x => x.Token == token)).ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
            await identity.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Gone, (await s.Client.PostAsJsonAsync($"/api/invitations/{token}/accept",
            new { password = s.Password })).StatusCode);

        await using (var identity = s.Identity())
        {
            var invitation = await identity.Invitations.SingleAsync(x => x.Token == token);
            invitation.ExpiresAt = DateTimeOffset.UtcNow.AddDays(1);
            invitation.AcceptedAt = DateTimeOffset.UtcNow;
            await identity.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsJsonAsync($"/api/invitations/{token}/accept",
            new { password = s.Password })).StatusCode);
    }

    [Fact]
    public async Task Invitations_and_audit_entries_are_confined_to_the_current_organization()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        await s.Client.PostAsJsonAsync("/api/organizations/current/invitations", new { email = "orgA-only@example.test", role = "Read Only" });

        using var adminB = await LoggedInClientAsync(s, s.EmailB, s.OrganizationB);
        var pendingForB = await adminB.GetFromJsonAsync<JsonElement[]>("/api/organizations/current/invitations");
        Assert.Empty(pendingForB!);
        var auditForB = await adminB.GetFromJsonAsync<JsonElement[]>("/api/organizations/current/audit");
        Assert.Empty(auditForB!);

        var auditForA = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/organizations/current/audit");
        Assert.Contains(auditForA!, x => x.GetProperty("eventType").GetString() == "InvitationSent");
    }

    [Fact]
    public async Task Unknown_role_is_a_404_and_revoking_the_last_manager_is_refused()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PutAsJsonAsync(
            "/api/organizations/current/roles/Not%20A%20Role/capabilities", new { grants = Array.Empty<string>(), revocations = Array.Empty<string>() })).StatusCode);

        // Only AdminA (Organization Admin) can manage members in this org; ReaderA (Read Only)
        // never had the capability. Revoking it here would leave nobody able to administer the org.
        var response = await s.Client.PutAsJsonAsync("/api/organizations/current/roles/Organization%20Admin/capabilities",
            new { grants = Array.Empty<string>(), revocations = new[] { "Identity.ManageMembers" } });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Granting_a_capability_to_a_role_takes_effect_on_an_already_issued_cookie()
    {
        await using var s = await fixture.CreateScenarioAsync();
        using var readerClient = await LoggedInClientAsync(s, s.ReaderEmail, s.OrganizationA);
        Assert.Equal(HttpStatusCode.Forbidden, (await readerClient.GetAsync("/api/organizations/current/invitations")).StatusCode);

        await s.LoginAsync();
        var grant = await s.Client.PutAsJsonAsync("/api/organizations/current/roles/Read%20Only/capabilities",
            new { grants = new[] { "Identity.ManageMembers" }, revocations = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        // No re-login on readerClient - ValidateCookieAsync recomputes capabilities every request.
        Assert.Equal(HttpStatusCode.OK, (await readerClient.GetAsync("/api/organizations/current/invitations")).StatusCode);
    }

    [Fact]
    public async Task Teams_are_created_and_membership_requires_an_existing_active_membership()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var created = await (await s.Client.PostAsJsonAsync("/api/organizations/current/teams", new { name = "Norfolk Crew" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var teamId = created.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsJsonAsync("/api/organizations/current/teams", new { name = "Norfolk Crew" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.PostAsync($"/api/organizations/current/teams/{teamId}/members/{s.ReaderA}", null)).StatusCode);
        var members = await s.Client.GetFromJsonAsync<Guid[]>($"/api/organizations/current/teams/{teamId}/members");
        Assert.Contains(s.ReaderA, members!);

        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PostAsync($"/api/organizations/current/teams/{teamId}/members/{Guid.NewGuid()}", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.DeleteAsync($"/api/organizations/current/teams/{teamId}/members/{s.ReaderA}")).StatusCode);
        members = await s.Client.GetFromJsonAsync<Guid[]>($"/api/organizations/current/teams/{teamId}/members");
        Assert.DoesNotContain(s.ReaderA, members!);
    }

    [Fact]
    public async Task A_team_from_another_organization_is_not_found()
    {
        await using var s = await fixture.CreateScenarioAsync();
        using var adminB = await LoggedInClientAsync(s, s.EmailB, s.OrganizationB);
        var teamB = await (await adminB.PostAsJsonAsync("/api/organizations/current/teams", new { name = "Other Org Team" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var teamBId = teamB.GetProperty("id").GetGuid();

        await s.LoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsync($"/api/organizations/current/teams/{teamBId}/members/{s.ReaderA}", null)).StatusCode);
    }

    [Fact]
    public async Task Membership_role_change_and_removal_flow_and_removed_members_cannot_log_in()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.PutAsJsonAsync(
            $"/api/organizations/current/members/{s.ReaderA}/role", new { role = "Property Manager" })).StatusCode);
        await using (var identity = s.Identity())
            Assert.Equal("Property Manager", (await identity.Memberships.SingleAsync(x => x.UserId == s.ReaderA)).Role);

        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.DeleteAsync($"/api/organizations/current/members/{s.ReaderA}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.AttemptLoginAsync(s.ReaderEmail, s.OrganizationA)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.DeleteAsync($"/api/organizations/current/members/{s.ReaderA}")).StatusCode);
    }

    [Fact]
    public async Task Removing_the_last_manager_is_refused_and_membership_endpoints_are_tenant_scoped()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        // AdminA is the only member of OrgA with ManageMembers; ReaderA cannot cover for them.
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.DeleteAsync($"/api/organizations/current/members/{s.AdminA}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PutAsJsonAsync(
            $"/api/organizations/current/members/{s.AdminA}/role", new { role = "Read Only" })).StatusCode);

        // AdminB is a real, active membership - but it belongs to OrganizationB.
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.DeleteAsync($"/api/organizations/current/members/{s.AdminB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PutAsJsonAsync(
            $"/api/organizations/current/members/{s.AdminB}/role", new { role = "Read Only" })).StatusCode);
    }

    [Fact]
    public async Task Sessions_lists_the_current_device_and_self_service_revoke_signs_it_out()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        using var secondDevice = await LoggedInClientAsync(s, s.EmailA, s.OrganizationA);

        var sessions = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/sessions");
        Assert.Equal(2, sessions!.Length);
        Assert.Single(sessions, x => x.GetProperty("isCurrent").GetBoolean());
        var other = sessions.Single(x => !x.GetProperty("isCurrent").GetBoolean()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.DeleteAsync($"/api/sessions/{other}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondDevice.GetAsync("/api/session")).StatusCode);
        // The revoking device's own cookie is untouched.
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/session")).StatusCode);
    }

    [Fact]
    public async Task A_user_cannot_revoke_another_users_session()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        using var readerClient = await LoggedInClientAsync(s, s.ReaderEmail, s.OrganizationA);

        Guid readerSessionId;
        await using (var identity = s.Identity())
            readerSessionId = (await identity.UserSessions.SingleAsync(x => x.UserId == s.ReaderA)).Id;

        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.DeleteAsync($"/api/sessions/{readerSessionId}")).StatusCode);
        // The reader's session is still good - the admin's attempt did nothing to it.
        Assert.Equal(HttpStatusCode.OK, (await readerClient.GetAsync("/api/session")).StatusCode);
    }
}
