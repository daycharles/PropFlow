using PropFlow.Domain.Assets;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class AssetTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Property = Guid.NewGuid();

    private static Asset New() => new(Org, Guid.NewGuid(), Property, null, AssetKind.Hvac, "Rooftop HVAC 1");

    [Fact]
    public void Requires_a_property_and_a_single_line_name()
    {
        Assert.Throws<ArgumentException>(() => new Asset(Org, Guid.NewGuid(), Guid.Empty, null, AssetKind.Hvac, "x"));
        Assert.Throws<ArgumentException>(() => new Asset(Org, Guid.NewGuid(), Property, null, AssetKind.Hvac, "   "));
        Assert.Throws<ArgumentException>(() =>
            new Asset(Org, Guid.NewGuid(), Property, null, AssetKind.Hvac, "line" + ((char)10).ToString() + "break"));
    }

    [Fact]
    public void Describe_updates_identifiers_and_rejects_control_characters()
    {
        var asset = New();
        asset.Describe("Chiller 2", AssetKind.Hvac, "  Trane ", "RTU-25", "SN-0001");

        Assert.Equal("Chiller 2", asset.Name);
        Assert.Equal("Trane", asset.Manufacturer);
        Assert.Equal("RTU-25", asset.Model);

        Assert.Throws<ArgumentException>(() =>
            asset.Describe("Name", AssetKind.Hvac, "bad" + ((char)9).ToString() + "mfr", null, null));
    }

    [Fact]
    public void SetLifecycle_validates_dates_and_service_life()
    {
        var asset = New();
        Assert.Throws<ArgumentException>(() =>
            asset.SetLifecycle(new DateOnly(2020, 1, 1), new DateOnly(2019, 1, 1), 10));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            asset.SetLifecycle(new DateOnly(2020, 1, 1), null, -1));

        asset.SetLifecycle(new DateOnly(2018, 6, 1), new DateOnly(2028, 6, 1), 15);
        Assert.Equal(new DateOnly(2018, 6, 1), asset.InstalledOn);
        Assert.Equal(15, asset.ExpectedServiceLifeYears);
    }

    [Fact]
    public void Warranty_and_age_are_computed_from_the_lifecycle_dates()
    {
        var asset = New();
        asset.SetLifecycle(new DateOnly(2018, 6, 1), new DateOnly(2028, 6, 1), 15);

        Assert.True(asset.IsUnderWarranty(new DateOnly(2028, 6, 1)));
        Assert.False(asset.IsUnderWarranty(new DateOnly(2028, 6, 2)));

        Assert.Equal(7, asset.AgeInYears(new DateOnly(2025, 12, 1)));
        Assert.Equal(6, asset.AgeInYears(new DateOnly(2025, 5, 31))); // birthday not yet reached
        Assert.Null(asset.AgeInYears(new DateOnly(2017, 1, 1)));      // before installation
    }

    [Fact]
    public void Age_is_null_without_an_installation_date()
    {
        Assert.Null(New().AgeInYears(new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Replacement_cost_is_non_negative_and_rounded()
    {
        var asset = New();
        Assert.Throws<ArgumentOutOfRangeException>(() => asset.SetReplacementCost(-1m));

        asset.SetReplacementCost(12345.678m);
        Assert.Equal(12345.68m, asset.ReplacementCostEstimate);

        asset.SetReplacementCost(null);
        Assert.Null(asset.ReplacementCostEstimate);
    }

    [Fact]
    public void Notes_reject_control_characters_but_allow_newlines()
    {
        var asset = New();
        asset.SetNotes("Compressor replaced" + ((char)10).ToString() + "Check warranty next visit");
        Assert.Contains("Compressor replaced", asset.Notes);

        Assert.Throws<ArgumentException>(() => asset.SetNotes("bell" + ((char)7).ToString() + "here"));
    }

    [Fact]
    public void Condition_defaults_unknown_and_is_recordable()
    {
        var asset = New();
        Assert.Equal(AssetCondition.Unknown, asset.Condition);
        asset.RecordCondition(AssetCondition.Poor);
        Assert.Equal(AssetCondition.Poor, asset.Condition);
    }
}
