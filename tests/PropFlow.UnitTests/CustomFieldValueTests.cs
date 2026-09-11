using PropFlow.Domain.Configuration;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class CustomFieldValueTests
{
    [Fact]
    public void A_value_round_trips_its_constructor_arguments()
    {
        var org = Guid.NewGuid();
        var work = Guid.NewGuid();
        var definition = Guid.NewGuid();
        var value = CustomFieldValue.Create(org, Guid.NewGuid(), work, definition, "3 years remaining", DateTimeOffset.UtcNow);
        Assert.Equal(work, value.WorkId);
        Assert.Equal(definition, value.CustomFieldDefinitionId);
        Assert.Equal("3 years remaining", value.Value);
    }

    [Fact]
    public void An_empty_work_or_definition_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => CustomFieldValue.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty,
            Guid.NewGuid(), "x", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => CustomFieldValue.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.Empty, "x", DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_value_is_rejected_at_construction(string value)
    {
        Assert.Throws<ArgumentException>(() => CustomFieldValue.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), value, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_value_over_two_thousand_characters_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => CustomFieldValue.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), new string('a', 2001), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SetValue_mutates_in_place_and_rejects_blank()
    {
        var value = CustomFieldValue.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "old", DateTimeOffset.UtcNow);
        value.SetValue("new", DateTimeOffset.UtcNow);
        Assert.Equal("new", value.Value);
        Assert.Throws<ArgumentException>(() => value.SetValue("", DateTimeOffset.UtcNow));
    }
}
