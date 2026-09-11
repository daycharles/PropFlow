namespace PropFlow.Domain.Properties;

public sealed record EvidenceRetentionPolicy
{
    public int RetentionDays { get; }
    public bool LegalHoldOverridesExpiry { get; }
    public EvidenceRetentionPolicy(int retentionDays, bool legalHoldOverridesExpiry = true)
    { RetentionDays = Validate(retentionDays); LegalHoldOverridesExpiry = legalHoldOverridesExpiry; }
    public DateTimeOffset? RetainUntil(DateTimeOffset createdAt, bool legalHold)
        => legalHold && LegalHoldOverridesExpiry ? null : createdAt.ToUniversalTime().AddDays(RetentionDays);
    private static int Validate(int days) => days is < 1 or > 36500 ? throw new ArgumentOutOfRangeException(nameof(days)) : days;
}

public sealed record EvidenceLink(Guid AttachmentId, Guid? IncidentId, Guid? ViolationId, DateTimeOffset CreatedAt, DateTimeOffset? RetainUntil, bool LegalHold)
{
    public bool CanDelete(DateTimeOffset asOf) => !LegalHold && RetainUntil is { } expiry && asOf >= expiry;
}
