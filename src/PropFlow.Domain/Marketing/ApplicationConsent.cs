using PropFlow.Domain.Communications;

namespace PropFlow.Domain.Marketing;

public enum ApplicationConsentType { BackgroundCheck = 0, CreditCheck = 1, EvictionHistory = 2, IncomeVerification = 3 }

// One recorded consent event for one applicant and one check. Append-only: revoking consent
// writes a new row with Decision = Revoked, it never updates the granting row. That is the
// whole point — the row set is the evidence that consent was held at the moment screening ran,
// and an UPDATE would destroy it. PF-S05.02 enforces the same rule at the other two rungs
// (EF GuardWrites, GRANT SELECT/INSERT only and a PostgreSQL trigger).
//
// The decision vocabulary is PropFlow.Domain.Communications.ConsentDecision rather than a
// second enum that means the same thing. Unknown is the absence of a row, so it is refused
// here: a stored row is always an explicit Granted or Revoked.
public sealed class ApplicationConsent : TenantEntity
{
    public const int SourceMaxLength = 200;

    // EF materialization.
    private ApplicationConsent(Guid organizationId, Guid id) : base(organizationId, id) { }

    public ApplicationConsent(
        Guid organizationId,
        Guid id,
        Guid applicationId,
        Guid applicantId,
        ApplicationConsentType consentType,
        ConsentDecision decision,
        DateTimeOffset recordedAt,
        Guid recordedBy,
        string source)
        : base(organizationId, id)
    {
        if (decision is not (ConsentDecision.Granted or ConsentDecision.Revoked))
            throw new ArgumentException("A recorded consent must be granted or revoked.", nameof(decision));
        ApplicationId = ApplicationText.RequireId(applicationId, nameof(applicationId));
        ApplicantId = ApplicationText.RequireId(applicantId, nameof(applicantId));
        ConsentType = consentType;
        Decision = decision;
        RecordedAt = recordedAt.ToUniversalTime();
        RecordedBy = ApplicationText.RequireId(recordedBy, nameof(recordedBy));
        // Where the consent came from: a client IP, "phone — read aloud", a portal session id.
        Source = ApplicationText.RequireSingleLine(source, nameof(source), SourceMaxLength);
    }

    public Guid ApplicationId { get; private set; }
    public Guid ApplicantId { get; private set; }
    public ApplicationConsentType ConsentType { get; private set; }
    public ConsentDecision Decision { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid RecordedBy { get; private set; }
    public string Source { get; private set; } = "";

    public bool AllowsScreening => Decision == ConsentDecision.Granted;

    // Reduces a sequence of consent rows to the effective one per
    // (ApplicationId, ApplicantId, ConsentType): the latest RecordedAt wins, and on an exact
    // tie the row later in the sequence wins so the reduction is deterministic for rows
    // recorded in the same instant. Pure and order-tolerant — callers hand it whatever the
    // query returned without having to sort first.
    public static IReadOnlyDictionary<ApplicationConsentKey, ApplicationConsent> Effective(IEnumerable<ApplicationConsent> consents)
    {
        ArgumentNullException.ThrowIfNull(consents);
        var effective = new Dictionary<ApplicationConsentKey, ApplicationConsent>();
        foreach (var consent in consents)
        {
            var key = new ApplicationConsentKey(consent.ApplicationId, consent.ApplicantId, consent.ConsentType);
            if (!effective.TryGetValue(key, out var current) || consent.RecordedAt >= current.RecordedAt)
                effective[key] = consent;
        }
        return effective;
    }

    // True only on an explicit, still-current grant. No row and a revoked row both block
    // screening, exactly as ChannelConsent.AllowsContact treats Unknown and Revoked alike.
    public static bool Allows(
        IEnumerable<ApplicationConsent> consents,
        Guid applicationId,
        Guid applicantId,
        ApplicationConsentType consentType) =>
        Effective(consents).TryGetValue(new ApplicationConsentKey(applicationId, applicantId, consentType), out var consent)
        && consent.AllowsScreening;
}

public readonly record struct ApplicationConsentKey(Guid ApplicationId, Guid ApplicantId, ApplicationConsentType ConsentType);
