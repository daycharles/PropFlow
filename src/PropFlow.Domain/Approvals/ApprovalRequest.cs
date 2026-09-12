namespace PropFlow.Domain.Approvals;

public enum ApprovalStatus { Pending, Approved, Rejected }

// PF-S03.05. A generic, reusable "does someone with authority say yes" primitive for whatever
// Gate 2-5 feature needs one and does not already have a bespoke flow the way Budget
// (Domain.Accounting) already does its own Draft/PendingApproval/Approved/Rejected states.
// This does not replace that, or any other feature's own approval state machine - it exists for
// the next one that has none.
//
// v1 is deliberately one decision, not a chain: SubjectType/SubjectId name whatever business
// object needs a yes/no gate (a free-text, validated string like TimelineEntry.RelatedObjectType,
// not a closed enum - the set of future subjects is not enumerable today the way
// ConfigurationEntityType's is), and Approve/Reject is terminal. A multi-step approval workflow
// (parallel/sequential approvers, thresholds) is a follow-on story if the demand appears, not
// implied by this.
public sealed class ApprovalRequest : TenantEntity
{
    private ApprovalRequest(Guid organizationId, Guid id) : base(organizationId, id) { }

    public static ApprovalRequest Create(Guid organizationId, Guid id, string subjectType, Guid subjectId,
        Guid requestedBy, string? note, DateTimeOffset requestedAt) => new(organizationId, id)
    {
        SubjectType = ValidateSubjectType(subjectType),
        SubjectId = subjectId == Guid.Empty ? throw new ArgumentException("Subject is required.", nameof(subjectId)) : subjectId,
        RequestedBy = requestedBy == Guid.Empty ? throw new ArgumentException("Actor is required.", nameof(requestedBy)) : requestedBy,
        Note = OptionalText(note, nameof(note)),
        Status = ApprovalStatus.Pending,
        RequestedAt = requestedAt.ToUniversalTime(),
    };

    public string SubjectType { get; private set; } = "";
    public Guid SubjectId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public string? Note { get; private set; }
    public ApprovalStatus Status { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionReason { get; private set; }

    public void Approve(Guid actorId, DateTimeOffset at, string? reason = null) => Decide(ApprovalStatus.Approved, actorId, at, reason);
    public void Reject(Guid actorId, DateTimeOffset at, string? reason = null) => Decide(ApprovalStatus.Rejected, actorId, at, reason);

    // Separation of duties: whoever asked cannot be the one who says yes. This is the one
    // invariant an approval gate exists to enforce, so it lives here rather than trusting every
    // caller to remember it.
    private void Decide(ApprovalStatus outcome, Guid actorId, DateTimeOffset at, string? reason)
    {
        if (Status != ApprovalStatus.Pending) throw new InvalidOperationException("Only a pending request can be decided.");
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        if (actorId == RequestedBy) throw new InvalidOperationException("The requester cannot decide their own request.");
        Status = outcome;
        DecidedBy = actorId;
        DecidedAt = at.ToUniversalTime();
        DecisionReason = OptionalText(reason, nameof(reason));
    }

    private static string ValidateSubjectType(string subjectType) =>
        !string.IsNullOrWhiteSpace(subjectType) && subjectType.Trim().Length <= 100
            ? subjectType.Trim() : throw new ArgumentException("Subject type must contain 1 to 100 characters.", nameof(subjectType));

    private static string? OptionalText(string? value, string paramName) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Trim().Length <= 1000 ? value.Trim()
        : throw new ArgumentException("Value must contain at most 1000 characters.", paramName);
}
