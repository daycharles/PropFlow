using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;

namespace PropFlow.Infrastructure.Communications;

// Delivers pending outbox messages for one tenant context. Each message is saved individually
// so a mid-batch failure keeps the completed work. A message that succeeds at the provider but
// fails to save stays pending and is retried; the sender collapses the duplicate by key.
public sealed class OutboxProcessor(CommunicationsStore store, IEnumerable<IMessageSender> senders, TimeProvider clock)
{
    private const int BatchSize = 50;

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var pending = await store.OutboxMessages
            .Where(x => x.Status == OutboxStatus.Pending)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var message in pending)
        {
            var sender = senders.FirstOrDefault(x => x.Channel == message.Channel);
            if (sender is null)
            {
                message.MarkFailed($"No sender registered for channel {message.Channel}.", clock.GetUtcNow());
            }
            else
            {
                var outbound = new OutboundMessage(message.Channel, message.RecipientAddress, message.Subject, message.Body);
                var result = await sender.SendAsync(outbound, message.IdempotencyKey, cancellationToken);
                if (result.Status == MessageDeliveryStatus.Sent)
                    message.MarkSent(result.ProviderReference ?? "unknown", clock.GetUtcNow());
                else
                    message.MarkFailed(result.FailureReason ?? "unknown", clock.GetUtcNow());
            }

            await store.SaveChangesAsync(cancellationToken);
            processed++;
        }

        return processed;
    }
}
