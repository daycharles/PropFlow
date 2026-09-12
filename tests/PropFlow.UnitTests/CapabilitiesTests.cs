using System.Reflection;
using PropFlow.Application;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class CapabilitiesTests
{
    [Theory]
    [InlineData("Organization Admin")]
    [InlineData("Property Manager")]
    public void Admin_and_property_manager_can_manage_templates_and_people(string role)
    {
        var capabilities = Capabilities.ForRole(role);
        Assert.Contains(Capabilities.ManageTemplates, capabilities);
        Assert.Contains(Capabilities.ManagePeople, capabilities);
        Assert.Contains(Capabilities.ReadWork, capabilities);
        Assert.Contains(Capabilities.AssignVendor, capabilities);
    }

    [Theory]
    [InlineData("Regional Manager")]
    [InlineData("Maintenance Supervisor")]
    public void Other_managers_assign_work_and_assets_but_not_templates_or_people(string role)
    {
        var capabilities = Capabilities.ForRole(role);
        Assert.Contains(Capabilities.ReadWork, capabilities);
        Assert.Contains(Capabilities.AssignVendor, capabilities);
        Assert.Contains(Capabilities.ManageAssets, capabilities);
        Assert.DoesNotContain(Capabilities.ManageTemplates, capabilities);
        Assert.DoesNotContain(Capabilities.ManagePeople, capabilities);
    }

    [Fact]
    public void Only_admin_and_property_manager_manage_integrations()
    {
        Assert.Contains(Capabilities.ManageIntegrations, Capabilities.ForRole("Organization Admin"));
        Assert.Contains(Capabilities.ManageIntegrations, Capabilities.ForRole("Property Manager"));
        Assert.DoesNotContain(Capabilities.ManageIntegrations, Capabilities.ForRole("Regional Manager"));
        Assert.DoesNotContain(Capabilities.ManageIntegrations, Capabilities.ForRole("Maintenance Supervisor"));
        Assert.DoesNotContain(Capabilities.ManageIntegrations, Capabilities.ForRole("Read Only"));
    }

    [Fact]
    public void Only_admin_and_property_manager_manage_members()
    {
        Assert.Contains(Capabilities.ManageMembers, Capabilities.ForRole("Organization Admin"));
        Assert.Contains(Capabilities.ManageMembers, Capabilities.ForRole("Property Manager"));
        Assert.DoesNotContain(Capabilities.ManageMembers, Capabilities.ForRole("Regional Manager"));
        Assert.DoesNotContain(Capabilities.ManageMembers, Capabilities.ForRole("Maintenance Supervisor"));
        Assert.DoesNotContain(Capabilities.ManageMembers, Capabilities.ForRole("Read Only"));
    }

    // FS-S09. The decision this pins: the ledger reaches Property Manager as well as Organization
    // Admin, and nobody else. There is no Accountant/Controller role in the fixed matrix, so
    // excluding PM would leave Organization Admin as the only role that can post a journal at all;
    // an organization that wants accounting off its PMs revokes Accounting.Manage for that role
    // through RoleCapabilityOverrides (PF-S01.04) rather than through a code change.
    [Fact]
    public void Only_admin_and_property_manager_reach_the_general_ledger()
    {
        Assert.Contains(Capabilities.ManageAccounting, Capabilities.ForRole("Organization Admin"));
        Assert.Contains(Capabilities.ManageAccounting, Capabilities.ForRole("Property Manager"));
        foreach (var role in new[] { "Regional Manager", "Maintenance Supervisor", "Read Only" })
            Assert.DoesNotContain(Capabilities.ManageAccounting, Capabilities.ForRole(role));
        Assert.DoesNotContain(Capabilities.ManageAccounting, Capabilities.ForRole("Technician", employeeId: Guid.NewGuid()));
        Assert.DoesNotContain(Capabilities.ManageAccounting, Capabilities.ForRole("Vendor", vendorId: Guid.NewGuid()));
        Assert.DoesNotContain(Capabilities.ManageAccounting, Capabilities.ForRole("Resident"));
    }

    [Fact]
    public void Only_admin_and_property_manager_manage_billing()
    {
        // FS-S08 decision: ManageBilling is left to the "All minus the portal capabilities"
        // branch of ForRole, so it reaches Organization Admin and Property Manager and nobody
        // else. Billing is an accounting surface; the maintenance and field roles enumerate
        // their capabilities explicitly and do not get it.
        Assert.Contains(Capabilities.ManageBilling, Capabilities.ForRole("Organization Admin"));
        Assert.Contains(Capabilities.ManageBilling, Capabilities.ForRole("Property Manager"));
        Assert.DoesNotContain(Capabilities.ManageBilling, Capabilities.ForRole("Regional Manager"));
        Assert.DoesNotContain(Capabilities.ManageBilling, Capabilities.ForRole("Maintenance Supervisor"));
        Assert.DoesNotContain(Capabilities.ManageBilling, Capabilities.ForRole("Read Only"));
        Assert.DoesNotContain(Capabilities.ManageBilling, Capabilities.ForRole("Technician", employeeId: Guid.NewGuid()));
        Assert.DoesNotContain(Capabilities.ManageBilling, Capabilities.ForRole("Vendor", vendorId: Guid.NewGuid()));
        Assert.DoesNotContain(Capabilities.ManageBilling, Capabilities.ForRole("Resident"));
    }

    [Fact]
    public void Read_only_role_can_only_read_work()
    {
        Assert.Equal([Capabilities.ReadWork], Capabilities.ForRole("Read Only"));
    }

    [Theory]
    [InlineData("Technician")]
    [InlineData("Vendor")]
    [InlineData("")]
    [InlineData("Unrecognized")]
    public void Field_roles_without_a_binding_and_unknown_roles_receive_nothing(string role)
    {
        Assert.Empty(Capabilities.ForRole(role));
    }

    [Fact]
    public void Field_roles_receive_a_narrow_set_when_bound_to_their_field_identity()
    {
        // A bound technician can read, mark on the way, and attach photos to their own work; the
        // endpoint scope check narrows all three to assigned work. A vendor stays read-only.
        Assert.Equal([Capabilities.ReadWork, Capabilities.MarkOnTheWay, Capabilities.ManageAttachments],
            Capabilities.ForRole("Technician", employeeId: Guid.NewGuid()));
        Assert.Equal([Capabilities.ReadWork], Capabilities.ForRole("Vendor", vendorId: Guid.NewGuid()));
        Assert.Empty(Capabilities.ForRole("Technician", vendorId: Guid.NewGuid()));
        Assert.Empty(Capabilities.ForRole("Vendor", employeeId: Guid.NewGuid()));
    }

    [Fact]
    public void Attachment_management_reaches_the_hands_on_work_roles_but_not_read_only()
    {
        foreach (var role in new[] { "Organization Admin", "Property Manager", "Regional Manager", "Maintenance Supervisor" })
            Assert.Contains(Capabilities.ManageAttachments, Capabilities.ForRole(role));
        Assert.DoesNotContain(Capabilities.ManageAttachments, Capabilities.ForRole("Read Only"));
        Assert.DoesNotContain(Capabilities.ManageAttachments, Capabilities.ForRole("Vendor", vendorId: Guid.NewGuid()));
    }

    // FS-S05. The decision these three pin: applicant workflow and applicant PII are two
    // capabilities, not one. Regional Manager is the role that proves the split is real - it
    // works applications but cannot read unmasked applicant detail, so the PF-S05.05 PII
    // endpoint has a control that can be denied to somebody who still holds the workflow.
    [Theory]
    [InlineData("Organization Admin")]
    [InlineData("Property Manager")]
    public void Admin_and_property_manager_hold_both_application_capabilities(string role)
    {
        var capabilities = Capabilities.ForRole(role);
        Assert.Contains(Capabilities.ManageApplications, capabilities);
        Assert.Contains(Capabilities.ReadApplicantPii, capabilities);
    }

    [Fact]
    public void Regional_manager_works_applications_but_cannot_read_applicant_pii()
    {
        var capabilities = Capabilities.ForRole("Regional Manager");
        Assert.Contains(Capabilities.ManageApplications, capabilities);
        Assert.DoesNotContain(Capabilities.ReadApplicantPii, capabilities);
    }

    [Fact]
    public void No_other_role_holds_either_application_capability()
    {
        foreach (var capabilities in new[]
                 {
                     Capabilities.ForRole("Maintenance Supervisor"),
                     Capabilities.ForRole("Read Only"),
                     Capabilities.ForRole("Technician", employeeId: Guid.NewGuid()),
                     Capabilities.ForRole("Vendor", vendorId: Guid.NewGuid()),
                     Capabilities.ForRole("Owner"),
                     Capabilities.ForRole("Resident")
                 })
        {
            Assert.DoesNotContain(Capabilities.ManageApplications, capabilities);
            Assert.DoesNotContain(Capabilities.ReadApplicantPii, capabilities);
        }
    }

    [Fact]
    public void Regional_manager_keeps_every_capability_it_held_before_applications_were_added()
    {
        // The shared CategoryManagement array is spread at the Regional Manager call site rather
        // than extended. This pins the pre-FS-S05 set as a subset, so a careless edit to that
        // shared array - which other role arms may later use - is caught here.
        string[] before =
        [
            Capabilities.ReadWork, Capabilities.AssignVendor, Capabilities.AssignEmployee,
            Capabilities.CreateWork, Capabilities.UpdateWork, Capabilities.ManageCategories,
            Capabilities.ManageAssets, Capabilities.ManageAttachments
        ];

        var capabilities = Capabilities.ForRole("Regional Manager");

        Assert.All(before, c => Assert.Contains(c, capabilities));
        Assert.Equal(before.Length + 1, capabilities.Count);
    }

    [Fact]
    public void Both_application_capabilities_have_an_authorization_policy()
    {
        // Program.cs:145-150 builds one authorization policy per entry in All. A capability
        // missing from All has no policy, and RequireAuthorization for it fails at runtime
        // rather than at compile time.
        Assert.Contains(Capabilities.ManageApplications, Capabilities.All);
        Assert.Contains(Capabilities.ReadApplicantPii, Capabilities.All);
    }

    [Fact]
    public void All_contains_every_capability_constant_without_duplicates()
    {
        var constants = typeof(Capabilities)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(constants);
        Assert.All(constants, c => Assert.Contains(c, Capabilities.All));
        Assert.Equal(Capabilities.All.Count, Capabilities.All.Distinct().Count());
    }
}
