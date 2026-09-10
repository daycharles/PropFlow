using PropFlow.Domain.Assets;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class RepeatRepairPolicyTests
{
    private static readonly Guid Org = Guid.NewGuid();

    private static RepeatRepairPolicy New() => new(Org, Guid.NewGuid());

    [Fact]
    public void Defaults_are_three_repairs_in_a_hundred_and_twenty_days_without_category_matching()
    {
        var policy = New();
        Assert.Equal(3, policy.RepairThreshold);
        Assert.Equal(120, policy.WindowDays);
        Assert.False(policy.MatchByCategory);
    }

    [Theory]
    [InlineData(1, 120)]
    [InlineData(51, 120)]
    [InlineData(3, 6)]
    [InlineData(3, 3651)]
    public void Configure_rejects_out_of_range_knobs(int threshold, int windowDays) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => New().Configure(threshold, windowDays, false));

    [Fact]
    public void Configure_stores_valid_knobs()
    {
        var policy = New();
        policy.Configure(5, 200, true);
        Assert.Equal(5, policy.RepairThreshold);
        Assert.Equal(200, policy.WindowDays);
        Assert.True(policy.MatchByCategory);
    }

    [Fact]
    public void Triggers_when_the_count_reaches_the_threshold()
    {
        var policy = New();
        policy.Configure(3, 120, false);
        Assert.False(policy.Triggers(2));
        Assert.True(policy.Triggers(3));
        Assert.True(policy.Triggers(9));
    }
}
