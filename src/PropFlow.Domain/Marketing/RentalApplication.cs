namespace PropFlow.Domain.Marketing;

public enum ApplicationStatus
{
    Draft = 0,
    Submitted = 1,
    ConsentGranted = 2,
    Screening = 3,
    UnderReview = 4,
    Approved = 5,
    Denied = 6,
    Withdrawn = 7
}

// One rental application against one listing. A new aggregate rather than more state on
// Applicant: Applicant is unique per inquiry (OperationsStore.cs), so application state there
// would permanently block joint applications and re-applications by the same person.
//
//   Draft --Submit--> Submitted --MarkConsented--> ConsentGranted --BeginScreening--> Screening
//                          ^                                                              |
//                   AbandonScreening <--------------------------------------------        |
//                                                                                CompleteScreening
//                          UnderReview --Approve--> Approved   (terminal)                  |
//                                      --Deny-----> Denied     (terminal)  <---------------
//        any non-terminal --Withdraw--> Withdrawn (terminal)
//
// There is deliberately no Reopen, unlike WorkItem: Approved, Denied and Withdrawn are final
// and a re-application is a new RentalApplication row, so the decision trail for an application
// can never be rewritten by resurrecting it.
public sealed class RentalApplication : TenantEntity
{
    // EF materialization.
    private RentalApplication(Guid organizationId, Guid id) : base(organizationId, id) { }

    public RentalApplication(Guid organizationId, Guid id, Guid listingId)
        : base(organizationId, id)
    {
        ListingId = ApplicationText.RequireId(listingId, nameof(listingId));
    }

    public Guid ListingId { get; private set; }
    public ApplicationStatus Status { get; private set; } = ApplicationStatus.Draft;
    public DateTimeOffset? SubmittedAt { get; private set; }
    // When the application reached a terminal state. Approve and Deny set it alongside the
    // ApplicationDecision row they write; Withdraw sets it with no decision row, because a
    // withdrawal is the applicant walking away rather than a decision by the organization.
    public DateTimeOffset? DecidedAt { get; private set; }

    private readonly List<ApplicationApplicant> _applicants = [];
    public IReadOnlyList<ApplicationApplicant> Applicants => _applicants;

    public bool IsTerminal => Status is ApplicationStatus.Approved or ApplicationStatus.Denied or ApplicationStatus.Withdrawn;

    public ApplicationApplicant? Primary => _applicants.SingleOrDefault(a => a.Role == ApplicantRole.Primary);

    // Exactly one Primary, enforced here rather than in the endpoint. Ordering is part of the
    // invariant: a co-applicant or guarantor cannot be added before the Primary exists, so
    // "exactly one Primary" holds from the first row onwards rather than only at submit time.
    public ApplicationApplicant AddApplicant(
        Guid id,
        Guid applicantId,
        ApplicantRole role,
        decimal? monthlyIncome = null,
        string? employmentStatus = null)
    {
        RefuseWhenTerminal();
        if (_applicants.Any(a => a.ApplicantId == applicantId))
            throw new InvalidOperationException("That applicant is already on this application.");
        if (role == ApplicantRole.Primary && Primary is not null)
            throw new InvalidOperationException("The application already has a primary applicant.");
        if (role != ApplicantRole.Primary && Primary is null)
            throw new InvalidOperationException("The primary applicant must be added first.");
        var applicant = new ApplicationApplicant(OrganizationId, id, Id, applicantId, role, monthlyIncome, employmentStatus);
        _applicants.Add(applicant);
        return applicant;
    }

    public void RemoveApplicant(Guid applicantId)
    {
        RefuseWhenTerminal();
        var applicant = _applicants.SingleOrDefault(a => a.ApplicantId == applicantId)
            ?? throw new InvalidOperationException("That applicant is not on this application.");
        if (applicant.Role == ApplicantRole.Primary && _applicants.Count > 1)
            throw new InvalidOperationException("The primary applicant cannot be removed while others remain.");
        _applicants.Remove(applicant);
    }

    // An application with nobody on it has nobody to consent, screen or decide about, so the
    // primary applicant is a precondition of submitting rather than a check at screening time.
    public void Submit(Guid actorId, DateTimeOffset now)
    {
        RequireActor(actorId);
        RequireStatus(ApplicationStatus.Draft, "Only a draft application can be submitted.");
        if (Primary is null) throw new InvalidOperationException("A submitted application needs a primary applicant.");
        Status = ApplicationStatus.Submitted;
        SubmittedAt = now.ToUniversalTime();
    }

    // The four mid-pipeline transitions take the actor but no clock: the aggregate records no
    // timestamp for them, and a parameter that is accepted and dropped reads as a column that
    // exists somewhere. The ApplicationConsent rows and the ScreeningRequest carry the times.
    public void MarkConsented(Guid actorId)
    {
        RequireActor(actorId);
        RequireStatus(ApplicationStatus.Submitted, "Consent can only be recorded on a submitted application.");
        Status = ApplicationStatus.ConsentGranted;
    }

    // The consent gate, and the load-bearing invariant of this aggregate. It lives in the
    // domain precisely so it is asserted without a database and so no endpoint, background job
    // or future caller can reach the provider with an applicant who has not consented.
    public void BeginScreening(Guid actorId)
    {
        RequireActor(actorId);
        RequireStatus(ApplicationStatus.ConsentGranted, "Screening requires recorded consent.");
        Status = ApplicationStatus.Screening;
    }

    public void CompleteScreening(Guid actorId)
    {
        RequireActor(actorId);
        RequireStatus(ApplicationStatus.Screening, "Only an application in screening can complete screening.");
        Status = ApplicationStatus.UnderReview;
    }

    // The retry engine exhausted its attempts (ScreeningRequest is now Abandoned). Back to
    // ConsentGranted, from which a fresh ScreeningRequest can be raised — consent is not
    // re-collected, because it was never revoked.
    public void AbandonScreening(Guid actorId)
    {
        RequireActor(actorId);
        RequireStatus(ApplicationStatus.Screening, "Only an application in screening can abandon screening.");
        Status = ApplicationStatus.ConsentGranted;
    }

    // The resolved FS-S05 open question. A Fail recommendation does not hard-block approval —
    // a hard block makes individualized assessment impossible — but approving over one is never
    // silent: the override note is required, and the refusal is a domain refusal so PF-S05.07's
    // endpoint cannot be the only thing standing between a Fail and a silent approval.
    public ApplicationDecision Approve(
        Guid actorId,
        DateTimeOffset now,
        string reason,
        string? note,
        IReadOnlyCollection<ScreeningRecommendation> screeningRecommendations)
    {
        ArgumentNullException.ThrowIfNull(screeningRecommendations);
        RequireActor(actorId);
        RequireStatus(ApplicationStatus.UnderReview, "Only an application under review can be decided.");
        if (screeningRecommendations.Contains(ScreeningRecommendation.Fail) && string.IsNullOrWhiteSpace(note))
            throw new InvalidOperationException("Approving over a failed screening requires an override note.");
        return Decide(ApplicationDecisionOutcome.Approved, ApplicationStatus.Approved, actorId, now, reason, note);
    }

    public ApplicationDecision Deny(Guid actorId, DateTimeOffset now, string reason, string? note = null)
    {
        RequireActor(actorId);
        RequireStatus(ApplicationStatus.UnderReview, "Only an application under review can be decided.");
        return Decide(ApplicationDecisionOutcome.Denied, ApplicationStatus.Denied, actorId, now, reason, note);
    }

    public void Withdraw(Guid actorId, DateTimeOffset now)
    {
        RequireActor(actorId);
        RefuseWhenTerminal();
        Status = ApplicationStatus.Withdrawn;
        DecidedAt = now.ToUniversalTime();
    }

    private ApplicationDecision Decide(
        ApplicationDecisionOutcome outcome,
        ApplicationStatus status,
        Guid actorId,
        DateTimeOffset now,
        string reason,
        string? note)
    {
        var decision = new ApplicationDecision(OrganizationId, Guid.NewGuid(), Id, outcome, reason, note, actorId, now);
        Status = status;
        DecidedAt = decision.DecidedAt;
        return decision;
    }

    private void RequireStatus(ApplicationStatus expected, string message)
    {
        RefuseWhenTerminal();
        if (Status != expected) throw new InvalidOperationException(message);
    }

    private void RefuseWhenTerminal()
    {
        if (IsTerminal) throw new InvalidOperationException("Approved, denied and withdrawn applications are terminal.");
    }

    private static void RequireActor(Guid actorId)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
    }
}
