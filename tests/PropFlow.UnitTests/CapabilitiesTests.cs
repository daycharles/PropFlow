using PropFlow.Application;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class CapabilitiesTests
{
    [Theory]
    [InlineData("Organization Admin")]
    [InlineData("Property Manager")]
    public void Admin_and_property_manager_can_manage_templates(string role)
    {
        Assert.Contains(Capabilities.ManageTemplates, Capabilities.ForRole(role));
        Assert.Contains(Capabilities.ReadWork, Capabilities.ForRole(role));
        Assert.Contains(Capabilities.AssignVendor, Capabilities.ForRole(role));
    }

    [Theory]
    [InlineData("Regional Manager")]
    [InlineData("Maintenance Supervisor")]
    public void Other_managers_assign_work_but_do_not_manage_templates(string role)
    {
        var capabilities = Capabilities.ForRole(role);
        Assert.Contains(Capabilities.ReadWork, capabilities);
        Assert.Contains(Capabilities.AssignVendor, capabilities);
        Assert.DoesNotContain(Capabilities.ManageTemplates, capabilities);
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
    public void Field_and_unknown_roles_receive_nothing(string role)
    {
        Assert.Empty(Capabilities.ForRole(role));
    }

    [Fact]
    public void All_lists_every_named_capability()
    {
        Assert.Equal(
            [Capabilities.ReadWork, Capabilities.AssignVendor, Capabilities.ManageTemplates],
            Capabilities.All);
    }
}
