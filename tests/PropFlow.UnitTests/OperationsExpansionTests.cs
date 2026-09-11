using PropFlow.Domain.Assets;
using PropFlow.Domain.Properties;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class OperationsExpansionTests
{
    [Fact]
    public void Preventive_plan_occurrences_are_deterministic_and_keyed()
    {
        var plan = new PreventiveMaintenancePlan(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Boiler service", MaintenanceRecurrence.Quarterly, new DateOnly(2026, 1, 31));
        Assert.Equal(new DateOnly(2026, 4, 30), plan.DueOn(1));
        Assert.Equal(2, plan.OccurrenceFor(new DateOnly(2026, 7, 31)));
        Assert.Equal($"pm:{plan.Id:N}:2", PreventiveMaintenancePlan.OccurrenceKey(plan.Id, 2));
    }

    [Fact]
    public void Compliance_obligation_escalates_only_while_active()
    {
        var obligation = new ComplianceObligation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Fire inspection", new DateOnly(2026, 1, 10), 5);
        Assert.True(obligation.IsOverdue(new DateOnly(2026, 1, 11)));
        Assert.True(obligation.IsEscalated(new DateOnly(2026, 1, 16)));
        obligation.Satisfy(new DateOnly(2026, 1, 16));
        Assert.False(obligation.IsOverdue(new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void Incident_requires_remediation_before_close()
    {
        var incident = new Incident(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Water leak", DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => incident.Close(DateTimeOffset.UtcNow));
        incident.BeginInvestigation();
        incident.MarkRemediated();
        incident.Close(DateTimeOffset.UtcNow);
        Assert.Equal(IncidentStatus.Closed, incident.Status);
    }
}
