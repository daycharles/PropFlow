namespace PropFlow.Domain.Communications;

public enum OutboxStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}

// A queued resident message. Written in the same transaction as the change that triggers it,
// then delivered out of band by the dispatcher. Idempotency is enforced per organization.
public sealed class OutboxMessage : TenantEntity
{
    // EF materialization.
    private OutboxMessage(Guid organizationId, Guid id) : base(organizationId, id) { }

    public OutboxMessage(Guid organizationId, Guid id, MessageChannel channel, string recipientAddress,
        string? subject, string body, string idempotencyKey, DateTimeOffset createdAt) : base(organizationId, id)
    {
        if (string.IsNullOrWhiteSpace(recipientAddress) || recipientAddress.Trim().Length > 320)
            throw new ArgumentException("Recipient address must contain 1 to 320 characters.", nameof(recipientAddress));
        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > 2000)
            throw new ArgumentException("Message body must contain 1 to 2000 characters.", nameof(body));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200)
            throw new ArgumentException("Idempotency key must contain 1 to 200 characters.", nameof(idempotencyKey));

        var trimmedSubject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        if (channel == MessageChannel.Email && trimmedSubject is null)
            throw new ArgumentException("Email messages require a subject.", nameof(subject));
        if (channel == MessageChannel.Sms && trimmedSubject is not null)
            throw new ArgumentException("SMS messages must not carry a subject.", nameof(subject));

        Channel = channel;
        RecipientAddress = recipientAddress.Trim();
        Subject = trimmedSubject;
        Body = body.Trim();
        IdempotencyKey = idempotencyKey.Trim();
        Status = OutboxStatus.Pending;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public MessageChannel Channel { get; private set; }
    public string RecipientAddress { get; private set; } = "";
    public string? Subject { get; private set; }
    public string Body { get; private set; } = "";
    public string IdempotencyKey { get; private set; } = "";
    public OutboxStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? ProviderReference { get; private set; }
    public string? FailureReason { get; private set; }

    public void MarkSent(string providerReference, DateTimeOffset at)
    {
        if (Status == OutboxStatus.Sent) return;
        if (string.IsNullOrWhiteSpace(providerReference))
            throw new ArgumentException("A provider reference is required.", nameof(providerReference));
        AttemptCount++;
        Status = OutboxStatus.Sent;
        ProviderReference = providerReference.Trim();
        FailureReason = null;
        LastAttemptAt = at.ToUniversalTime();
    }

    public void MarkFailed(string reason, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A failure reason is required.", nameof(reason));
        AttemptCount++;
        Status = OutboxStatus.Failed;
        FailureReason = reason.Trim();
        LastAttemptAt = at.ToUniversalTime();
    }
}
