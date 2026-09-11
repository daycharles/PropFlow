namespace PropFlow.Domain.Leasing;

public enum ResidentPaymentStatus { Submitted, Settled, Failed }

public sealed class ResidentPayment(Guid organizationId, Guid id, Guid leaseId, Guid residentId, decimal amount, DateOnly dueOn, string? reference, Guid? chargeId = null)
    : TenantEntity(organizationId, id)
{
    public Guid LeaseId { get; private set; } = leaseId == Guid.Empty ? throw new ArgumentException("Lease is required.", nameof(leaseId)) : leaseId;
    public Guid ResidentId { get; private set; } = residentId == Guid.Empty ? throw new ArgumentException("Resident is required.", nameof(residentId)) : residentId;
    public Guid? ChargeId { get; private set; } = chargeId == Guid.Empty ? null : chargeId;
    public decimal Amount { get; private set; } = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
    public DateOnly DueOn { get; private set; } = dueOn;
    public string? Reference { get; private set; } = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
    public ResidentPaymentStatus Status { get; private set; } = ResidentPaymentStatus.Submitted;
    public DateTimeOffset SubmittedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SettledAt { get; private set; }
    public void Settle() { if (Status != ResidentPaymentStatus.Submitted) throw new InvalidOperationException("Only submitted payments can be settled."); Status = ResidentPaymentStatus.Settled; SettledAt = DateTimeOffset.UtcNow; }
    public void Fail() { if (Status != ResidentPaymentStatus.Submitted) throw new InvalidOperationException("Only submitted payments can fail."); Status = ResidentPaymentStatus.Failed; }
}
