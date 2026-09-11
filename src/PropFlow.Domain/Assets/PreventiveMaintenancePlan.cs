namespace PropFlow.Domain.Assets;

public enum MaintenanceRecurrence
{
    Monthly = 1,
    Quarterly = 2,
    Yearly = 3
}

public sealed class PreventiveMaintenancePlan : TenantEntity
{
    private PreventiveMaintenancePlan(Guid organizationId, Guid id) : base(organizationId, id) { }

    public PreventiveMaintenancePlan(Guid organizationId, Guid id, Guid assetId, string name,
        MaintenanceRecurrence recurrence, DateOnly firstDueOn)
        : base(organizationId, id)
    {
        if (assetId == Guid.Empty) throw new ArgumentException("Asset is required.", nameof(assetId));
        AssetId = assetId;
        Rename(name);
        Recurrence = recurrence;
        FirstDueOn = firstDueOn;
    }

    public Guid AssetId { get; private set; }
    public string Name { get; private set; } = "";
    public MaintenanceRecurrence Recurrence { get; private set; }
    public DateOnly FirstDueOn { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new ArgumentException("Name must contain 1 to 200 characters.", nameof(name));
        Name = name.Trim();
    }

    public void SetActive(bool active) => IsActive = active;

    public DateOnly DueOn(int occurrence)
    {
        if (occurrence < 0) throw new ArgumentOutOfRangeException(nameof(occurrence));
        return Recurrence switch
        {
            MaintenanceRecurrence.Monthly => FirstDueOn.AddMonths(occurrence),
            MaintenanceRecurrence.Quarterly => FirstDueOn.AddMonths(occurrence * 3),
            MaintenanceRecurrence.Yearly => FirstDueOn.AddYears(occurrence),
            _ => throw new InvalidOperationException("Unsupported maintenance recurrence.")
        };
    }

    public DateOnly? NextDueOn(DateOnly asOf)
    {
        if (!IsActive) return null;
        if (asOf <= FirstDueOn) return FirstDueOn;
        var months = (asOf.Year - FirstDueOn.Year) * 12 + asOf.Month - FirstDueOn.Month;
        var step = MonthsPerOccurrence();
        var occurrence = months / step;
        var candidate = DueOn(occurrence);
        return candidate < asOf ? DueOn(occurrence + 1) : candidate;
    }

    public IReadOnlyList<PreventiveOccurrence> DueOccurrencesThrough(DateOnly through)
    {
        if (!IsActive || through < FirstDueOn) return [];
        var occurrences = new List<PreventiveOccurrence>();
        for (var occurrence = 0; ; occurrence++)
        {
            var dueOn = DueOn(occurrence);
            if (dueOn > through) break;
            occurrences.Add(new PreventiveOccurrence(Id, AssetId, occurrence, dueOn, OccurrenceKey(Id, occurrence)));
        }
        return occurrences;
    }

    public int OccurrenceFor(DateOnly dueOn)
    {
        if (dueOn < FirstDueOn) throw new ArgumentOutOfRangeException(nameof(dueOn));
        var months = (dueOn.Year - FirstDueOn.Year) * 12 + dueOn.Month - FirstDueOn.Month;
        var step = MonthsPerOccurrence();
        if (months % step != 0 || FirstDueOn.AddMonths(months) != dueOn)
            throw new ArgumentException("Date is not an occurrence of this plan.", nameof(dueOn));
        return months / step;
    }

    public static string OccurrenceKey(Guid planId, int occurrence) => $"pm:{planId:N}:{occurrence}";

    private int MonthsPerOccurrence() => Recurrence switch
    {
        MaintenanceRecurrence.Monthly => 1,
        MaintenanceRecurrence.Quarterly => 3,
        MaintenanceRecurrence.Yearly => 12,
        _ => throw new InvalidOperationException("Unsupported maintenance recurrence.")
    };
}

public sealed record PreventiveOccurrence(Guid PlanId, Guid AssetId, int Occurrence, DateOnly DueOn, string OccurrenceKey);
