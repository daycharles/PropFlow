using PropFlow.Domain.Properties;

namespace PropFlow.Application.Compliance;

public sealed record ComplianceObligationQuery(Guid? PropertyId, ComplianceObligationStatus? Status, bool? OverdueOnly, DateOnly AsOf, int Page = 1, int PageSize = 50);
public sealed record ComplianceObligationPage(IReadOnlyList<ComplianceObligation> Items, int TotalCount, int Page, int PageSize);
public sealed record RiskDashboard(int ActiveObligations, int OverdueObligations, int EscalatedObligations, int OpenIncidents, int OpenViolations, int OverdueRemediations);
public sealed record ComplianceEvidenceCommand(Guid AttachmentId, Guid? IncidentId, Guid? ViolationId, DateTimeOffset CreatedAt, bool LegalHold);

public interface IComplianceOperations
{
    Task<ComplianceObligationPage> ListObligationsAsync(ComplianceObligationQuery query, CancellationToken cancellationToken);
    Task<ComplianceObligation> CreateObligationAsync(ComplianceObligation obligation, CancellationToken cancellationToken);
    Task<ComplianceObligation?> GetObligationAsync(Guid id, CancellationToken cancellationToken);
    Task<Incident?> GetIncidentAsync(Guid id, CancellationToken cancellationToken);
    Task<Incident?> TransitionIncidentAsync(Guid id, IncidentAuditAction action, Guid actorId, DateTimeOffset occurredAt, string? note, CancellationToken cancellationToken);
    Task<Violation?> GetViolationAsync(Guid id, CancellationToken cancellationToken);
    Task<Remediation?> GetRemediationAsync(Guid id, CancellationToken cancellationToken);
    Task<EvidenceLink?> AddEvidenceAsync(ComplianceEvidenceCommand command, CancellationToken cancellationToken);
}

public interface IComplianceRiskReader
{
    Task<RiskDashboard> GetDashboardAsync(DateOnly asOf, CancellationToken cancellationToken);
}
