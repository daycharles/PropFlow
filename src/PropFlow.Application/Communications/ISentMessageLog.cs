using System.Diagnostics.CodeAnalysis;
using PropFlow.Domain.Communications;

namespace PropFlow.Application.Communications;

public sealed record SentMessage(
    MessageChannel Channel,
    string RecipientAddress,
    string? Subject,
    string Body,
    string IdempotencyKey,
    string ProviderReference);

// Records what the mock providers "delivered" so tests and a future in-app activity view can
// inspect it. Deduplicates by idempotency key so a retried dispatch never records a second send.
public interface ISentMessageLog
{
    IReadOnlyList<SentMessage> Sent { get; }

    void Record(SentMessage message);

    bool TryGet(string idempotencyKey, [MaybeNullWhen(false)] out SentMessage message);
}
