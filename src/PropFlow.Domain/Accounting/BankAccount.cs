namespace PropFlow.Domain.Accounting;

public enum BankAccountStatus { Active, Inactive }
public sealed class BankAccount(Guid organizationId, Guid id, string name, string institution, string lastFour, Guid assetAccountId, DateTimeOffset createdAt) : TenantEntity(organizationId, id)
{
    public string Name { get; private set; } = Required(name, nameof(name), 100);
    public string Institution { get; private set; } = Required(institution, nameof(institution), 100);
    public string LastFour { get; private set; } = Required(lastFour, nameof(lastFour), 4);
    public Guid AssetAccountId { get; private set; } = assetAccountId == Guid.Empty ? throw new ArgumentException("Asset account is required.", nameof(assetAccountId)) : assetAccountId;
    public BankAccountStatus Status { get; private set; } = BankAccountStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;
    public void Deactivate() => Status = BankAccountStatus.Inactive;
    private static string Required(string value, string name, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException("Value is required.", name);
}
