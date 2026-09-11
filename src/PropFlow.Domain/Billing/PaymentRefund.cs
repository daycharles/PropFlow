namespace PropFlow.Domain.Billing;

// Append-only. A refund is a separate row rather than a mutation of the payment so the
// provider reference on it can carry the same idempotency guarantee a payment does, and so
// the runtime role needs no UPDATE grant on the refund trail.
public sealed class PaymentRefund(Guid organizationId, Guid id, Guid paymentId, decimal amount, string providerReference, string? reason, DateTimeOffset issuedAt)
    : TenantEntity(organizationId, id)
{
    public Guid PaymentId { get; private set; } = paymentId == Guid.Empty ? throw new ArgumentException("Payment is required.", nameof(paymentId)) : paymentId;
    public decimal Amount { get; private set; } = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
    public string ProviderReference { get; private set; } = !string.IsNullOrWhiteSpace(providerReference) && providerReference.Trim().Length <= 200
        ? providerReference.Trim()
        : throw new ArgumentException("A provider reference of 1 to 200 characters is required.", nameof(providerReference));
    public string? Reason { get; private set; } = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    public DateTimeOffset IssuedAt { get; private set; } = issuedAt;
}
