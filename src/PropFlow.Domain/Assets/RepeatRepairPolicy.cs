namespace PropFlow.Domain.Assets;

// One per organization: the thresholds that decide when an asset's recent work counts as a
// "repeat repair". Detection itself (counting the window) lives in the application layer; this
// entity only holds the knobs and the comparison.
public sealed class RepeatRepairPolicy : TenantEntity
{
    // EF materialization only.
    private RepeatRepairPolicy(Guid organizationId, Guid id) : base(organizationId, id) { }

    public RepeatRepairPolicy(Guid organizationId, Guid id, int repairThreshold = DefaultThreshold,
        int windowDays = DefaultWindowDays, bool matchByCategory = false)
        : base(organizationId, id) => Configure(repairThreshold, windowDays, matchByCategory);

    public const int DefaultThreshold = 3;
    public const int DefaultWindowDays = 120;

    public int RepairThreshold { get; private set; } = DefaultThreshold;
    public int WindowDays { get; private set; } = DefaultWindowDays;
    // When set, only work sharing a category counts toward the threshold.
    public bool MatchByCategory { get; private set; }

    public void Configure(int repairThreshold, int windowDays, bool matchByCategory)
    {
        if (repairThreshold is < 2 or > 50)
            throw new ArgumentOutOfRangeException(nameof(repairThreshold), "The repeat-repair threshold must be 2 to 50.");
        if (windowDays is < 7 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(windowDays), "The detection window must be 7 to 3650 days.");
        RepairThreshold = repairThreshold;
        WindowDays = windowDays;
        MatchByCategory = matchByCategory;
    }

    public bool Triggers(int repairCount) => repairCount >= RepairThreshold;
}
