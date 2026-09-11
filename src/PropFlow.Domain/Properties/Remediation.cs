namespace PropFlow.Domain.Properties;

public enum RemediationStatus { Planned = 1, InProgress = 2, Completed = 3, Cancelled = 4 }

public sealed class Remediation : TenantEntity
{
    public Remediation(Guid organizationId, Guid id, Guid propertyId, Guid? violationId, Guid? incidentId, string action, DateOnly dueOn)
        : base(organizationId, id)
    {
        if (propertyId == Guid.Empty) throw new ArgumentException("Property is required.", nameof(propertyId));
        if (violationId is null && incidentId is null) throw new ArgumentException("A violation or incident is required.");
        if (string.IsNullOrWhiteSpace(action) || action.Trim().Length > 500) throw new ArgumentException("Action must contain 1 to 500 characters.", nameof(action));
        PropertyId = propertyId; ViolationId = violationId; IncidentId = incidentId; Action = action.Trim(); DueOn = dueOn;
    }
    public Guid PropertyId { get; private set; }
    public Guid? ViolationId { get; private set; }
    public Guid? IncidentId { get; private set; }
    public string Action { get; private set; }
    public DateOnly DueOn { get; private set; }
    public RemediationStatus Status { get; private set; } = RemediationStatus.Planned;
    public DateOnly? CompletedOn { get; private set; }
    public bool IsOverdue(DateOnly asOf) => Status is not (RemediationStatus.Completed or RemediationStatus.Cancelled) && asOf > DueOn;
    public void Start() { if (Status != RemediationStatus.Planned) throw new InvalidOperationException("Remediation is not planned."); Status = RemediationStatus.InProgress; }
    public void Complete(DateOnly on) { if (Status is RemediationStatus.Completed or RemediationStatus.Cancelled) throw new InvalidOperationException("Remediation is final."); Status = RemediationStatus.Completed; CompletedOn = on; }
    public void Cancel() { if (Status == RemediationStatus.Completed) throw new InvalidOperationException("Completed remediation cannot be cancelled."); Status = RemediationStatus.Cancelled; }
}
