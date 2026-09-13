using PropFlow.Domain.Reporting;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class ReportScheduleTests
{
    [Fact]
    public void Delivery_advances_by_frequency_and_counts_once()
    {
        var schedule = new ReportSchedule(Guid.NewGuid(), Guid.NewGuid(), "Daily", "operational", ReportScheduleFrequency.Daily, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), "ops@example.test");
        schedule.MarkDelivered(new DateTimeOffset(2026, 9, 12, 1, 0, 0, TimeSpan.Zero));
        Assert.Equal(1, schedule.DeliveryCount); Assert.Equal(new DateTimeOffset(2026, 9, 13, 1, 0, 0, TimeSpan.Zero), schedule.NextRunAt);
    }

    [Fact]
    public void Invalid_schedule_values_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ReportSchedule(Guid.NewGuid(), Guid.NewGuid(), "", "operational", ReportScheduleFrequency.Daily, DateTimeOffset.UtcNow, "ops@example.test"));
        Assert.Throws<ArgumentException>(() => new ReportSchedule(Guid.NewGuid(), Guid.NewGuid(), "Daily", "", ReportScheduleFrequency.Daily, DateTimeOffset.UtcNow, "ops@example.test"));
    }
}
