namespace PropFlow.Domain.Billing;

public enum ReconciliationStatus { Matched, Exception, Resolved }

public sealed class PaymentReconciliation(Guid organizationId, Guid id, string providerReference, Guid? paymentId, decimal amount, string? note, DateTimeOffset createdAt)
    : TenantEntity(organizationId, id)
{
    public string ProviderReference { get; private set; } = !string.IsNullOrWhiteSpace(providerReference) && providerReference.Trim().Length <= 200 ? providerReference.Trim() : throw new ArgumentException("Provider reference is required.", nameof(providerReference));
    public Guid? PaymentId { get; private set; } = paymentId;
    public decimal Amount { get; private set; } = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
    public string? Note { get; private set; } = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)];
    public ReconciliationStatus Status { get; private set; } = paymentId is null ? ReconciliationStatus.Exception : ReconciliationStatus.Matched;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;
    public void Resolve(string? note) { Status = ReconciliationStatus.Resolved; Note = string.IsNullOrWhiteSpace(note) ? Note : note.Trim()[..Math.Min(note.Trim().Length, 500)]; }
}
