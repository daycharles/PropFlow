namespace PropFlow.Domain.Billing;

// One rule per scope: PropertyId null is the organization-wide default, a non-null
// PropertyId overrides it for that property.
public sealed class LateFeeRule(Guid organizationId, Guid id, Guid? propertyId, string name, int graceDays, decimal flatAmount, decimal percentOfOutstanding, decimal? maximumAmount, DateTimeOffset createdAt)
    : TenantEntity(organizationId, id)
{
    public Guid? PropertyId { get; private set; } = propertyId == Guid.Empty ? null : propertyId;
    public string Name { get; private set; } = !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200 ? name.Trim() : throw new ArgumentException("Value must contain 1 to 200 characters.", nameof(name));
    public int GraceDays { get; private set; } = graceDays is >= 0 and <= 90 ? graceDays : throw new ArgumentOutOfRangeException(nameof(graceDays), "Grace days must be between 0 and 90.");
    // A rule that charges nothing is a configuration mistake, not a no-op rule, so the
    // "at least one of flat/percent" invariant is enforced here rather than left to the caller.
    public decimal FlatAmount { get; private set; } = flatAmount < 0
        ? throw new ArgumentOutOfRangeException(nameof(flatAmount), "The flat amount cannot be negative.")
        : flatAmount == 0 && percentOfOutstanding <= 0
            ? throw new ArgumentException("A rule must set a flat amount, a percentage, or both.", nameof(flatAmount))
            : flatAmount;
    public decimal PercentOfOutstanding { get; private set; } = percentOfOutstanding is >= 0 and <= 100 ? percentOfOutstanding : throw new ArgumentOutOfRangeException(nameof(percentOfOutstanding), "The percentage must be between 0 and 100.");
    public decimal? MaximumAmount { get; private set; } = maximumAmount is { } maximum && maximum <= 0 ? throw new ArgumentOutOfRangeException(nameof(maximumAmount), "The maximum must be greater than zero.") : maximumAmount;
    public bool IsEnabled { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;

    // Banker-free rounding: money rounds half away from zero, the way an invoice does.
    public decimal FeeFor(decimal outstanding)
    {
        if (outstanding <= 0) return 0;
        var fee = FlatAmount + Math.Round(outstanding * PercentOfOutstanding / 100m, 2, MidpointRounding.AwayFromZero);
        if (MaximumAmount is { } maximum && fee > maximum) fee = maximum;
        return Math.Round(fee, 2, MidpointRounding.AwayFromZero);
    }

    public bool AppliesOn(DateOnly dueOn, DateOnly asOf) => IsEnabled && asOf > dueOn.AddDays(GraceDays);
}
