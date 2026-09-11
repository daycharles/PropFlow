using PropFlow.Domain.Properties;

namespace PropFlow.Application.Compliance;

public static class ComplianceRiskCalculator
{
    public static RiskDashboard Calculate(
        IEnumerable<ComplianceObligation> obligations,
        IEnumerable<Incident> incidents,
        IEnumerable<Violation> violations,
        IEnumerable<Remediation> remediations,
        DateOnly asOf)
    {
        var obligationList = obligations.ToList();
        return new(
            obligationList.Count(obligation => obligation.Status == ComplianceObligationStatus.Active),
            obligationList.Count(obligation => obligation.IsOverdue(asOf)),
            obligationList.Count(obligation => obligation.IsEscalated(asOf)),
            incidents.Count(incident => incident.Status != IncidentStatus.Closed),
            violations.Count(violation => violation.Status is ViolationStatus.Open or ViolationStatus.Remediating),
            remediations.Count(remediation => remediation.IsOverdue(asOf)));
    }
}
