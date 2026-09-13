using PropFlow.Domain;

namespace PropFlow.Domain.Communications;

public enum ConversationStatus { Open, Closed }

public sealed class Conversation(Guid organizationId, Guid id, string subject, string participantAddress)
    : TenantEntity(organizationId, id)
{
    public string Subject { get; private set; } = Required(subject, nameof(subject), 200);
    public string ParticipantAddress { get; private set; } = Required(participantAddress, nameof(participantAddress), 320);
    public ConversationStatus Status { get; private set; } = ConversationStatus.Open;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastMessageAt { get; private set; }

    public void Touch(DateTimeOffset at) => LastMessageAt = at.ToUniversalTime();
    public void Close() => Status = ConversationStatus.Closed;
    public void Reopen() => Status = ConversationStatus.Open;

    private static string Required(string value, string name, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}

public enum ConversationMessageDirection { Inbound, Outbound }

public sealed class ConversationMessage(Guid organizationId, Guid id, Guid conversationId,
    ConversationMessageDirection direction, MessageChannel channel, string body, string? actorId,
    DateTimeOffset occurredAt) : TenantEntity(organizationId, id)
{
    public Guid ConversationId { get; private set; } = conversationId == Guid.Empty ? throw new ArgumentException("Conversation is required.", nameof(conversationId)) : conversationId;
    public ConversationMessageDirection Direction { get; private set; } = direction;
    public MessageChannel Channel { get; private set; } = channel;
    public string Body { get; private set; } = Required(body, nameof(body), 10000);
    public string? ActorId { get; private set; } = string.IsNullOrWhiteSpace(actorId) ? null : actorId.Trim();
    public DateTimeOffset OccurredAt { get; private set; } = occurredAt.ToUniversalTime();
    public string IntegrityHash { get; private set; } = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body)));

    private static string Required(string value, string name, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}

public sealed class Campaign(Guid organizationId, Guid id, string name, MessageChannel channel, string subject,
    string body, DateTimeOffset? scheduledAt) : TenantEntity(organizationId, id)
{
    public string Name { get; private set; } = Required(name, nameof(name), 200);
    public MessageChannel Channel { get; private set; } = channel;
    public string? Subject { get; private set; } = channel == MessageChannel.Sms ? null : Required(subject, nameof(subject), 200);
    public string Body { get; private set; } = Required(body, nameof(body), 10000);
    public DateTimeOffset? ScheduledAt { get; private set; } = scheduledAt?.ToUniversalTime();
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsPublished { get; private set; }
    public void Publish() => IsPublished = true;
    public void Unpublish() => IsPublished = false;
    private static string Required(string value, string name, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}

public sealed class ChannelUnsubscribe(Guid organizationId, Guid id, MessageChannel channel, string address,
    DateTimeOffset unsubscribedAt) : TenantEntity(organizationId, id)
{
    public MessageChannel Channel { get; private set; } = channel;
    public string Address { get; private set; } = address.Trim();
    public DateTimeOffset UnsubscribedAt { get; private set; } = unsubscribedAt.ToUniversalTime();
}
