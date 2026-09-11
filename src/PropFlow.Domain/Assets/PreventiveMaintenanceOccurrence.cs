namespace PropFlow.Domain.Assets;

/// <summary>Durable idempotency record for one preventive-maintenance occurrence.</summary>
public sealed class PreventiveMaintenanceOccurrence : TenantEntity
{
    private PreventiveMaintenanceOccurrence(Guid organizationId, Guid id) : base(organizationId, id) { }

    public PreventiveMaintenanceOccurrence(Guid organizationId, Guid id, Guid planId, Guid assetId,
        string occurrenceKey, DateOnly dueOn, Guid workItemId, DateTimeOffset generatedAt)
        : base(organizationId, id)
    {
        if (planId == Guid.Empty) throw new ArgumentException("Plan is required.", nameof(planId));
        if (assetId == Guid.Empty) throw new ArgumentException("Asset is required.", nameof(assetId));
        if (workItemId == Guid.Empty) throw new ArgumentException("Work item is required.", nameof(workItemId));
        if (string.IsNullOrWhiteSpace(occurrenceKey) || occurrenceKey.Trim().Length > 120)
            throw new ArgumentException("Occurrence key is required.", nameof(occurrenceKey));
        PlanId = planId;
        AssetId = assetId;
        OccurrenceKey = occurrenceKey.Trim();
        DueOn = dueOn;
        WorkItemId = workItemId;
        GeneratedAt = generatedAt.ToUniversalTime();
    }

    public Guid PlanId { get; private set; }
    public Guid AssetId { get; private set; }
    public string OccurrenceKey { get; private set; } = "";
    public DateOnly DueOn { get; private set; }
    public Guid WorkItemId { get; private set; }
    public DateTimeOffset GeneratedAt { get; private set; }
}
