using PropFlow.Domain.Communications;

namespace PropFlow.Application.Communications;

public sealed record OutboxSubmission(
    MessageChannel Channel,
    string RecipientAddress,
    string? Subject,
    string Body,
    string IdempotencyKey,
    Guid? WorkId = null,
    bool ResidentVisible = false);

public interface IOutbox
{
    // Queues a message in the current tenant context. Submitting the same idempotency key again
    // is a no-op; the return value is true only for the enqueue that created the row.
    Task<bool> EnqueueAsync(OutboxSubmission submission, CancellationToken cancellationToken);
    /// <summary>Queues the whole batch in one transaction after callers have validated it.</summary>
    Task<IReadOnlyList<bool>> EnqueueBatchAsync(IReadOnlyList<OutboxSubmission> submissions, CancellationToken cancellationToken);
}
