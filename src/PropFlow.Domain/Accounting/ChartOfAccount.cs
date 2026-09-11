namespace PropFlow.Domain.Accounting;

public enum AccountType { Asset, Liability, Equity, Revenue, Expense }

// The ledger's account dimension. Accounts are the one accounting table that is genuinely
// editable — a code/name typo has to be fixable and a retired account has to stop accepting
// postings — so this carries UPDATE-shaped behaviour while journals stay append-only.
public sealed class ChartOfAccount(Guid organizationId, Guid id, string code, string name, AccountType type, DateTimeOffset createdAt)
    : TenantEntity(organizationId, id)
{
    public string Code { get; private set; } = Required(code, nameof(code), 20);
    public string Name { get; private set; } = Required(name, nameof(name), 200);
    public AccountType Type { get; private set; } = Enum.IsDefined(type) ? type : throw new ArgumentException("Unknown account type.", nameof(type));
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt.ToUniversalTime();

    // Asset and Expense balances increase on the debit side; the other three on the credit side.
    // The trial balance uses this to present a signed balance per account.
    public bool IsDebitNormal => Type is AccountType.Asset or AccountType.Expense;

    public void Rename(string name) => Name = Required(name, nameof(name), 200);

    public void Deactivate()
    {
        if (!IsActive) throw new InvalidOperationException("The account is already inactive.");
        IsActive = false;
    }

    public void Activate()
    {
        if (IsActive) throw new InvalidOperationException("The account is already active.");
        IsActive = true;
    }

    private static string Required(string value, string name, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim()
            : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}
