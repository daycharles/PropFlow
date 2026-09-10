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

    // Terminal message payloads are retained for audit/replay visibility, then removed.
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    public string SmsProvider { get; set; } = "Mock";
    public string EmailProvider { get; set; } = "Mock";
    public string? TwilioAccountSid { get; set; }
    public string? TwilioAuthToken { get; set; }
    public string? TwilioFromNumber { get; set; }
    public string? SendGridApiKey { get; set; }
    public string? SendGridFromAddress { get; set; }

    public void Validate(bool productionOrStaging)
    {
        ValidateProvider(SmsProvider, "SMS");
        ValidateProvider(EmailProvider, "email");
        if (SmsProvider.Equals("Twilio", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(TwilioAccountSid) || string.IsNullOrWhiteSpace(TwilioAuthToken) || string.IsNullOrWhiteSpace(TwilioFromNumber)))
            throw new InvalidOperationException("Twilio SMS requires account SID, auth token, and from number.");
        if (EmailProvider.Equals("SendGrid", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(SendGridApiKey) || string.IsNullOrWhiteSpace(SendGridFromAddress)))
            throw new InvalidOperationException("SendGrid email requires API key and from address.");
    }

    private static void ValidateProvider(string provider, string channel)
    {
        if (provider is "Mock" or "Twilio" or "SendGrid") return;
        throw new InvalidOperationException($"Unsupported {channel} provider '{provider}'.");
    }
}
