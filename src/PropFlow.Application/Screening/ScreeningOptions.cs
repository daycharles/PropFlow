namespace PropFlow.Application.Screening;

// Bound from the "Screening" configuration section, the CommunicationsOptions idiom.
//
// MaxAttempts is configuration rather than a constant or a request parameter. A constant cannot
// be tuned when a bureau's availability turns out to be worse than assumed, and a per-request
// parameter would let the caller grant itself unlimited retries against a paid third party —
// which is exactly the budget the retry ceiling exists to protect. An operator changes it
// without a redeploy, and no HTTP client can move it.
public sealed class ScreeningOptions
{
    public const string SectionName = "Screening";

    // Attempts before a ScreeningRequest is abandoned. Three matches the outbox's posture of
    // "retry a transient failure a few times, then stop and surface it" rather than retrying
    // into a wall; CommunicationsOptions.MaxDeliveryAttempts is higher (8) because a message
    // costs nothing to re-send and a credit pull does.
    public int MaxAttempts { get; set; } = 3;

    // Which stand-in verdict the configured provider returns. Read by
    // ConfiguredScreeningProvider directly from IConfiguration; declared here so the section's
    // shape is documented in one place.
    public string ProviderMode { get; set; } = "Available";

    public void Validate()
    {
        if (MaxAttempts < 1)
            throw new InvalidOperationException($"{SectionName}:MaxAttempts must be at least 1.");
    }
}
