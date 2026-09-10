using PropFlow.Domain.Work;

namespace PropFlow.Domain.Attention;

// Ordered so OrderByDescending puts the most urgent first.
public enum AttentionSeverity { Informational, Warning, Critical }

public enum AttentionReason
{
    UnassignedEmergency,
    SlaBreach,
    Overdue,
    WaitingOnVendor,
    WaitingOnResident,
    RepeatRepair,
    UnitTurnAtRisk,
}

/// <summary>
/// The fixed thresholds the attention rules use. Not yet per-organization configurable — see
/// docs/backlog.md "Open questions". The first-response budget is measured from creation while
/// the work is still <see cref="WorkStatus.New"/> (nobody has picked it up).
/// </summary>
public static class AttentionThresholds
{
    public static TimeSpan FirstResponseBudget(WorkPriority priority) => priority switch
    {
        WorkPriority.Critical => TimeSpan.FromHours(4),
        WorkPriority.High => TimeSpan.FromHours(24),
        WorkPriority.Normal => TimeSpan.FromHours(72),
        _ => TimeSpan.FromHours(120),
    };

    public static readonly TimeSpan StaleHold = TimeSpan.FromDays(7);
    public static readonly TimeSpan UnitTurnLead = TimeSpan.FromDays(5);
}

/// <summary>
/// A read-only snapshot of one work item plus the two facts the rules need that are not on the
/// row: when anything last happened to it (its latest timeline entry, or its creation) and
/// whether its asset has crossed the repeat-repair threshold.
/// </summary>
public sealed record WorkAttentionSnapshot(
    WorkStatus Status,
    WorkPriority Priority,
    WorkType WorkType,
    bool HasVendor,
    bool HasEmployee,
    bool HasResident,
    bool AssetIsRepeatRepair,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset? DueDate);

public sealed record AttentionFinding(AttentionReason Reason, AttentionSeverity Severity, string Detail);

/// <summary>
/// Pure evaluation of the attention rules for a single work item. One work item can match
/// several reasons; the caller flattens the findings into the queue.
/// </summary>
public static class AttentionRules
{
    public static IReadOnlyList<AttentionFinding> Evaluate(WorkAttentionSnapshot work, DateTimeOffset now)
    {
        if (work.Status is WorkStatus.Completed or WorkStatus.Cancelled or WorkStatus.Draft) return [];

        var findings = new List<AttentionFinding>();

        if (work.Priority == WorkPriority.Critical && !work.HasVendor && !work.HasEmployee)
            findings.Add(new(AttentionReason.UnassignedEmergency, AttentionSeverity.Critical,
                "Critical priority with no vendor or staff assigned."));

        if (work.Status == WorkStatus.New)
        {
            var budget = AttentionThresholds.FirstResponseBudget(work.Priority);
            var age = now - work.CreatedAt;
            if (age > budget)
                findings.Add(new(AttentionReason.SlaBreach,
                    work.Priority is WorkPriority.Critical or WorkPriority.High
                        ? AttentionSeverity.Critical
                        : AttentionSeverity.Warning,
                    $"Unactioned for {Humanize(age)} — past the {Humanize(budget)} first-response target."));
        }

        if (work.DueDate is { } due && due < now && work.Status != WorkStatus.Completed)
            findings.Add(new(AttentionReason.Overdue,
                work.Priority == WorkPriority.Critical ? AttentionSeverity.Critical : AttentionSeverity.Warning,
                $"Past due by {Humanize(now - due)}."));

        if (work.Status == WorkStatus.OnHold && now - work.LastActivityAt > AttentionThresholds.StaleHold)
        {
            var stalled = Humanize(now - work.LastActivityAt);
            if (work.HasVendor)
                findings.Add(new(AttentionReason.WaitingOnVendor, AttentionSeverity.Warning,
                    $"On hold with a vendor for {stalled} with no update."));
            else if (work.HasResident)
                findings.Add(new(AttentionReason.WaitingOnResident, AttentionSeverity.Informational,
                    $"On hold pending the resident for {stalled}."));
        }

        if (work.AssetIsRepeatRepair)
            findings.Add(new(AttentionReason.RepeatRepair, AttentionSeverity.Warning,
                "Open work on an asset that has crossed the repeat-repair threshold."));

        if (work.WorkType == WorkType.UnitTurnTask
            && work.Status is WorkStatus.New or WorkStatus.Assigned
            && work.DueDate is { } turnDue && turnDue <= now + AttentionThresholds.UnitTurnLead)
            findings.Add(turnDue < now
                ? new(AttentionReason.UnitTurnAtRisk, AttentionSeverity.Critical,
                    "Unit-turn task is past its target date and not started.")
                : new(AttentionReason.UnitTurnAtRisk, AttentionSeverity.Warning,
                    $"Unit-turn task due in {Humanize(turnDue - now)} and not started."));

        return findings;
    }

    private static string Humanize(TimeSpan span)
    {
        var value = span < TimeSpan.Zero ? TimeSpan.Zero : span;
        if (value.TotalDays >= 1) return Plural((int)Math.Round(value.TotalDays), "day");
        if (value.TotalHours >= 1) return Plural((int)Math.Round(value.TotalHours), "hour");
        return Plural(Math.Max(1, (int)Math.Round(value.TotalMinutes)), "minute");
    }

    private static string Plural(int count, string unit) => $"{count} {unit}{(count == 1 ? "" : "s")}";
}
