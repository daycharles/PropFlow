using PropFlow.Domain.Properties;

namespace PropFlow.Application.Compliance;

public sealed record ComplianceOccurrence(Guid SourceObligationId, DateOnly DueOn, string IdempotencyKey);

public static class ComplianceRecurrenceGenerator
{
    public static IReadOnlyList<ComplianceOccurrence> DueThrough(ComplianceObligation obligation, DateOnly through)
    {
        var results = new List<ComplianceOccurrence>();
        var due = obligation.DueOn;
        while (due <= through)
        {
            results.Add(new(obligation.Id, due, ComplianceObligation.OccurrenceKey(obligation.Id, due)));
            if (obligation.Recurrence == ComplianceRecurrence.None)
                break;

            due = obligation.Advance(due);
            if (obligation.RecurrenceEndOn is { } end && due > end) break;
        }
        return results;
    }
}
