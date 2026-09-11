namespace PropFlow.Domain.Accounting;

public enum FiscalPeriodStatus { Open, Closed }

// The accounting calendar. Closing a period is the control that stops the books moving under a
// report that has already been issued: JournalEntry.Post refuses a closed period, so a
// correction after close has to be a reversing entry dated into an open period.
public sealed class FiscalPeriod(Guid organizationId, Guid id, string name, DateOnly startsOn, DateOnly endsOn)
    : TenantEntity(organizationId, id)
{
    public string Name { get; private set; } = !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 50
        ? name.Trim()
        : throw new ArgumentException("Value must contain 1 to 50 characters.", nameof(name));
    public DateOnly StartsOn { get; private set; } = startsOn;
    public DateOnly EndsOn { get; private set; } = endsOn >= startsOn
        ? endsOn
        : throw new ArgumentException("A fiscal period cannot end before it starts.", nameof(endsOn));
    public FiscalPeriodStatus Status { get; private set; } = FiscalPeriodStatus.Open;
    public Guid? ClosedBy { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public bool IsClosed => Status is FiscalPeriodStatus.Closed;
    public bool Covers(DateOnly date) => date >= StartsOn && date <= EndsOn;

    public void Close(Guid actorId, DateTimeOffset at)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        if (IsClosed) throw new InvalidOperationException("The fiscal period is already closed.");
        Status = FiscalPeriodStatus.Closed;
        ClosedBy = actorId;
        ClosedAt = at.ToUniversalTime();
    }
}
