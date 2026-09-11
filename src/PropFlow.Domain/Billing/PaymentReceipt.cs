namespace PropFlow.Domain.Billing;

public sealed class PaymentReceipt(Guid organizationId, Guid id, Guid paymentId, string receiptNumber, DateTimeOffset issuedAt)
    : TenantEntity(organizationId, id)
{
    public Guid PaymentId { get; private set; } = paymentId == Guid.Empty ? throw new ArgumentException("Payment is required.", nameof(paymentId)) : paymentId;
    public string ReceiptNumber { get; private set; } = !string.IsNullOrWhiteSpace(receiptNumber) && receiptNumber.Trim().Length <= 100 ? receiptNumber.Trim() : throw new ArgumentException("Receipt number is required.", nameof(receiptNumber));
    public DateTimeOffset IssuedAt { get; private set; } = issuedAt;
}
