using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class AuthenticationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Login_sets_secure_cookie_and_verified_session()
    {
        await using var s = await fixture.CreateScenarioAsync();
        using var login = await s.AttemptLoginAsync(s.EmailA, s.OrganizationA);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), x => x.StartsWith("__Host-PropFlow="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        var session = await s.Client.GetFromJsonAsync<JsonElement>("/api/session");
        Assert.Equal(s.OrganizationA, session.GetProperty("organizationId").GetGuid());
        Assert.Equal(s.AdminA.ToString(), session.GetProperty("userId").GetString());
        using var response = await s.Client.GetAsync("/api/session");
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Anonymous_and_spoofed_header_requests_are_unauthorized()
    {
        await using var s = await fixture.CreateScenarioAsync();
        s.Client.DefaultRequestHeaders.Add("X-Organization-Id", s.OrganizationA.ToString());
        foreach (var path in new[] { "/api/session", "/api/work/", $"/api/work/{s.WorkA}" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Correct_password_cannot_select_an_organization_without_membership()
    {
        await using var s = await fixture.CreateScenarioAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.AttemptLoginAsync(s.EmailA, s.OrganizationB)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client.GetAsync("/api/session")).StatusCode);
    }

    [Fact]
    public async Task Login_and_authenticated_writes_require_csrf()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var login = await s.Client.PostAsJsonAsync("/api/auth/login", new { email = s.EmailA, password = s.Password, organizationId = s.OrganizationA });
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        await s.LoginAsync();
        s.Client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await s.AssignAsync(s.WorkA, s.VendorA)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        await using var store = s.Store(s.OrganizationA);
        Assert.Null((await store.WorkItems.SingleAsync()).VendorId);
    }

    [Fact]
    public async Task Anonymous_csrf_token_cannot_be_reused_after_login()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.AttemptLoginAsync(s.EmailA, s.OrganizationA);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.AssignAsync(s.WorkA, s.VendorA)).StatusCode);
    }

    [Fact]
    public async Task Read_only_capability_blocks_assignment()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/work/")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.AssignAsync(s.WorkA, s.VendorA)).StatusCode);
    }

    [Fact]
    public async Task Role_changes_apply_to_existing_cookie()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        await using var identity = s.Identity();
        var membership = await identity.Memberships.SingleAsync(x => x.UserId == s.AdminA);
        membership.Role = "Read Only";
        await identity.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await s.AssignAsync(s.WorkA, s.VendorA)).StatusCode);
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("organization")]
    [InlineData("security-stamp")]
    public async Task Revoked_access_invalidates_existing_cookie(string kind)
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        await using var identity = s.Identity();
        if (kind == "membership") (await identity.Memberships.SingleAsync(x => x.UserId == s.AdminA)).IsActive = false;
        if (kind == "organization") (await identity.Organizations.SingleAsync(x => x.Id == s.OrganizationA)).IsActive = false;
        if (kind == "security-stamp") (await identity.Users.SingleAsync(x => x.Id == s.AdminA)).SecurityStamp = Guid.NewGuid().ToString();
        await identity.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client.GetAsync("/api/session")).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_copied_cookie()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var login = await s.AttemptLoginAsync(s.EmailA, s.OrganizationA);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-PropFlow=")).Split(';')[0];
        await s.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        using var replay = s.Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/session")).StatusCode);
    }

    [Fact]
    public async Task Failed_password_attempts_lock_account()
    {
        await using var s = await fixture.CreateScenarioAsync();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await s.AttemptLoginAsync(s.EmailA, s.OrganizationA, "Wrong!Password999")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.AttemptLoginAsync(s.EmailA, s.OrganizationA)).StatusCode);
        await using var store = s.Identity();
        Assert.True((await store.Users.SingleAsync(x => x.Id == s.AdminA)).LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_is_rate_limited()
    {
        await using var s = await fixture.CreateScenarioAsync();
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await s.AttemptLoginAsync("missing@example.test", s.OrganizationA)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await s.AttemptLoginAsync("missing@example.test", s.OrganizationA)).StatusCode);
    }

    [Fact]
    public async Task Readiness_and_authenticated_openapi_are_available()
    {
        await using var s = await fixture.CreateScenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client.GetAsync("/openapi/v1.json")).StatusCode);
        await s.LoginAsync();
        var contract = await s.Client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.True(contract.GetProperty("paths").TryGetProperty("/api/auth/login", out _));
    }
}
