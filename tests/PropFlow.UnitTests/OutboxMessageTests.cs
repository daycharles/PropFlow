using PropFlow.Domain.Communications;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class OutboxMessageTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateTimeOffset When = DateTimeOffset.Parse("2026-09-09T09:00:00-04:00");
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StaleTimeout = TimeSpan.FromMinutes(5);
    private const int MaxAttempts = 8;

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
    public void Provider_callback_records_terminal_status_and_is_replay_safe()
    {
        var message = Sms();
        message.BeginDelivery(When);
        message.MarkSent("provider-1", When);

        message.ApplyProviderCallback(ProviderDeliveryStatus.Delivered, null, When.AddMinutes(1));
        message.ApplyProviderCallback(ProviderDeliveryStatus.Failed, "late duplicate", When);

        Assert.Equal(ProviderDeliveryStatus.Delivered, message.ProviderDeliveryStatus);
        Assert.Equal(When.AddMinutes(1), message.ProviderUpdatedAt);
        Assert.Equal(OutboxStatus.Sent, message.Status);
    }

    [Fact]
    public void Failed_provider_callback_requires_a_reason()
    {
        var message = Sms();
        Assert.Throws<ArgumentException>(() =>
            message.ApplyProviderCallback(ProviderDeliveryStatus.Failed, null, When));
    }

    [Fact]
    public void RecordFailedAttempt_keeps_the_message_pending_until_the_cap()
    {
        var message = Sms();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            message.BeginDelivery(When);
            message.RecordFailedAttempt("provider down", When, MaxAttempts);

            var expected = attempt >= MaxAttempts ? OutboxStatus.Failed : OutboxStatus.Pending;
            Assert.Equal(expected, message.Status);
            Assert.Equal("provider down", message.FailureReason);
        }
    }

    [Fact]
    public void RecordFailedAttempt_validates_its_arguments()
    {
        var message = Sms();
        message.BeginDelivery(When);
        Assert.Throws<ArgumentException>(() => message.RecordFailedAttempt(" ", When, MaxAttempts));
        Assert.Throws<ArgumentOutOfRangeException>(() => message.RecordFailedAttempt("x", When, 0));
    }

    [Fact]
    public void A_first_attempt_is_claimable_immediately()
    {
        Assert.True(Sms().IsClaimable(When, RetryDelay, StaleTimeout));
    }

    [Fact]
    public void A_failed_attempt_is_not_reclaimable_until_the_retry_delay_elapses()
    {
        var now = DateTimeOffset.Parse("2026-09-09T09:00:00Z");
        var message = Sms(now.AddHours(-1));
        message.BeginDelivery(now);
        message.RecordFailedAttempt("down", now, MaxAttempts);

        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.False(message.IsClaimable(now, RetryDelay, StaleTimeout));
        Assert.False(message.IsClaimable(now.AddMinutes(1), RetryDelay, StaleTimeout));
        Assert.True(message.IsClaimable(now.AddMinutes(2), RetryDelay, StaleTimeout));
    }

    [Fact]
    public void A_claimed_message_is_only_reclaimable_once_the_claim_goes_stale()
    {
        var now = DateTimeOffset.Parse("2026-09-09T09:00:00Z");
        var message = Sms(now.AddHours(-1));
        message.BeginDelivery(now);

        Assert.False(message.IsClaimable(now, RetryDelay, StaleTimeout));
        Assert.False(message.IsClaimable(now.AddMinutes(4), RetryDelay, StaleTimeout));
        Assert.True(message.IsClaimable(now.AddMinutes(6), RetryDelay, StaleTimeout));
    }

    [Fact]
    public void A_terminal_message_is_never_claimable()
    {
        var message = Sms();
        message.BeginDelivery(When);
        message.MarkSent("ref", When);

        Assert.False(message.IsClaimable(When.AddDays(1), RetryDelay, StaleTimeout));
    }
}
