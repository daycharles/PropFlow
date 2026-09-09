namespace PropFlow.Domain.Communications;

public enum ConsentDecision
{
    Unknown = 0,
    Granted = 1,
    Revoked = 2
}

// Immutable per-channel contact consent for a resident. The resident association and its
// persistence arrive with the resident/occupancy task; the transition rules live here so
// they are exercised independently of the database.
public sealed record ChannelConsent
{
    private ChannelConsent(MessageChannel channel, ConsentDecision decision, DateTimeOffset? decidedAt)
    {
        Channel = channel;
        Decision = decision;
        DecidedAt = decidedAt;
    }

    public MessageChannel Channel { get; }
    public ConsentDecision Decision { get; }
    public DateTimeOffset? DecidedAt { get; }

    // Contact is only permitted on an explicit grant. Unknown and revoked both block sending.
    public bool AllowsContact => Decision == ConsentDecision.Granted;

    public static ChannelConsent Unset(MessageChannel channel) => new(channel, ConsentDecision.Unknown, null);

    public ChannelConsent Grant(DateTimeOffset at) => new(Channel, ConsentDecision.Granted, at.ToUniversalTime());

    public ChannelConsent Revoke(DateTimeOffset at) => new(Channel, ConsentDecision.Revoked, at.ToUniversalTime());
}
