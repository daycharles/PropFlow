using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;

namespace PropFlow.Infrastructure.Communications;

// Delivers outbox messages for one tenant context. Each message is claimed under the
// concurrency token before the provider is contacted, so competing dispatchers deliver it at
// most once; a message left Sending by a crashed worker is reclaimed after StaleClaimTimeout.
// Delivery failures keep the message retryable until OutboxMessage.MaxDeliveryAttempts.
public sealed class OutboxProcessor(CommunicationsStore store, IEnumerable<IMessageSender> senders, TimeProvider clock)
{
    private const int BatchSize = 50;
    private static readonly TimeSpan StaleClaimTimeout = TimeSpan.FromMinutes(5);

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var cutoff = now - StaleClaimTimeout;
        var candidates = await store.OutboxMessages
            .Where(x => x.Status == OutboxStatus.Pending
                || (x.Status == OutboxStatus.Sending && x.LastAttemptAt != null && x.LastAttemptAt <= cutoff))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var delivered = 0;
        foreach (var message in candidates)
        {
            if (!message.IsClaimable(now, StaleClaimTimeout))
                continue;

            message.BeginDelivery(clock.GetUtcNow());
            try
            {
                await store.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another dispatcher claimed this message first.
                store.Entry(message).State = EntityState.Detached;
                continue;
            }

            await DeliverAsync(message, cancellationToken);

            try
            {
                await store.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                store.Entry(message).State = EntityState.Detached;
                continue;
            }

            if (message.Status == OutboxStatus.Sent) delivered++;
        }

        return delivered;
    }

    private async Task DeliverAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var sender = senders.FirstOrDefault(x => x.Channel == message.Channel);
            if (sender is null)
            {
                message.RecordFailedAttempt($"No sender registered for channel {message.Channel}.", clock.GetUtcNow());
                return;
            }

            var outbound = new OutboundMessage(message.Channel, message.RecipientAddress, message.Subject, message.Body);
            var result = await sender.SendAsync(outbound, message.IdempotencyKey, cancellationToken);
            if (result.Status == MessageDeliveryStatus.Sent)
                message.MarkSent(result.ProviderReference ?? "unknown", clock.GetUtcNow());
            else
                message.RecordFailedAttempt(result.FailureReason ?? "unknown", clock.GetUtcNow());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            message.RecordFailedAttempt(exception.Message, clock.GetUtcNow());
        }
    }
}
