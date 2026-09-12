using PropFlow.Domain.Configuration;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class OrganizationSettingsTests
{
    private static OrganizationSettings Create(string timeZone = "America/New_York") =>
        OrganizationSettings.Create(Guid.NewGuid(), Guid.NewGuid(), timeZone, DateTimeOffset.UtcNow);

    private static BusinessHoursWindow[] OpenWeekdaysNineToFive() =>
        Enum.GetValues<DayOfWeek>().Select(d => d is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? new BusinessHoursWindow(d, null, null)
            : new BusinessHoursWindow(d, new TimeOnly(9, 0), new TimeOnly(17, 0))).ToArray();

    [Fact]
    public void A_new_settings_row_starts_closed_every_day()
    {
        var settings = Create();
        Assert.All(settings.ReadBusinessHours(), h =>
        {
            Assert.Null(h.Open);
            Assert.Null(h.Close);
        });
        Assert.Equal(7, settings.ReadBusinessHours().Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("UTC")]
    [InlineData("Not A Zone")]
    public void An_invalid_time_zone_is_rejected(string timeZone)
    {
        Assert.Throws<ArgumentException>(() => Create(timeZone));
    }

    [Fact]
    public void Setting_a_full_week_round_trips()
    {
        var settings = Create();
        var hours = OpenWeekdaysNineToFive();
        settings.SetBusinessHours(hours);

        var read = settings.ReadBusinessHours();
        Assert.Equal(7, read.Count);
        Assert.Equal(new TimeOnly(9, 0), read.Single(h => h.Day == DayOfWeek.Monday).Open);
        Assert.Equal(new TimeOnly(17, 0), read.Single(h => h.Day == DayOfWeek.Monday).Close);
        Assert.Null(read.Single(h => h.Day == DayOfWeek.Saturday).Open);
    }

    [Fact]
    public void A_day_missing_from_the_week_is_rejected()
    {
        var settings = Create();
        var hours = OpenWeekdaysNineToFive().Where(h => h.Day != DayOfWeek.Friday).ToArray();
        Assert.Throws<ArgumentException>(() => settings.SetBusinessHours(hours));
    }

    [Fact]
    public void A_duplicate_day_is_rejected()
    {
        var settings = Create();
        var hours = OpenWeekdaysNineToFive();
        hours[6] = hours[0]; // duplicate one day in place of another, still 7 entries total
        Assert.Throws<ArgumentException>(() => settings.SetBusinessHours(hours));
    }

    [Fact]
    public void An_open_time_without_a_close_time_is_rejected()
    {
        var settings = Create();
        var hours = OpenWeekdaysNineToFive();
        hours[1] = hours[1] with { Close = null };
        Assert.Throws<ArgumentException>(() => settings.SetBusinessHours(hours));
    }

    [Fact]
    public void An_open_time_at_or_after_the_close_time_is_rejected()
    {
        var settings = Create();
        var hours = OpenWeekdaysNineToFive();
        hours[1] = hours[1] with { Open = new TimeOnly(17, 0), Close = new TimeOnly(9, 0) };
        Assert.Throws<ArgumentException>(() => settings.SetBusinessHours(hours));
    }

    [Fact]
    public void The_default_time_zone_can_be_changed()
    {
        var settings = Create("America/New_York");
        settings.SetDefaultTimeZone("America/Los_Angeles");
        Assert.Equal("America/Los_Angeles", settings.DefaultTimeZoneId);
    }
}
