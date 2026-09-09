using PropFlow.Domain.Communications;

namespace PropFlow.Application.Communications;

// A fully rendered message handed to a provider. No template placeholders remain.
public sealed class OutboundMessage
{
    public OutboundMessage(MessageChannel channel, string recipientAddress, string? subject, string body)
    {
        if (string.IsNullOrWhiteSpace(recipientAddress))
            throw new ArgumentException("A recipient address is required.", nameof(recipientAddress));
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("A message body is required.", nameof(body));

        var trimmedSubject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        if (channel == MessageChannel.Email && trimmedSubject is null)
            throw new ArgumentException("Email messages require a subject.", nameof(subject));
        if (channel == MessageChannel.Sms && trimmedSubject is not null)
            throw new ArgumentException("SMS messages must not carry a subject.", nameof(subject));

        Channel = channel;
        RecipientAddress = recipientAddress.Trim();
        Subject = trimmedSubject;
        Body = body.Trim();
    }

    public MessageChannel Channel { get; }
    public string RecipientAddress { get; }
    public string? Subject { get; }
    public string Body { get; }
}

public enum MessageDeliveryStatus
{
    Sent = 1,
    Failed = 2
}

public sealed record MessageDeliveryResult(MessageDeliveryStatus Status, string? ProviderReference, string? FailureReason)
{
    public static MessageDeliveryResult Delivered(string providerReference) =>
        new(MessageDeliveryStatus.Sent, providerReference, null);

    public static MessageDeliveryResult Rejected(string failureReason) =>
        new(MessageDeliveryStatus.Failed, null, failureReason);
}

// One implementation per channel. Milestone 4 ships mock SMS and email providers; real
// providers slot in behind the same contract with the outbox unchanged. The idempotency key
// lets a provider (or the mock) collapse a re-delivery after a transient failure.
public interface IMessageSender
{
    MessageChannel Channel { get; }

    Task<MessageDeliveryResult> SendAsync(OutboundMessage message, string idempotencyKey, CancellationToken cancellationToken);
}
