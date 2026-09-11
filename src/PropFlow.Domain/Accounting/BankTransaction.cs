namespace PropFlow.Domain.Accounting;

public enum BankTransactionStatus { Unmatched, Matched }
public sealed class BankTransaction(Guid organizationId, Guid id, Guid bankAccountId, string externalId, decimal amount, DateOnly postedOn, DateTimeOffset createdAt) : TenantEntity(organizationId, id)
{
    public Guid BankAccountId { get; private set; } = bankAccountId == Guid.Empty ? throw new ArgumentException("Bank account is required.", nameof(bankAccountId)) : bankAccountId;
    public string ExternalId { get; private set; } = !string.IsNullOrWhiteSpace(externalId) && externalId.Trim().Length <= 200 ? externalId.Trim() : throw new ArgumentException("External id is required.", nameof(externalId));
    public decimal Amount { get; private set; } = amount == 0 ? throw new ArgumentOutOfRangeException(nameof(amount)) : amount;
    public DateOnly PostedOn { get; private set; } = postedOn;
    public BankTransactionStatus Status { get; private set; } = BankTransactionStatus.Unmatched;
    public Guid? JournalEntryId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;
    public void Match(Guid journalEntryId) { if (journalEntryId == Guid.Empty) throw new ArgumentException("Journal entry is required.", nameof(journalEntryId)); JournalEntryId = journalEntryId; Status = BankTransactionStatus.Matched; }
}
