namespace PropFlow.Domain.Assets;

public enum AssetCostType
{
    Maintenance = 1,
    Repair = 2,
    CapitalImprovement = 3,
    Replacement = 4
}

public sealed class AssetLifecycleCost : TenantEntity
{
    private AssetLifecycleCost(Guid organizationId, Guid id) : base(organizationId, id) { }

    public AssetLifecycleCost(Guid organizationId, Guid id, Guid assetId, AssetCostType type,
        decimal amount, DateOnly incurredOn, string description, Guid? workItemId = null)
        : base(organizationId, id)
    {
        if (assetId == Guid.Empty) throw new ArgumentException("Asset is required.", nameof(assetId));
        if (amount < 0m) throw new ArgumentOutOfRangeException(nameof(amount), "Cost cannot be negative.");
        AssetId = assetId;
        Type = type;
        Amount = decimal.Round(amount, 2);
        IncurredOn = incurredOn;
        Description = Required(description);
        WorkItemId = workItemId;
    }

    public Guid AssetId { get; private set; }
    public AssetCostType Type { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly IncurredOn { get; private set; }
    public string Description { get; private set; } = "";
    public Guid? WorkItemId { get; private set; }

    private static string Required(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 500
            ? value.Trim()
            : throw new ArgumentException("Description must contain 1 to 500 characters.", nameof(value));
}
