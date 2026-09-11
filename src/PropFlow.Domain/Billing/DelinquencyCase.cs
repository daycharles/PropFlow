namespace PropFlow.Domain.Billing;

public enum DelinquencyStatus { Open, Contacted, Resolved }

public sealed class DelinquencyCase(Guid organizationId, Guid id, Guid leaseId, decimal balance, DateOnly openedOn, DateTimeOffset createdAt)
    : TenantEntity(organizationId, id)
{
    public Guid LeaseId { get; private set; } = leaseId == Guid.Empty ? throw new ArgumentException("Lease is required.", nameof(leaseId)) : leaseId;
    public decimal Balance { get; private set; } = balance > 0 ? balance : throw new ArgumentOutOfRangeException(nameof(balance));
    public DateOnly OpenedOn { get; private set; } = openedOn;
    public DelinquencyStatus Status { get; private set; } = DelinquencyStatus.Open;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;
    public void Contact() { if (Status == DelinquencyStatus.Resolved) throw new InvalidOperationException("Resolved case cannot be contacted."); Status = DelinquencyStatus.Contacted; }
    public void Resolve() => Status = DelinquencyStatus.Resolved;
}
