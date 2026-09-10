namespace PropFlow.Application.Assets;

/// <summary>The organization's repeat-repair thresholds.</summary>
public sealed record RepeatRepairPolicyView(int RepairThreshold, int WindowDays, bool MatchByCategory);

/// <summary>
/// The repeat-repair picture for one asset: how much recent work it has taken, whether that
/// crosses the threshold, and the cost so far in the window. <see cref="Since"/> is the start of
/// the detection window.
/// </summary>
public sealed record RepeatRepairAssessment(
    int RepairThreshold,
    int WindowDays,
    bool MatchByCategory,
    int RepairCount,
    DateTimeOffset Since,
    decimal TotalCostInWindow,
    int? AgeInYears,
    bool IsRepeatRepair);

public interface IRepeatRepairDetector
{
    /// <summary>The stored policy, or the defaults when the organization has not configured one.</summary>
    Task<RepeatRepairPolicyView> GetPolicyAsync(CancellationToken cancellationToken);

    /// <summary>Upsert the organization's policy. Throws <see cref="ArgumentOutOfRangeException"/> on an out-of-range value.</summary>
    Task<RepeatRepairPolicyView> SetPolicyAsync(int repairThreshold, int windowDays, bool matchByCategory, CancellationToken cancellationToken);

    /// <summary>
    /// Assess the asset against the current policy. <paramref name="categoryId"/> narrows the
    /// count to matching work when the policy's category-similarity option is on (for the
    /// "creating work about this asset" surface, which has a category but no work id yet).
    /// Returns <c>null</c> when the asset does not exist in this tenant.
    /// </summary>
    Task<RepeatRepairAssessment?> AssessAsync(Guid assetId, Guid? categoryId, CancellationToken cancellationToken);
}
