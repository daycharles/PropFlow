using PropFlow.Domain.Configuration;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class NumberingSequenceTests
{
    private static NumberingSequence Create(string? prefix = "WO-", int width = 5) =>
        NumberingSequence.Create(Guid.NewGuid(), Guid.NewGuid(), ConfigurationEntityType.WorkItem, prefix, width, DateTimeOffset.UtcNow);

    [Fact]
    public void A_sequence_starts_at_one()
    {
        var sequence = Create();
        Assert.Equal(1, sequence.NextValue);
        Assert.Equal(ConfigurationEntityType.WorkItem, sequence.AppliesTo);
    }

    [Fact]
    public void A_null_prefix_becomes_empty_not_an_error()
    {
        var sequence = Create(prefix: null);
        Assert.Equal("", sequence.Prefix);
    }

    [Fact]
    public void A_prefix_over_twenty_characters_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Create(prefix: new string('x', 21)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Width_outside_one_to_ten_is_rejected(int width)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(width: width));
    }

    [Fact]
    public void Configure_changes_prefix_and_width_but_never_NextValue()
    {
        var sequence = Create();
        sequence.Configure("INC-", 3);
        Assert.Equal("INC-", sequence.Prefix);
        Assert.Equal(3, sequence.Width);
        Assert.Equal(1, sequence.NextValue);
    }

    [Theory]
    [InlineData("WO-", 5, 1, "WO-00001")]
    [InlineData("WO-", 5, 42, "WO-00042")]
    [InlineData("", 3, 7, "007")]
    [InlineData("INC-", 1, 12345, "INC-12345")]
    public void Format_zero_pads_to_width_and_never_truncates_a_larger_value(string prefix, int width, long value, string expected)
    {
        Assert.Equal(expected, NumberingSequence.Format(prefix, width, value));
    }

    [Fact]
    public void FormatNext_previews_the_current_NextValue()
    {
        var sequence = Create(prefix: "WO-", width: 4);
        Assert.Equal("WO-0001", sequence.FormatNext());
    }
}
