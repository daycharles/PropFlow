namespace PropFlow.Domain.Leasing;

public enum LeaseChargeType { Recurring, OneTime }
public enum LeaseChargeStatus { Open, Paid, Voided }

public sealed class LeaseCharge(Guid organizationId, Guid id, Guid leaseId, LeaseChargeType type, string description, decimal amount, DateOnly dueOn) : TenantEntity(organizationId, id)
{
    public Guid LeaseId { get; private set; } = leaseId == Guid.Empty ? throw new ArgumentException("Lease is required.", nameof(leaseId)) : leaseId;
    public LeaseChargeType Type { get; private set; } = type;
    public string Description { get; private set; } = Required(description, nameof(description), 200);
    public decimal Amount { get; private set; } = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
    public DateOnly DueOn { get; private set; } = dueOn;
    public LeaseChargeStatus Status { get; private set; } = LeaseChargeStatus.Open;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public void MarkPaid() { if (Status != LeaseChargeStatus.Open) throw new InvalidOperationException("Only open charges can be paid."); Status = LeaseChargeStatus.Paid; }
    public void Void() { if (Status == LeaseChargeStatus.Paid) throw new InvalidOperationException("A paid charge cannot be voided."); Status = LeaseChargeStatus.Voided; }
    private static string Required(string value, string name, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}
