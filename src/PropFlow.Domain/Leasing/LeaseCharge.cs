namespace PropFlow.Domain.Leasing;

public enum LeaseChargeType { Recurring, OneTime, LateFee }
public enum LeaseChargeStatus { Open, PartiallyPaid, Paid, Voided }

// FS-S08 extends this entity rather than introducing a second charge concept: it is the one
// row a lease balance is computed from. AmountApplied carries both settled payments and
// applied credits, so Outstanding is the single source of the balance.
public sealed class LeaseCharge(Guid organizationId, Guid id, Guid leaseId, LeaseChargeType type, string description, decimal amount, DateOnly dueOn, Guid? recurringChargeId = null) : TenantEntity(organizationId, id)
{
    public Guid LeaseId { get; private set; } = leaseId == Guid.Empty ? throw new ArgumentException("Lease is required.", nameof(leaseId)) : leaseId;
    public LeaseChargeType Type { get; private set; } = type;
    public string Description { get; private set; } = Required(description, nameof(description), 200);
    public decimal Amount { get; private set; } = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
    public DateOnly DueOn { get; private set; } = dueOn;
    public LeaseChargeStatus Status { get; private set; } = LeaseChargeStatus.Open;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    // Set when the row was produced by a RecurringCharge schedule. The unique index on
    // (OrganizationId, RecurringChargeId, DueOn) is what stops a second generation run from
    // duplicating a period.
    public Guid? RecurringChargeId { get; private set; } = recurringChargeId == Guid.Empty ? null : recurringChargeId;
    public decimal AmountApplied { get; private set; }
    // Stamped when a late fee has been raised against this charge, so a second late-fee run
    // on the same day (or any later day) cannot raise a second fee for it.
    public DateOnly? LateFeeAppliedOn { get; private set; }

    public decimal Outstanding => Amount - AmountApplied;

    public void Apply(decimal amount)
    {
        if (Status == LeaseChargeStatus.Voided) throw new InvalidOperationException("A voided charge cannot take a payment.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        if (amount > Outstanding) throw new InvalidOperationException("The amount exceeds the outstanding balance of the charge.");
        AmountApplied += amount;
        Status = Outstanding == 0 ? LeaseChargeStatus.Paid : LeaseChargeStatus.PartiallyPaid;
    }

    // A refund or a reversed credit hands value back; the charge becomes owed again.
    public void Reverse(decimal amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        if (amount > AmountApplied) throw new InvalidOperationException("The amount exceeds what has been applied to the charge.");
        AmountApplied -= amount;
        if (Status != LeaseChargeStatus.Voided) Status = AmountApplied == 0 ? LeaseChargeStatus.Open : LeaseChargeStatus.PartiallyPaid;
    }

    public void MarkPaid()
    {
        if (Status is LeaseChargeStatus.Paid or LeaseChargeStatus.Voided) throw new InvalidOperationException("Only open charges can be paid.");
        Apply(Outstanding);
    }

    public void MarkLateFeeApplied(DateOnly on)
    {
        if (LateFeeAppliedOn is not null) throw new InvalidOperationException("A late fee has already been raised for this charge.");
        LateFeeAppliedOn = on;
    }

    public void Void()
    {
        if (Status == LeaseChargeStatus.Paid) throw new InvalidOperationException("A paid charge cannot be voided.");
        if (AmountApplied > 0) throw new InvalidOperationException("A charge with money applied to it cannot be voided.");
        Status = LeaseChargeStatus.Voided;
    }

    private static string Required(string value, string name, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}
