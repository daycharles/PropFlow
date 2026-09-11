namespace PropFlow.Domain.Billing;

public enum CreditStatus { Open, Applied, Voided }

// A concession or goodwill credit held against a lease. Applying it moves value into
// LeaseCharge.AmountApplied, so the lease balance stays a function of the charges.
public sealed class Credit(Guid organizationId, Guid id, Guid leaseId, decimal amount, string reason, DateOnly issuedOn, DateTimeOffset createdAt)
    : TenantEntity(organizationId, id)
{
    public Guid LeaseId { get; private set; } = leaseId == Guid.Empty ? throw new ArgumentException("Lease is required.", nameof(leaseId)) : leaseId;
    public decimal Amount { get; private set; } = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
    public string Reason { get; private set; } = !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 200 ? reason.Trim() : throw new ArgumentException("Value must contain 1 to 200 characters.", nameof(reason));
    public DateOnly IssuedOn { get; private set; } = issuedOn;
    public decimal AppliedAmount { get; private set; }
    public CreditStatus Status { get; private set; } = CreditStatus.Open;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;

    public decimal Remaining => Amount - AppliedAmount;

    public void Apply(decimal amount)
    {
        if (Status == CreditStatus.Voided) throw new InvalidOperationException("A voided credit cannot be applied.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        if (amount > Remaining) throw new InvalidOperationException("The credit does not have enough remaining value.");
        AppliedAmount += amount;
        if (Remaining == 0) Status = CreditStatus.Applied;
    }

    public void Void()
    {
        if (AppliedAmount > 0) throw new InvalidOperationException("A credit that has been applied cannot be voided.");
        Status = CreditStatus.Voided;
    }
}
