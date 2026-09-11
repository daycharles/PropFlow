namespace PropFlow.Domain.Accounting;

public enum BudgetStatus { Draft, PendingApproval, Approved, Rejected }

public sealed class Budget(Guid organizationId, Guid id, Guid propertyId, int year, string name, DateTimeOffset createdAt)
    : TenantEntity(organizationId, id)
{
    public Guid PropertyId { get; private set; } = propertyId == Guid.Empty ? throw new ArgumentException("Property is required.", nameof(propertyId)) : propertyId;
    public int Year { get; private set; } = year is >= 2000 and <= 2100 ? year : throw new ArgumentOutOfRangeException(nameof(year));
    public string Name { get; private set; } = Required(name, nameof(name), 200);
    public BudgetStatus Status { get; private set; } = BudgetStatus.Draft;
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = createdAt.ToUniversalTime();
    public void Submit() { if (Status != BudgetStatus.Draft) throw new InvalidOperationException("Only a draft budget can be submitted."); Status = BudgetStatus.PendingApproval; }
    public void Approve(Guid actor, DateTimeOffset at) { if (actor == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actor)); if (Status != BudgetStatus.PendingApproval) throw new InvalidOperationException("Only a submitted budget can be approved."); Status = BudgetStatus.Approved; ApprovedBy = actor; ApprovedAt = at.ToUniversalTime(); }
    public void Reject() { if (Status != BudgetStatus.PendingApproval) throw new InvalidOperationException("Only a submitted budget can be rejected."); Status = BudgetStatus.Rejected; }
    private static string Required(string value, string name, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}

public sealed class BudgetLine(Guid organizationId, Guid id, Guid budgetId, Guid accountId, int month, decimal amount)
    : TenantEntity(organizationId, id)
{
    public Guid BudgetId { get; private set; } = budgetId == Guid.Empty ? throw new ArgumentException("Budget is required.", nameof(budgetId)) : budgetId;
    public Guid AccountId { get; private set; } = accountId == Guid.Empty ? throw new ArgumentException("Account is required.", nameof(accountId)) : accountId;
    public int Month { get; private set; } = month is >= 1 and <= 12 ? month : throw new ArgumentOutOfRangeException(nameof(month));
    public decimal Amount { get; private set; } = amount >= 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
}
