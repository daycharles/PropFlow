namespace PropFlow.Domain.Communications;

public enum OutboxStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Sending = 3
}
public enum ProviderDeliveryStatus { Unknown = 0, Delivered = 1, Failed = 2 }

// A queued resident message. Enqueued in the transaction that triggers it, then delivered out
// of band by the dispatcher. Idempotency is enforced per organization by the idempotency key;
// the xmin concurrency token lets competing dispatchers claim a message exactly once.
public sealed class OutboxMessage : TenantEntity
{
    // EF materialization only.
    private OutboxMessage(Guid organizationId, Guid id) : base(organizationId, id) { }

    public static OutboxMessage Create(Guid organizationId, Guid id, MessageChannel channel, string recipientAddress,
        string? subject, string body, string idempotencyKey, DateTimeOffset createdAt,
        Guid? workId = null, bool residentVisible = false)
    {
        var normalizedRecipient = MessageText.RequireSingleLine(recipientAddress, nameof(recipientAddress), 320);
        var normalizedBody = MessageText.RequireBody(body, nameof(body), 2000);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200)
            throw new ArgumentException("Idempotency key must contain 1 to 200 characters.", nameof(idempotencyKey));

        var trimmedSubject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        if (channel == MessageChannel.Email && trimmedSubject is null)
            throw new ArgumentException("Email messages require a subject.", nameof(subject));
        if (channel == MessageChannel.Sms && trimmedSubject is not null)
            throw new ArgumentException("SMS messages must not carry a subject.", nameof(subject));
        if (trimmedSubject is not null)
            trimmedSubject = MessageText.RequireSingleLine(trimmedSubject, nameof(subject), 200);

        if (workId == Guid.Empty) throw new ArgumentException("Work id cannot be empty.", nameof(workId));

        return new OutboxMessage(organizationId, id)
        {
            Channel = channel,
            RecipientAddress = normalizedRecipient,
            Subject = trimmedSubject,
            Body = normalizedBody,
            IdempotencyKey = idempotencyKey.Trim(),
            Status = OutboxStatus.Pending,
            CreatedAt = createdAt.ToUniversalTime(),
            WorkId = workId,
            ResidentVisible = residentVisible
        };
    }

    public MessageChannel Channel { get; private set; }
    public string RecipientAddress { get; private set; } = "";
    public string? Subject { get; private set; }
    public string Body { get; private set; } = "";
    public string IdempotencyKey { get; private set; } = "";
    // The work item this message was sent about, if any, and whether it belongs on the
    // resident-visible half of that work item's history.
    public Guid? WorkId { get; private set; }
    public bool ResidentVisible { get; private set; }
    public OutboxStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? ProviderReference { get; private set; }
    public string? FailureReason { get; private set; }
    public ProviderDeliveryStatus ProviderDeliveryStatus { get; private set; }
    public DateTimeOffset? ProviderUpdatedAt { get; private set; }

    public void ApplyProviderCallback(ProviderDeliveryStatus status, string? failureReason, DateTimeOffset at)
    {
        if (status == ProviderDeliveryStatus.Unknown) throw new ArgumentException("A terminal provider status is required.", nameof(status));
        if (status == ProviderDeliveryStatus.Failed && string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("A failed callback requires a failure reason.", nameof(failureReason));
        var occurredAt = at.ToUniversalTime();
        if (ProviderUpdatedAt is { } prior && prior >= occurredAt) return;
        ProviderDeliveryStatus = status;
        ProviderUpdatedAt = occurredAt;
        if (status == ProviderDeliveryStatus.Failed) FailureReason = failureReason!.Trim();
    }

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
