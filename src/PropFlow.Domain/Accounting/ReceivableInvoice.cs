namespace PropFlow.Domain.Accounting;

public enum ReceivableStatus { Open, Paid, Void }
public sealed class ReceivableInvoice(Guid organizationId, Guid id, Guid residentId, string invoiceNumber, decimal amount, DateOnly dueOn, DateTimeOffset createdAt) : TenantEntity(organizationId, id)
{
    public Guid ResidentId { get; private set; } = residentId == Guid.Empty ? throw new ArgumentException("Resident is required.", nameof(residentId)) : residentId;
    public string InvoiceNumber { get; private set; } = !string.IsNullOrWhiteSpace(invoiceNumber) && invoiceNumber.Trim().Length <= 100 ? invoiceNumber.Trim() : throw new ArgumentException("Invoice number is required.", nameof(invoiceNumber));
    public decimal Amount { get; private set; } = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
    public decimal AmountPaid { get; private set; }
    public DateOnly DueOn { get; private set; } = dueOn;
    public ReceivableStatus Status { get; private set; } = ReceivableStatus.Open;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;
    public void Receive(decimal amount) { if (Status != ReceivableStatus.Open) throw new InvalidOperationException("Only open invoices can receive payment."); if (amount <= 0 || amount > Amount - AmountPaid) throw new ArgumentOutOfRangeException(nameof(amount)); AmountPaid += amount; if (AmountPaid == Amount) Status = ReceivableStatus.Paid; }
    public void Void() { if (AmountPaid > 0) throw new InvalidOperationException("A paid invoice cannot be voided."); Status = ReceivableStatus.Void; }
}
