namespace PropFlow.Application.Communications;

// Bound from the "Communications" configuration section. Defaults suit a single-instance MVP
// with mock providers; operators tune these without a redeploy.
public sealed class CommunicationsOptions
{
    public const string SectionName = "Communications";

    // How often the dispatcher polls the outbox.
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);

    // Minimum wait between delivery attempts for the same message.
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(2);

    // Attempts before a message is abandoned as Failed.
    public int MaxDeliveryAttempts { get; set; } = 8;

    // How long a claimed (Sending) message may sit before another dispatcher reclaims it.
    public TimeSpan StaleClaimTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
