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
