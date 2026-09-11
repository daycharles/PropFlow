namespace PropFlow.Domain.Properties;

public enum IncidentAuditAction { Created = 1, InvestigationStarted = 2, Remediated = 3, Closed = 4 }

public sealed record IncidentAuditEntry(
    IncidentAuditAction Action,
    IncidentStatus FromStatus,
    IncidentStatus ToStatus,
    Guid ActorId,
    DateTimeOffset OccurredAt,
    string? Note);
