using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class OutboundMessageTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_recipient(string recipient)
    {
        Assert.Throws<ArgumentException>(() => new OutboundMessage(MessageChannel.Sms, recipient, null, "Body"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_body(string body)
    {
        Assert.Throws<ArgumentException>(() => new OutboundMessage(MessageChannel.Sms, "+15550001111", null, body));
    }

    [Fact]
    public void Email_requires_a_subject()
    {
        Assert.Throws<ArgumentException>(() => new OutboundMessage(MessageChannel.Email, "resident@example.test", null, "Body"));
    }

    [Fact]
    public void Sms_rejects_a_subject()
    {
        Assert.Throws<ArgumentException>(() => new OutboundMessage(MessageChannel.Sms, "+15550001111", "Subject", "Body"));
    }

    [Fact]
    public void Trims_every_field()
    {
        var message = new OutboundMessage(MessageChannel.Email, " resident@example.test ", " Visit ", " Body ");

        Assert.Equal("resident@example.test", message.RecipientAddress);
        Assert.Equal("Visit", message.Subject);
        Assert.Equal("Body", message.Body);
    }

    [Fact]
    public void Delivery_result_factories_set_the_expected_shape()
    {
        var delivered = MessageDeliveryResult.Delivered("ref-1");
        Assert.Equal(MessageDeliveryStatus.Sent, delivered.Status);
        Assert.Equal("ref-1", delivered.ProviderReference);
        Assert.Null(delivered.FailureReason);

        var rejected = MessageDeliveryResult.Rejected("nope");
        Assert.Equal(MessageDeliveryStatus.Failed, rejected.Status);
        Assert.Null(rejected.ProviderReference);
        Assert.Equal("nope", rejected.FailureReason);
    }
}
