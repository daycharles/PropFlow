using System.Diagnostics.CodeAnalysis;
using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;

namespace PropFlow.Infrastructure.Communications;

// Thread-safe in-memory record of "delivered" messages. Singleton for the life of the host;
// bounded so a long-running process cannot grow it without limit.
public sealed class InMemorySentMessageLog : ISentMessageLog
{
    private const int Capacity = 500;
    private readonly Lock gate = new();
    private readonly LinkedList<SentMessage> sent = [];
    private readonly Dictionary<string, SentMessage> byKey = new(StringComparer.Ordinal);

    public IReadOnlyList<SentMessage> Sent
    {
        get { lock (gate) { return [.. sent]; } }
    }

    public void Record(SentMessage message)
    {
        lock (gate)
        {
            if (!byKey.TryAdd(message.IdempotencyKey, message)) return;
            sent.AddLast(message);
            if (sent.Count > Capacity)
            {
                var evicted = sent.First!.Value;
                sent.RemoveFirst();
                byKey.Remove(evicted.IdempotencyKey);
            }
        }
    }

    public bool TryGet(string idempotencyKey, [MaybeNullWhen(false)] out SentMessage message)
    {
        lock (gate) { return byKey.TryGetValue(idempotencyKey, out message); }
    }
}

// Records the send instead of contacting a provider. A repeat send for the same idempotency key
// returns the original result without recording a duplicate.
public abstract class MockMessageSender(ISentMessageLog log) : IMessageSender
{
    public abstract MessageChannel Channel { get; }

    public Task<MessageDeliveryResult> SendAsync(OutboundMessage message, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (log.TryGet(idempotencyKey, out var prior))
            return Task.FromResult(MessageDeliveryResult.Delivered(prior.ProviderReference));

        var reference = $"mock-{Channel.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}";
        log.Record(new SentMessage(message.Channel, message.RecipientAddress, message.Subject, message.Body, idempotencyKey, reference));
        // A concurrent first send for the same key may have won the Record race; return whatever
        // is actually in the log so every caller gets a reference that was recorded.
        log.TryGet(idempotencyKey, out var recorded);
        return Task.FromResult(MessageDeliveryResult.Delivered(recorded?.ProviderReference ?? reference));
    }
}

public sealed class MockSmsSender(ISentMessageLog log) : MockMessageSender(log)
{
    public override MessageChannel Channel => MessageChannel.Sms;
}

public sealed class MockEmailSender(ISentMessageLog log) : MockMessageSender(log)
{
    public override MessageChannel Channel => MessageChannel.Email;
}
