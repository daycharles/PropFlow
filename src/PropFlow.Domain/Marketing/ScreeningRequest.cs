namespace PropFlow.Domain.Marketing;

// One screening call to the provider for one applicant, plus its retry state. The retry engine
// is pure domain — modelled on IntegrationConnection.BeginSync/CompleteSync/FailSync — so the
// exhaustion boundary is provable without a container, a provider or an HTTP stack.
//
// IdempotencyKey is what makes a retry safe: PF-S05.02 puts a unique index on
// (OrganizationId, IdempotencyKey), so a second attempt reaches the provider under the same
// key and the provider returns the first answer rather than re-running a credit pull.
// It is the only piece of the provider conversation PropFlow persists besides the verdict.
public sealed class ScreeningRequest : TenantEntity
{
    public const int IdempotencyKeyMaxLength = 200;
    public const int ErrorMaxLength = 1000;

    // EF materialization.
    private ScreeningRequest(Guid organizationId, Guid id) : base(organizationId, id) { }

    public ScreeningRequest(Guid organizationId, Guid id, Guid applicationId, Guid applicantId, string idempotencyKey)
        : base(organizationId, id)
    {
        ApplicationId = ApplicationText.RequireId(applicationId, nameof(applicationId));
        ApplicantId = ApplicationText.RequireId(applicantId, nameof(applicantId));
        IdempotencyKey = ApplicationText.RequireSingleLine(idempotencyKey, nameof(idempotencyKey), IdempotencyKeyMaxLength);
    }

    public Guid ApplicationId { get; private set; }
    public Guid ApplicantId { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
    public ScreeningRequestStatus Status { get; private set; } = ScreeningRequestStatus.Pending;
    public int Attempts { get; private set; }
    public DateTimeOffset? LastAttemptedAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public bool IsTerminal => Status is ScreeningRequestStatus.Completed or ScreeningRequestStatus.Abandoned;

    // Counting the attempt here, before the provider is contacted, is the same at-most-once
    // posture as OutboxProcessor: a crash mid-call must burn an attempt, otherwise a call that
    // reliably kills the process retries forever.
    public void BeginAttempt(DateTimeOffset now)
    {
        RefuseWhenTerminal();
        if (Status == ScreeningRequestStatus.InFlight)
            throw new InvalidOperationException("An attempt is already in flight.");
        Attempts++;
        Status = ScreeningRequestStatus.InFlight;
        LastAttemptedAt = now.ToUniversalTime();
    }

    public void Complete(DateTimeOffset now)
    {
        RefuseWhenTerminal();
        if (Status != ScreeningRequestStatus.InFlight)
            throw new InvalidOperationException("Only an in-flight screening request can be completed.");
        Status = ScreeningRequestStatus.Completed;
        CompletedAt = now.ToUniversalTime();
        LastAttemptedAt = now.ToUniversalTime();
        LastError = null;
    }

    // Back to Pending for another go, unless this attempt was the last one allowed — at which
    // point the request is Abandoned and the application can be returned to ConsentGranted with
    // RentalApplication.AbandonScreening so a fresh request can be raised.
    public void RecordFailure(string error, DateTimeOffset now, int maxAttempts)
    {
        RefuseWhenTerminal();
        if (Status != ScreeningRequestStatus.InFlight)
            throw new InvalidOperationException("Only an in-flight screening request can fail.");
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        LastAttemptedAt = now.ToUniversalTime();
        LastError = ApplicationText.RequireSingleLine(error, nameof(error), ErrorMaxLength);
        Status = Attempts >= maxAttempts ? ScreeningRequestStatus.Abandoned : ScreeningRequestStatus.Pending;
    }

    private void RefuseWhenTerminal()
    {
        if (IsTerminal) throw new InvalidOperationException("Completed and abandoned screening requests are terminal.");
    }
}
