using PropFlow.Application.Compliance;
using PropFlow.Domain.Properties;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class ComplianceRiskTests
{
    [Fact]
    public void Recurring_obligations_produce_stable_occurrences_through_cutoff()
    {
        var obligation = new ComplianceObligation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Annual gas inspection", new(2026, 1, 31), 10, ComplianceRecurrence.Monthly, new(2026, 4, 30));
        var occurrences = ComplianceRecurrenceGenerator.DueThrough(obligation, new(2026, 6, 1));

        Assert.Equal(4, occurrences.Count);
        Assert.Equal(new DateOnly(2026, 2, 28), occurrences[1].DueOn);
        Assert.Equal(ComplianceObligation.OccurrenceKey(obligation.Id, new(2026, 4, 30)), occurrences[3].IdempotencyKey);
        Assert.Equal(occurrences, ComplianceRecurrenceGenerator.DueThrough(obligation, new(2026, 6, 1)));
    }

    [Fact]
    public void Non_recurring_obligation_with_max_date_cutoff_returns_once()
    {
        var obligation = new ComplianceObligation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "One-time inspection", DateOnly.MaxValue, 0);

        var occurrences = ComplianceRecurrenceGenerator.DueThrough(obligation, DateOnly.MaxValue);

        var occurrence = Assert.Single(occurrences);
        Assert.Equal(DateOnly.MaxValue, occurrence.DueOn);
    }

    [Fact]
    public void Incident_transitions_are_append_only_and_audited()
    {
        var actor = Guid.NewGuid();
        var incident = new Incident(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Water leak", DateTimeOffset.UtcNow);
        incident.BeginInvestigation(actor, DateTimeOffset.Parse("2026-01-02T01:00:00Z"), "Triage started");
        incident.MarkRemediated(actor, DateTimeOffset.Parse("2026-01-03T01:00:00Z"));
        incident.Close(actor, DateTimeOffset.Parse("2026-01-04T01:00:00Z"));

        Assert.Equal(3, incident.Audit.Count);
        Assert.Equal(IncidentAuditAction.InvestigationStarted, incident.Audit[0].Action);
        Assert.All(incident.Audit, entry => Assert.Equal(actor, entry.ActorId));
        Assert.Equal(IncidentStatus.Closed, incident.Audit[^1].ToStatus);
    }

    [Fact]
    public void Violation_remediation_and_overdue_state_follow_lifecycle()
    {
        var violation = new Violation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Missing extinguisher", new(2026, 1, 1), 4);
        var remediation = new Remediation(violation.OrganizationId, Guid.NewGuid(), violation.PropertyId, violation.Id, null, "Install replacement", new(2026, 1, 10));

        Assert.True(remediation.IsOverdue(new(2026, 1, 11)));
        remediation.Start();
        violation.StartRemediation();
        remediation.Complete(new(2026, 1, 12));
        violation.Resolve(new(2026, 1, 12));
        Assert.False(remediation.IsOverdue(new(2026, 2, 1)));
        Assert.Equal(ViolationStatus.Resolved, violation.Status);
    }

    [Fact]
    public void Evidence_retention_respects_legal_hold_and_expiry()
    {
        var policy = new EvidenceRetentionPolicy(30);
        var created = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        Assert.Equal(created.AddDays(30), policy.RetainUntil(created, false));
        Assert.Null(policy.RetainUntil(created, true));
        Assert.False(new EvidenceLink(Guid.NewGuid(), null, null, created, created.AddDays(30), true).CanDelete(created.AddDays(31)));
        Assert.True(new EvidenceLink(Guid.NewGuid(), null, null, created, created.AddDays(30), false).CanDelete(created.AddDays(30)));
    }

    [Fact]
    public void Risk_dashboard_counts_only_active_and_open_risks()
    {
        var organizationId = Guid.NewGuid();
        var obligation = new ComplianceObligation(organizationId, Guid.NewGuid(), Guid.NewGuid(), "Inspection", new(2026, 1, 1), 5);
        var incident = new Incident(organizationId, Guid.NewGuid(), Guid.NewGuid(), "Leak", DateTimeOffset.UtcNow);
        var violation = new Violation(organizationId, Guid.NewGuid(), Guid.NewGuid(), "Missing sign", new(2026, 1, 1), 2);
        var remediation = new Remediation(organizationId, Guid.NewGuid(), violation.PropertyId, violation.Id, null, "Install sign", new(2026, 1, 5));

        var dashboard = ComplianceRiskCalculator.Calculate([obligation], [incident], [violation], [remediation], new(2026, 1, 10));

        Assert.Equal(new RiskDashboard(1, 1, 1, 1, 1, 1), dashboard);
    }
}
