namespace PropFlow.Domain.Billing;

public enum RecurringChargeStatus { Active, Paused, Ended }

// A schedule that *generates* operations."LeaseCharges" rows; it is not a charge itself.
// FS-S08 deliberately keeps LeaseCharge as the single charge entity.
public sealed class RecurringCharge(Guid organizationId, Guid id, Guid leaseId, string description, decimal amount, int dayOfMonth, DateOnly startsOn, DateOnly? endsOn, DateTimeOffset createdAt)
    : TenantEntity(organizationId, id)
{
    public Guid LeaseId { get; private set; } = leaseId == Guid.Empty ? throw new ArgumentException("Lease is required.", nameof(leaseId)) : leaseId;
    public string Description { get; private set; } = Required(description, nameof(description), 200);
    public decimal Amount { get; private set; } = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
    // 1..28 so every month has the day; a 29th-31st schedule would silently skip months.
    public int DayOfMonth { get; private set; } = dayOfMonth is >= 1 and <= 28 ? dayOfMonth : throw new ArgumentOutOfRangeException(nameof(dayOfMonth), "Day of month must be between 1 and 28.");
    public DateOnly StartsOn { get; private set; } = startsOn;
    public DateOnly? EndsOn { get; private set; } = endsOn is { } end && end < startsOn ? throw new ArgumentException("The end date cannot precede the start date.", nameof(endsOn)) : endsOn;
    public RecurringChargeStatus Status { get; private set; } = RecurringChargeStatus.Active;
    // The high-water mark that makes generation idempotent: a second run over the same
    // period produces nothing.
    public DateOnly? GeneratedThrough { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;

    public IReadOnlyList<DateOnly> DueDatesThrough(DateOnly through)
    {
        if (Status != RecurringChargeStatus.Active) return [];
        var last = EndsOn is { } end && end < through ? end : through;
        var floor = GeneratedThrough is { } generated && generated >= StartsOn ? generated : StartsOn.AddDays(-1);
        var dates = new List<DateOnly>();
        for (var cursor = new DateOnly(StartsOn.Year, StartsOn.Month, DayOfMonth); cursor <= last; cursor = cursor.AddMonths(1))
            if (cursor > floor && cursor >= StartsOn) dates.Add(cursor);
        return dates;
    }

    public void MarkGeneratedThrough(DateOnly through)
    {
        if (GeneratedThrough is { } current && through < current) throw new ArgumentException("Generation cannot move backwards.", nameof(through));
        GeneratedThrough = through;
    }

    public void Pause()
    {
        if (Status != RecurringChargeStatus.Active) throw new InvalidOperationException("Only an active schedule can be paused.");
        Status = RecurringChargeStatus.Paused;
    }

    public void Resume()
    {
        if (Status != RecurringChargeStatus.Paused) throw new InvalidOperationException("Only a paused schedule can be resumed.");
        Status = RecurringChargeStatus.Active;
    }

    public void End(DateOnly on)
    {
        if (Status == RecurringChargeStatus.Ended) throw new InvalidOperationException("The schedule has already ended.");
        if (on < StartsOn) throw new ArgumentException("The end date cannot precede the start date.", nameof(on));
        EndsOn = on;
        Status = RecurringChargeStatus.Ended;
    }

    private static string Required(string value, string name, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}
