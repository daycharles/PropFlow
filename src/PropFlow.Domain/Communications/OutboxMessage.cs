namespace PropFlow.Domain.Communications;

public enum OutboxStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Sending = 3
}

// A queued resident message. Enqueued in the transaction that triggers it, then delivered out
// of band by the dispatcher. Idempotency is enforced per organization by the idempotency key;
// the xmin concurrency token lets competing dispatchers claim a message exactly once.
public sealed class OutboxMessage : TenantEntity
{
    // EF materialization only.
    private OutboxMessage(Guid organizationId, Guid id) : base(organizationId, id) { }

    public static OutboxMessage Create(Guid organizationId, Guid id, MessageChannel channel, string recipientAddress,
        string? subject, string body, string idempotencyKey, DateTimeOffset createdAt)
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

        return new OutboxMessage(organizationId, id)
        {
            Channel = channel,
            RecipientAddress = recipientAddress.Trim(),
            Subject = trimmedSubject,
            Body = body.Trim(),
            IdempotencyKey = idempotencyKey.Trim(),
            Status = OutboxStatus.Pending,
            CreatedAt = createdAt.ToUniversalTime()
        };
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

    // Pending: eligible once RetryDelay has elapsed since the last attempt (immediately on the
    // first). Sending: eligible only if the prior claim has gone stale.
    public bool IsClaimable(DateTimeOffset now, TimeSpan retryDelay, TimeSpan staleClaimTimeout)
    {
        var utcNow = now.ToUniversalTime();
        return Status switch
        {
            OutboxStatus.Pending => LastAttemptAt is not { } last || last <= utcNow - retryDelay,
            OutboxStatus.Sending => LastAttemptAt is { } claimed && claimed <= utcNow - staleClaimTimeout,
            _ => false
        };
    }

    // Claims the message for a delivery attempt. Saved under the concurrency token before the
    // provider is contacted so only one dispatcher proceeds.
    public void BeginDelivery(DateTimeOffset at)
    {
        if (Status is not (OutboxStatus.Pending or OutboxStatus.Sending))
            throw new InvalidOperationException($"A {Status} message cannot be delivered.");
        AttemptCount++;
        Status = OutboxStatus.Sending;
        LastAttemptAt = at.ToUniversalTime();
    }

    public void MarkSent(string providerReference, DateTimeOffset at)
    {
        if (Status == OutboxStatus.Sent) return;
        if (string.IsNullOrWhiteSpace(providerReference))
            throw new ArgumentException("A provider reference is required.", nameof(providerReference));
        Status = OutboxStatus.Sent;
        ProviderReference = providerReference.Trim();
        FailureReason = null;
        LastAttemptAt = at.ToUniversalTime();
    }

    // Records a failed attempt. The message stays retryable until the attempt cap, then fails.
    public void RecordFailedAttempt(string reason, DateTimeOffset at, int maxAttempts)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A failure reason is required.", nameof(reason));
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "At least one attempt is required.");
        FailureReason = reason.Trim();
        LastAttemptAt = at.ToUniversalTime();
        Status = AttemptCount >= maxAttempts ? OutboxStatus.Failed : OutboxStatus.Pending;
    }
}
