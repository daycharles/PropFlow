using PropFlow.Domain.Accounting;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class FiscalPeriodTests
{
    private static readonly Guid Organization = Guid.NewGuid();

    private static FiscalPeriod Period(string name = "2026-01") =>
        new(Organization, Guid.NewGuid(), name, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

    [Fact]
    public void Period_cannot_end_before_it_starts()
    {
        Assert.Throws<ArgumentException>(() =>
            new FiscalPeriod(Organization, Guid.NewGuid(), "2026-01", new DateOnly(2026, 1, 31), new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void A_single_day_period_is_allowed()
    {
        var day = new DateOnly(2026, 1, 1);
        var period = new FiscalPeriod(Organization, Guid.NewGuid(), "2026-01-01", day, day);
        Assert.True(period.Covers(day));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Period_refuses_a_blank_name(string blank)
    {
        Assert.Throws<ArgumentException>(() => Period(blank));
    }

    [Fact]
    public void Coverage_includes_both_end_dates_and_excludes_everything_outside()
    {
        var period = Period();
        Assert.True(period.Covers(new DateOnly(2026, 1, 1)));
        Assert.True(period.Covers(new DateOnly(2026, 1, 31)));
        Assert.False(period.Covers(new DateOnly(2025, 12, 31)));
        Assert.False(period.Covers(new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void A_new_period_is_open_and_closing_records_the_actor_and_the_time()
    {
        var period = Period();
        var actor = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 2, 3, 9, 30, 0, TimeSpan.FromHours(-5));
        Assert.False(period.IsClosed);
        Assert.Equal(FiscalPeriodStatus.Open, period.Status);

        period.Close(actor, at);

        Assert.True(period.IsClosed);
        Assert.Equal(FiscalPeriodStatus.Closed, period.Status);
        Assert.Equal(actor, period.ClosedBy);
        Assert.Equal(at.ToUniversalTime(), period.ClosedAt);
    }

    [Fact]
    public void Closing_an_already_closed_period_is_refused()
    {
        var period = Period();
        period.Close(Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        Assert.Throws<InvalidOperationException>(() => period.Close(Guid.NewGuid(), DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Closing_requires_an_actor()
    {
        Assert.Throws<ArgumentException>(() => Period().Close(Guid.Empty, DateTimeOffset.UnixEpoch));
    }
}
