using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using PropFlow.Domain.Assets;
using PropFlow.Domain.People;
using PropFlow.Domain.Properties;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Identity;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class ScopedWorkAuthorizationTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("Technician")]
    [InlineData("Vendor")]
    public async Task Field_roles_are_denied_work_at_another_property_or_with_another_assignment(string role)
    {
        await using var s = await fixture.CreateScenarioAsync();
        var user = Guid.NewGuid(); var assignedParty = Guid.NewGuid(); var otherParty = Guid.NewGuid();
        var otherProperty = Guid.NewGuid(); var allowed = Guid.NewGuid(); var crossProperty = Guid.NewGuid(); var crossAssignment = Guid.NewGuid();
        await AddFieldUserAsync(s, user, role, role == "Technician" ? assignedParty : null, role == "Vendor" ? assignedParty : null, s.PropertyA);
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            store.Properties.Add(new Property(s.OrganizationA, otherProperty, s.PortfolioA, "Scoped secondary property", "America/New_York"));
            if (role == "Technician") store.Employees.AddRange(new Employee(s.OrganizationA, assignedParty, "Assigned technician", null, null), new Employee(s.OrganizationA, otherParty, "Other technician", null, null));
            else store.Vendors.AddRange(new Vendor(s.OrganizationA, assignedParty, "Assigned vendor"), new Vendor(s.OrganizationA, otherParty, "Other vendor"));
            var permitted = new WorkItem(s.OrganizationA, allowed, "Allowed scoped work", s.PropertyA, s.AdminA);
            var foreignProperty = new WorkItem(s.OrganizationA, crossProperty, "Other-property scoped work", otherProperty, s.AdminA);
            var foreignAssignment = new WorkItem(s.OrganizationA, crossAssignment, "Other-assignment scoped work", s.PropertyA, s.AdminA);
            if (role == "Technician") { permitted.AssignEmployee(assignedParty); foreignProperty.AssignEmployee(assignedParty); foreignAssignment.AssignEmployee(otherParty); }
            else { permitted.AssignVendor(assignedParty); foreignProperty.AssignVendor(assignedParty); foreignAssignment.AssignVendor(otherParty); }
            store.WorkItems.AddRange(permitted, foreignProperty, foreignAssignment);
            await store.SaveChangesAsync();
        }
        using var login = await s.AttemptLoginAsync($"{user:N}@example.test", s.OrganizationA);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var list = await s.Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/work");
        Assert.Equal(allowed, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync($"/api/work/{allowed}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync($"/api/work/{allowed}/timeline")).StatusCode);
        foreach (var denied in new[] { crossProperty, crossAssignment })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/work/{denied}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/work/{denied}/timeline")).StatusCode);
        }
    }

    [Fact]
    public async Task Technician_can_only_read_currently_assigned_work()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var technician = Guid.NewGuid(); var employee = Guid.NewGuid();
        await AddFieldUserAsync(s, technician, "Technician", employeeId: employee);
        var assigned = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            store.Employees.Add(new Employee(s.OrganizationA, employee, "Scoped Technician", null, null));
            var work = new WorkItem(s.OrganizationA, assigned, "Technician repair", s.PropertyA, s.AdminA);
            work.AssignEmployee(employee);
            store.WorkItems.Add(work);
            await store.SaveChangesAsync();
        }

        using var login = await s.AttemptLoginAsync($"{technician:N}@example.test", s.OrganizationA);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var list = await s.Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/work");
        Assert.Equal(assigned, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync($"/api/work/{assigned}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/work/{s.WorkA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/work/{s.WorkA}/timeline")).StatusCode);
    }

    [Fact]
    public async Task Vendor_property_binding_narrows_assigned_work()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var vendorUser = Guid.NewGuid();
        await AddFieldUserAsync(s, vendorUser, "Vendor", vendorId: s.VendorA, propertyId: s.PropertyB);
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            var work = await store.WorkItems.SingleAsync(x => x.Id == s.WorkA);
            work.AssignVendor(s.VendorA);
            await store.SaveChangesAsync();
        }

        using var login = await s.AttemptLoginAsync($"{vendorUser:N}@example.test", s.OrganizationA);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var list = await s.Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/work");
        Assert.Empty(list.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/work/{s.WorkA}")).StatusCode);
    }

    private static async Task AddFieldUserAsync(Scenario s, Guid id, string role, Guid? employeeId = null, Guid? vendorId = null, Guid? propertyId = null)
    {
        await using var identity = s.Identity();
        var email = $"{id:N}@example.test";
        var user = new ApplicationUser { Id = id, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, s.Password);
        identity.Users.Add(user);
        identity.Memberships.Add(new OrganizationMembership { OrganizationId = s.OrganizationA, UserId = id, Role = role, EmployeeId = employeeId, VendorId = vendorId });
        if (propertyId is { } property) identity.MembershipPropertyBindings.Add(new MembershipPropertyBinding { OrganizationId = s.OrganizationA, UserId = id, PropertyId = property });
        await identity.SaveChangesAsync();
    }
}
