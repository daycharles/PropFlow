using PropFlow.Domain.Communications;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class OutboxMessageTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateTimeOffset When = DateTimeOffset.Parse("2026-09-09T09:00:00-04:00");

    private static OutboxMessage Sms(DateTimeOffset? createdAt = null) => OutboxMessage.Create(
        Org, Guid.NewGuid(), MessageChannel.Sms, "+15550001111", null, "On the way.", "k-" + Guid.NewGuid(),
        createdAt ?? When);

    [Fact]
    public void Create_starts_pending_with_no_attempts()
    {
        var message = Sms();

        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(0, message.AttemptCount);
        Assert.Equal(When.ToUniversalTime(), message.CreatedAt);
    }

    [Theory]
    [InlineData("", null, "Body", "key")]
    [InlineData("+15550001111", null, "", "key")]
    [InlineData("+15550001111", null, "Body", "")]
    public void Create_validates_required_fields(string recipient, string? subject, string body, string key)
    {
        Assert.Throws<ArgumentException>(() =>
            OutboxMessage.Create(Org, Guid.NewGuid(), MessageChannel.Sms, recipient, subject, body, key, When));
    }

    [Fact]
    public void Create_enforces_the_channel_subject_rule()
    {
        Assert.Throws<ArgumentException>(() =>
            OutboxMessage.Create(Org, Guid.NewGuid(), MessageChannel.Email, "r@example.test", null, "Body", "key", When));
        Assert.Throws<ArgumentException>(() =>
            OutboxMessage.Create(Org, Guid.NewGuid(), MessageChannel.Sms, "+15550001111", "Subject", "Body", "key", When));
    }

    [Fact]
    public void BeginDelivery_claims_the_message_and_counts_the_attempt()
    {
        var message = Sms();

        message.BeginDelivery(When);

        Assert.Equal(OutboxStatus.Sending, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal(When, message.LastAttemptAt);
        Assert.Equal(TimeSpan.Zero, message.LastAttemptAt!.Value.Offset);
    }

    [Fact]
    public void BeginDelivery_is_rejected_once_the_message_is_terminal()
    {
        var message = Sms();
        message.BeginDelivery(When);
        message.MarkSent("ref", When);

        Assert.Throws<InvalidOperationException>(() => message.BeginDelivery(When));
    }

    [Fact]
    public void MarkSent_records_the_provider_reference_once()
    {
        var message = Sms();
        message.BeginDelivery(When);

        message.MarkSent("mock-1", When);
        Assert.Equal(OutboxStatus.Sent, message.Status);
        Assert.Equal("mock-1", message.ProviderReference);
        Assert.Null(message.FailureReason);

        message.MarkSent("mock-2", When);
        Assert.Equal("mock-1", message.ProviderReference);
        Assert.Equal(1, message.AttemptCount);
    }

    [Fact]
    public void MarkSent_requires_a_provider_reference()
    {
        var message = Sms();
        message.BeginDelivery(When);
        Assert.Throws<ArgumentException>(() => message.MarkSent("  ", When));
    }

    [Fact]
    public void RecordFailedAttempt_keeps_the_message_pending_until_the_cap()
    {
        var message = Sms();

        for (var attempt = 1; attempt <= OutboxMessage.MaxDeliveryAttempts; attempt++)
        {
            message.BeginDelivery(When);
            message.RecordFailedAttempt("provider down", When);

            var expected = attempt >= OutboxMessage.MaxDeliveryAttempts ? OutboxStatus.Failed : OutboxStatus.Pending;
            Assert.Equal(expected, message.Status);
            Assert.Equal("provider down", message.FailureReason);
        }
    }

    [Fact]
    public void RecordFailedAttempt_requires_a_reason()
    {
        var message = Sms();
        message.BeginDelivery(When);
        Assert.Throws<ArgumentException>(() => message.RecordFailedAttempt(" ", When));
    }

    [Fact]
    public void IsClaimable_covers_pending_and_stale_sending_only()
    {
        var now = DateTimeOffset.Parse("2026-09-09T09:00:00Z");
        var stale = TimeSpan.FromMinutes(5);
        var message = Sms(now.AddHours(-1));

        Assert.True(message.IsClaimable(now, stale));

        message.BeginDelivery(now);
        Assert.False(message.IsClaimable(now, stale));
        Assert.False(message.IsClaimable(now.AddMinutes(4), stale));
        Assert.True(message.IsClaimable(now.AddMinutes(6), stale));

        message.MarkSent("ref", now.AddMinutes(6));
        Assert.False(message.IsClaimable(now.AddDays(1), stale));
    }
}
