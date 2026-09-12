namespace PropFlow.Domain.Marketing;

public enum ApplicationDecisionOutcome { Approved = 0, Denied = 1 }

// The audit row for an approve or a deny. Append-only and created only by
// RentalApplication.Approve / RentalApplication.Deny, so the override rule below cannot be
// bypassed by constructing a decision directly.
//
// A denial is adverse-action evidence, which is why Reason is a required code rather than free
// text and why nothing here is mutable.
public sealed class ApplicationDecision : TenantEntity
{
    public const int ReasonMaxLength = 200;
    public const int NoteMaxLength = 4000;

    // EF materialization.
    private ApplicationDecision(Guid organizationId, Guid id) : base(organizationId, id) { }

    internal ApplicationDecision(
        Guid organizationId,
        Guid id,
        Guid applicationId,
        ApplicationDecisionOutcome outcome,
        string reason,
        string? note,
        Guid decidedBy,
        DateTimeOffset decidedAt)
        : base(organizationId, id)
    {
        ApplicationId = ApplicationText.RequireId(applicationId, nameof(applicationId));
        Outcome = outcome;
        Reason = ApplicationText.RequireSingleLine(reason, nameof(reason), ReasonMaxLength);
        Note = ApplicationText.OptionalText(note, nameof(note), NoteMaxLength);
        DecidedBy = ApplicationText.RequireId(decidedBy, nameof(decidedBy));
        DecidedAt = decidedAt.ToUniversalTime();
    }

    public Guid ApplicationId { get; private set; }
    public ApplicationDecisionOutcome Outcome { get; private set; }
    public string Reason { get; private set; } = "";
    public string? Note { get; private set; }
    public Guid DecidedBy { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }
}
