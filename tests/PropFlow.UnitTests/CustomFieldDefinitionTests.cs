using PropFlow.Domain.Configuration;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class CustomFieldDefinitionTests
{
    private static CustomFieldDefinition Text(bool required = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "warranty_note", "Warranty note", CustomFieldAppliesTo.WorkItem,
            CustomFieldType.Text, null, required, 0, DateTimeOffset.UtcNow);

    [Fact]
    public void A_field_round_trips_its_constructor_values()
    {
        var field = Text();
        Assert.Equal("warranty_note", field.Key);
        Assert.Equal("Warranty note", field.Name);
        Assert.Equal(CustomFieldAppliesTo.WorkItem, field.AppliesTo);
        Assert.Equal(CustomFieldType.Text, field.FieldType);
        Assert.False(field.IsArchived);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Has Upper")]
    [InlineData("has space")]
    [InlineData("1startswithdigit")]
    [InlineData("has-hyphen")]
    public void A_malformed_key_is_rejected(string key)
    {
        Assert.Throws<ArgumentException>(() => new CustomFieldDefinition(Guid.NewGuid(), Guid.NewGuid(), key, "Name",
            CustomFieldAppliesTo.WorkItem, CustomFieldType.Text, null, false, 0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_key_over_fifty_characters_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new CustomFieldDefinition(Guid.NewGuid(), Guid.NewGuid(),
            new string('a', 51), "Name", CustomFieldAppliesTo.WorkItem, CustomFieldType.Text, null, false, 0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SingleSelect_requires_at_least_one_option()
    {
        Assert.Throws<ArgumentException>(() => new CustomFieldDefinition(Guid.NewGuid(), Guid.NewGuid(), "priority_tier",
            "Priority tier", CustomFieldAppliesTo.WorkItem, CustomFieldType.SingleSelect, [], false, 0, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new CustomFieldDefinition(Guid.NewGuid(), Guid.NewGuid(), "priority_tier",
            "Priority tier", CustomFieldAppliesTo.WorkItem, CustomFieldType.SingleSelect, null, false, 0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SingleSelect_rejects_duplicate_options()
    {
        Assert.Throws<ArgumentException>(() => new CustomFieldDefinition(Guid.NewGuid(), Guid.NewGuid(), "priority_tier",
            "Priority tier", CustomFieldAppliesTo.WorkItem, CustomFieldType.SingleSelect, ["Gold", "gold"], false, 0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_non_SingleSelect_field_rejects_options()
    {
        Assert.Throws<ArgumentException>(() => new CustomFieldDefinition(Guid.NewGuid(), Guid.NewGuid(), "note", "Note",
            CustomFieldAppliesTo.WorkItem, CustomFieldType.Text, ["a"], false, 0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SingleSelect_round_trips_its_options()
    {
        var field = new CustomFieldDefinition(Guid.NewGuid(), Guid.NewGuid(), "priority_tier", "Priority tier",
            CustomFieldAppliesTo.WorkItem, CustomFieldType.SingleSelect, ["Gold", "Silver"], false, 0, DateTimeOffset.UtcNow);
        Assert.Equal(["Gold", "Silver"], field.ReadOptions());
    }

    [Theory]
    [InlineData(CustomFieldType.Text, "anything", true)]
    [InlineData(CustomFieldType.Number, "12.5", true)]
    [InlineData(CustomFieldType.Number, "not-a-number", false)]
    [InlineData(CustomFieldType.Date, "2026-01-15", true)]
    [InlineData(CustomFieldType.Date, "not-a-date", false)]
    [InlineData(CustomFieldType.Boolean, "true", true)]
    [InlineData(CustomFieldType.Boolean, "yes", false)]
    public void Accepts_validates_by_field_type(CustomFieldType type, string raw, bool expected)
    {
        var field = new CustomFieldDefinition(Guid.NewGuid(), Guid.NewGuid(), "field", "Field",
            CustomFieldAppliesTo.WorkItem, type, null, false, 0, DateTimeOffset.UtcNow);
        Assert.Equal(expected, field.Accepts(raw));
    }

    [Fact]
    public void SingleSelect_only_accepts_a_listed_option()
    {
        var field = new CustomFieldDefinition(Guid.NewGuid(), Guid.NewGuid(), "tier", "Tier",
            CustomFieldAppliesTo.WorkItem, CustomFieldType.SingleSelect, ["Gold", "Silver"], false, 0, DateTimeOffset.UtcNow);
        Assert.True(field.Accepts("Gold"));
        Assert.False(field.Accepts("Bronze"));
    }

    [Fact]
    public void A_blank_value_is_accepted_only_when_the_field_is_optional()
    {
        Assert.True(Text(required: false).Accepts(null));
        Assert.True(Text(required: false).Accepts("  "));
        Assert.False(Text(required: true).Accepts(null));
        Assert.False(Text(required: true).Accepts(""));
    }

    [Fact]
    public void Rename_reorder_and_set_required_mutate_in_place()
    {
        var field = Text();
        field.Rename("Renamed");
        field.Reorder(5);
        field.SetRequired(true);
        Assert.Equal("Renamed", field.Name);
        Assert.Equal(5, field.SortOrder);
        Assert.True(field.IsRequired);
    }

    [Fact]
    public void Archive_is_one_way_and_leaves_the_row_in_place()
    {
        var field = Text();
        field.Archive();
        Assert.True(field.IsArchived);
        Assert.Equal("warranty_note", field.Key);
    }

    [Fact]
    public void ReplaceOptions_on_a_SingleSelect_field_revalidates()
    {
        var field = new CustomFieldDefinition(Guid.NewGuid(), Guid.NewGuid(), "tier", "Tier",
            CustomFieldAppliesTo.WorkItem, CustomFieldType.SingleSelect, ["Gold"], false, 0, DateTimeOffset.UtcNow);
        field.ReplaceOptions(["Gold", "Platinum"]);
        Assert.Equal(["Gold", "Platinum"], field.ReadOptions());
        Assert.Throws<ArgumentException>(() => field.ReplaceOptions([]));
    }
}
