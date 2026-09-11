namespace PropFlow.Domain.Properties;

public enum IncidentStatus { Open = 1, Investigating = 2, Remediated = 3, Closed = 4 }

public sealed class Incident : TenantEntity
{
    private Incident(Guid organizationId, Guid id) : base(organizationId, id) { }

    public Incident(Guid organizationId, Guid id, Guid propertyId, string title, DateTimeOffset occurredAt)
        : base(organizationId, id)
    {
        if (propertyId == Guid.Empty) throw new ArgumentException("Property is required.", nameof(propertyId));
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200) throw new ArgumentException("Title must contain 1 to 200 characters.", nameof(title));
        PropertyId = propertyId; Title = title.Trim(); OccurredAt = occurredAt.ToUniversalTime();
    }

    public Guid PropertyId { get; private set; }
    public string Title { get; private set; } = "";
    public DateTimeOffset OccurredAt { get; private set; }
    public IncidentStatus Status { get; private set; } = IncidentStatus.Open;
    public DateTimeOffset? ClosedAt { get; private set; }
    private readonly List<IncidentAuditEntry> _audit = [];
    public IReadOnlyList<IncidentAuditEntry> Audit => _audit;

    public void BeginInvestigation() => BeginInvestigation(Guid.Empty, DateTimeOffset.UtcNow);
    public void BeginInvestigation(Guid actorId, DateTimeOffset at, string? note = null)
    { Transition(IncidentStatus.Open, IncidentStatus.Investigating, IncidentAuditAction.InvestigationStarted, actorId, at, note); }
    public void MarkRemediated() => MarkRemediated(Guid.Empty, DateTimeOffset.UtcNow);
    public void MarkRemediated(Guid actorId, DateTimeOffset at, string? note = null)
    { if (Status is not (IncidentStatus.Investigating or IncidentStatus.Open)) throw new InvalidOperationException("Incident must be open or under investigation."); Transition(Status, IncidentStatus.Remediated, IncidentAuditAction.Remediated, actorId, at, note); }
    public void Close(DateTimeOffset closedAt) => Close(Guid.Empty, closedAt);
    public void Close(Guid actorId, DateTimeOffset closedAt, string? note = null)
    { Transition(IncidentStatus.Remediated, IncidentStatus.Closed, IncidentAuditAction.Closed, actorId, closedAt, note); ClosedAt = closedAt.ToUniversalTime(); }
    private void Transition(IncidentStatus from, IncidentStatus to, IncidentAuditAction action, Guid actorId, DateTimeOffset at, string? note)
    { Require(from); Status = to; _audit.Add(new(action, from, to, actorId, at.ToUniversalTime(), note)); }
    private void Require(IncidentStatus expected) { if (Status != expected) throw new InvalidOperationException($"Incident must be {expected}."); }
}
