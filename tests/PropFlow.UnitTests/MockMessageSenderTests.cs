using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;
using PropFlow.Infrastructure.Communications;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class MockMessageSenderTests
{
    [Fact]
    public void Senders_report_their_channel()
    {
        var log = new InMemorySentMessageLog();
        Assert.Equal(MessageChannel.Sms, new MockSmsSender(log).Channel);
        Assert.Equal(MessageChannel.Email, new MockEmailSender(log).Channel);
    }

    [Fact]
    public async Task Records_the_delivery_and_returns_a_provider_reference()
    {
        var log = new InMemorySentMessageLog();
        var sender = new MockSmsSender(log);
        var message = new OutboundMessage(MessageChannel.Sms, "+15550001111", null, "On the way.");

        var result = await sender.SendAsync(message, "work-1-otw", default);

        Assert.Equal(MessageDeliveryStatus.Sent, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.ProviderReference));
        var recorded = Assert.Single(log.Sent);
        Assert.Equal("work-1-otw", recorded.IdempotencyKey);
        Assert.Equal("On the way.", recorded.Body);
    }

    [Fact]
    public async Task A_repeat_send_for_the_same_key_returns_the_first_result_without_recording_again()
    {
        var log = new InMemorySentMessageLog();
        var sender = new MockEmailSender(log);
        var message = new OutboundMessage(MessageChannel.Email, "r@example.test", "Subj", "Body");

        var first = await sender.SendAsync(message, "dup", default);
        var second = await sender.SendAsync(message, "dup", default);

        Assert.Equal(first.ProviderReference, second.ProviderReference);
        Assert.Single(log.Sent);
    }
}
