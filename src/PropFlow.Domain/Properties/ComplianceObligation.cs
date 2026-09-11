namespace PropFlow.Domain.Properties;

public enum ComplianceObligationStatus { Active = 1, Satisfied = 2, Waived = 3 }
public enum ComplianceRecurrence { None = 0, Monthly = 1, Quarterly = 2, Yearly = 3 }

public sealed class ComplianceObligation : TenantEntity
{
    public ComplianceObligation(Guid organizationId, Guid id, Guid propertyId, string title,
        DateOnly dueOn, int escalationDays, ComplianceRecurrence recurrence = ComplianceRecurrence.None,
        DateOnly? recurrenceEndOn = null)
        : base(organizationId, id)
    {
        if (propertyId == Guid.Empty) throw new ArgumentException("Property is required.", nameof(propertyId));
        if (escalationDays < 0 || escalationDays > 3650) throw new ArgumentOutOfRangeException(nameof(escalationDays));
        if (recurrenceEndOn is { } end && end < dueOn) throw new ArgumentException("Recurrence end must be on or after the due date.", nameof(recurrenceEndOn));
        PropertyId = propertyId; Title = Required(title); DueOn = dueOn; EscalationDays = escalationDays;
        Recurrence = recurrence; RecurrenceEndOn = recurrenceEndOn;
    }

    public Guid PropertyId { get; private set; }
    public string Title { get; private set; }
    public DateOnly DueOn { get; private set; }
    public int EscalationDays { get; private set; }
    public ComplianceRecurrence Recurrence { get; private set; }
    public DateOnly? RecurrenceEndOn { get; private set; }
    public ComplianceObligationStatus Status { get; private set; } = ComplianceObligationStatus.Active;
    public DateOnly? SatisfiedOn { get; private set; }

    public bool IsOverdue(DateOnly asOf) => Status == ComplianceObligationStatus.Active && asOf > DueOn;
    public bool IsEscalated(DateOnly asOf) => Status == ComplianceObligationStatus.Active && asOf > DueOn.AddDays(EscalationDays);
    public DateOnly? NextDueOn()
    {
        if (Recurrence == ComplianceRecurrence.None) return null;
        var next = Advance(DueOn);
        return RecurrenceEndOn is { } end && next > end ? null : next;
    }
    public DateOnly Advance(DateOnly dueOn) => Recurrence switch
    {
        ComplianceRecurrence.Monthly => AddMonthsPreservingMonthEnd(dueOn, 1),
        ComplianceRecurrence.Quarterly => AddMonthsPreservingMonthEnd(dueOn, 3),
        ComplianceRecurrence.Yearly => AddMonthsPreservingMonthEnd(dueOn, 12),
        _ => throw new InvalidOperationException("Unsupported compliance recurrence.")
    };
    private static DateOnly AddMonthsPreservingMonthEnd(DateOnly date, int months)
    {
        var target = date.AddMonths(months);
        return date.Day == DateTime.DaysInMonth(date.Year, date.Month)
            ? new DateOnly(target.Year, target.Month, DateTime.DaysInMonth(target.Year, target.Month))
            : target;
    }
    public static string OccurrenceKey(Guid obligationId, DateOnly dueOn) => $"compliance:{obligationId:N}:{dueOn:yyyyMMdd}";
    public void Satisfy(DateOnly on) { if (Status != ComplianceObligationStatus.Active) throw new InvalidOperationException("Obligation is not active."); Status = ComplianceObligationStatus.Satisfied; SatisfiedOn = on; }
    public void Waive() { if (Status != ComplianceObligationStatus.Active) throw new InvalidOperationException("Obligation is not active."); Status = ComplianceObligationStatus.Waived; }
    private static string Required(string value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 200 ? value.Trim() : throw new ArgumentException("Title must contain 1 to 200 characters.", nameof(value));
}
