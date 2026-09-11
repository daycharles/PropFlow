namespace PropFlow.Domain.Accounting;

public enum PayableStatus { Open, Paid, Void }
public sealed class PayableInvoice(Guid organizationId, Guid id, Guid vendorId, string invoiceNumber, decimal amount, DateOnly dueOn, DateTimeOffset createdAt) : TenantEntity(organizationId, id)
{
    public Guid VendorId { get; private set; } = vendorId == Guid.Empty ? throw new ArgumentException("Vendor is required.", nameof(vendorId)) : vendorId;
    public string InvoiceNumber { get; private set; } = Required(invoiceNumber, nameof(invoiceNumber), 100);
    public decimal Amount { get; private set; } = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
    public decimal AmountPaid { get; private set; }
    public DateOnly DueOn { get; private set; } = dueOn;
    public PayableStatus Status { get; private set; } = PayableStatus.Open;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;
    public void Pay(decimal amount) { if (Status != PayableStatus.Open) throw new InvalidOperationException("Only open invoices can be paid."); if (amount <= 0 || amount > Amount - AmountPaid) throw new ArgumentOutOfRangeException(nameof(amount)); AmountPaid += amount; if (AmountPaid == Amount) Status = PayableStatus.Paid; }
    public void Void() { if (AmountPaid > 0) throw new InvalidOperationException("A paid invoice cannot be voided."); Status = PayableStatus.Void; }
    private static string Required(string value, string name, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException("Invoice number is required.", name);
}
