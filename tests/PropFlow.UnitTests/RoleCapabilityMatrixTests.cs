using PropFlow.Application;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class RoleCapabilityMatrixTests
{
    [Fact]
    public void No_overrides_returns_exactly_the_defaults_in_order()
    {
        string[] defaults = [Capabilities.ReadWork, Capabilities.AssignVendor];

        Assert.Equal(defaults, RoleCapabilityMatrix.Effective(defaults, [], []));
    }

    [Fact]
    public void A_grant_adds_a_capability_the_defaults_do_not_have()
    {
        string[] defaults = [Capabilities.ReadWork];

        var effective = RoleCapabilityMatrix.Effective(defaults, [Capabilities.ManageAssets], []);

        Assert.Equal([Capabilities.ReadWork, Capabilities.ManageAssets], effective);
    }

    [Fact]
    public void A_revocation_removes_a_default_capability()
    {
        string[] defaults = [Capabilities.ReadWork, Capabilities.AssignVendor];

        var effective = RoleCapabilityMatrix.Effective(defaults, [], [Capabilities.AssignVendor]);

        Assert.Equal([Capabilities.ReadWork], effective);
    }

    [Fact]
    public void Revocation_wins_over_a_grant_of_the_same_capability()
    {
        var effective = RoleCapabilityMatrix.Effective([], [Capabilities.ManageAssets], [Capabilities.ManageAssets]);

        Assert.Empty(effective);
    }

    [Fact]
    public void A_capability_present_in_both_defaults_and_grants_is_not_duplicated()
    {
        string[] defaults = [Capabilities.ReadWork];

        var effective = RoleCapabilityMatrix.Effective(defaults, [Capabilities.ReadWork], []);

        Assert.Equal([Capabilities.ReadWork], effective);
    }

    [Fact]
    public void Revoking_a_capability_that_was_never_granted_is_a_harmless_no_op()
    {
        string[] defaults = [Capabilities.ReadWork];

        var effective = RoleCapabilityMatrix.Effective(defaults, [], [Capabilities.ManageIntegrations]);

        Assert.Equal([Capabilities.ReadWork], effective);
    }
}
