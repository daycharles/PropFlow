namespace PropFlow.Domain.Leasing;

public enum ResidentPaymentStatus { Submitted, Settled, Failed }

// FS-S08 extends the Gate 1 payment primitive rather than adding a competing one.
// ProviderReference is the idempotency key: it is unique per organization
// (OperationsStore.cs), so a replayed provider callback finds the existing row instead of
// inserting a second one.
public sealed class ResidentPayment(Guid organizationId, Guid id, Guid leaseId, Guid residentId, decimal amount, DateOnly dueOn, string? reference, Guid? chargeId = null, string? providerReference = null)
    : TenantEntity(organizationId, id)
{
    public Guid LeaseId { get; private set; } = leaseId == Guid.Empty ? throw new ArgumentException("Lease is required.", nameof(leaseId)) : leaseId;
    public Guid ResidentId { get; private set; } = residentId == Guid.Empty ? throw new ArgumentException("Resident is required.", nameof(residentId)) : residentId;
    public Guid? ChargeId { get; private set; } = chargeId == Guid.Empty ? null : chargeId;
    public decimal Amount { get; private set; } = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
    public DateOnly DueOn { get; private set; } = dueOn;
    public string? Reference { get; private set; } = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
    public string? ProviderReference { get; private set; } = string.IsNullOrWhiteSpace(providerReference) ? null
        : providerReference.Trim().Length <= 200 ? providerReference.Trim()
        : throw new ArgumentException("A provider reference may contain at most 200 characters.", nameof(providerReference));
    public ResidentPaymentStatus Status { get; private set; } = ResidentPaymentStatus.Submitted;
    public DateTimeOffset SubmittedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SettledAt { get; private set; }
    public string? FailureReason { get; private set; }
    public decimal RefundedAmount { get; private set; }

    public decimal NetSettled => Status == ResidentPaymentStatus.Settled ? Amount - RefundedAmount : 0;

    public void Settle(DateTimeOffset at)
    {
        if (Status != ResidentPaymentStatus.Submitted) throw new InvalidOperationException("Only submitted payments can be settled.");
        Status = ResidentPaymentStatus.Settled;
        SettledAt = at;
    }

    public void Fail(string? reason = null)
    {
        if (Status != ResidentPaymentStatus.Submitted) throw new InvalidOperationException("Only submitted payments can fail.");
        Status = ResidentPaymentStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 500)];
    }

    public void Refund(decimal amount)
    {
        if (Status != ResidentPaymentStatus.Settled) throw new InvalidOperationException("Only a settled payment can be refunded.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        if (amount > Amount - RefundedAmount) throw new InvalidOperationException("The refund exceeds the unrefunded amount of the payment.");
        RefundedAmount += amount;
    }
}
