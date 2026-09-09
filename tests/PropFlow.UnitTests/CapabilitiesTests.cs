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
    public void Other_managers_assign_work_but_do_not_manage_templates_or_people(string role)
    {
        var capabilities = Capabilities.ForRole(role);
        Assert.Contains(Capabilities.ReadWork, capabilities);
        Assert.Contains(Capabilities.AssignVendor, capabilities);
        Assert.DoesNotContain(Capabilities.ManageTemplates, capabilities);
        Assert.DoesNotContain(Capabilities.ManagePeople, capabilities);
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
