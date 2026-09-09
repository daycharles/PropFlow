namespace PropFlow.Domain.Communications;

// A reusable resident message. The body may contain {{ placeholder }} tokens that the
// application layer renders against work/property/schedule values before dispatch.
public sealed class MessageTemplate : TenantEntity
{
    public MessageTemplate(Guid organizationId, Guid id, string name, MessageChannel channel, string? subject, string body)
        : base(organizationId, id)
    {
        Name = NormalizeName(name);
        Channel = channel;
        (Subject, Body) = NormalizeContent(channel, subject, body);
        IsActive = true;
    }

    public string Name { get; private set; }
    public MessageChannel Channel { get; private set; }
    public string? Subject { get; private set; }
    public string Body { get; private set; }
    public bool IsActive { get; private set; }

    public void Rename(string name) => Name = NormalizeName(name);

    // The channel is fixed for the life of the template; only its content changes.
    public void Revise(string? subject, string body) => (Subject, Body) = NormalizeContent(Channel, subject, body);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static string NormalizeName(string name) => MessageText.RequireSingleLine(name, nameof(name), 200);

    private static (string? Subject, string Body) NormalizeContent(MessageChannel channel, string? subject, string body)
    {
        var normalizedBody = MessageText.RequireBody(body, nameof(body), 2000);

        var trimmed = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        switch (channel)
        {
            case MessageChannel.Email when trimmed is null:
                throw new ArgumentException("Email templates require a subject of 1 to 200 characters.", nameof(subject));
            case MessageChannel.Email:
                trimmed = MessageText.RequireSingleLine(trimmed, nameof(subject), 200);
                break;
            case MessageChannel.Sms when trimmed is not null:
                throw new ArgumentException("SMS templates must not carry a subject.", nameof(subject));
            case MessageChannel.Sms:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown message channel.");
        }

        return (trimmed, normalizedBody);
    }
}
