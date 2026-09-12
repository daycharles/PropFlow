using PropFlow.Domain.Integrations;

namespace PropFlow.Application.Integrations;

// What the reconciler does with one external record this run.
public enum ReconcileAction
{
    // The PropFlow row does not exist yet and everything needed to build it is present.
    Create = 0,

    // The PropFlow row exists and the source has moved on since it was last written.
    Update = 1,

    // ReconciledHash already equals ContentHash: the PropFlow row is current. The replay-safe
    // fast path, and the reason a second sync of an unchanged source writes nothing.
    Unchanged = 2,

    // A divergence a human has to settle. A conflict row is raised in EVERY mode, including
    // ReportOnly — reporting is the point of ReportOnly.
    Conflict = 3,

    // Nothing to do and nothing to report: a ReportOnly profile, or a child whose parent did not
    // reconcile this run. Deliberately distinct from Conflict; see ParentState below.
    Skip = 4
}

// Everything a Conflict row needs, decided without touching the database.
public sealed record ConflictDetail(
    ConflictReason Reason,
    string Field,
    string? ObservedValue,
    string? CurrentValue,
    string? Detail);

public sealed record ReconcileDecision(ReconcileAction Action, ConflictDetail? Conflict = null)
{
    public static ReconcileDecision Create { get; } = new(ReconcileAction.Create);
    public static ReconcileDecision Update { get; } = new(ReconcileAction.Update);
    public static ReconcileDecision Unchanged { get; } = new(ReconcileAction.Unchanged);
    public static ReconcileDecision Skip { get; } = new(ReconcileAction.Skip);
    public static ReconcileDecision Conflicted(ConflictDetail detail) =>
        new(ReconcileAction.Conflict, detail);
}

// Whether the records this one hangs off are available.
public enum ParentState
{
    // This kind has no parent (Property), or every parent it names resolved.
    Resolved = 0,

    // The parent is in this snapshot but did not reconcile this run — usually because the parent
    // itself raised a conflict, or because the profile is ReportOnly.
    //
    // This is a Skip, NOT a conflict, and the distinction matters more than it looks. One missing
    // TargetPortfolioId would otherwise cascade into a conflict for every space, occupancy, work
    // order and asset under that property: nine queue entries for one missing field, eight of
    // which a human can do nothing about. The parent's conflict is the actionable one, and the
    // children follow on the next run once it is fixed.
    NotReconciledYet = 1,

    // The parent external id is not in this snapshot at all. That is a genuinely broken feed —
    // the source is describing a space in a property it did not send — so it IS a conflict.
    MissingFromSnapshot = 2
}

// The reconciler's decision procedure, and the per-kind mapping preconditions it feeds on.
//
// PURE by construction: no I/O, no clock, no database. Everything it needs is already materialised
// by the caller, which is what lets the whole decision table be unit-tested without Docker and
// lets PF-S19.10's replay assertions be written against something they can reason about.
public static class ReconciliationRules
{
    /// <summary>
    /// The one decision sequence, identical for every canonical kind. Order is load-bearing and is
    /// the table PF-S19.10 tests against:
    /// <list type="number">
    /// <item>a mapping gap intrinsic to the record is a Conflict, in every mode;</item>
    /// <item>a parent absent from the whole snapshot is a Conflict;</item>
    /// <item>a parent that merely did not reconcile this run is a Skip;</item>
    /// <item>a ReportOnly (or absent) profile is a Skip — it reported, it does not write;</item>
    /// <item>an already-reconciled, unchanged record is Unchanged;</item>
    /// <item>otherwise Create when there is no linked PropFlow row, Update when there is.</item>
    /// </list>
    /// Step 1 precedes step 4 on purpose: ReportOnly still raises conflicts, which is the entire
    /// value of connect → sync → review → promote.
    /// </summary>
    public static ReconcileDecision Decide(ExternalRecordLink link, MappingProfile? profile,
        ParentState parents, ConflictDetail? mappingGap)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (mappingGap is not null) return ReconcileDecision.Conflicted(mappingGap);

        if (parents == ParentState.MissingFromSnapshot)
            return ReconcileDecision.Conflicted(new ConflictDetail(ConflictReason.MissingParentLink,
                "ParentExternalId", null, null,
                "The record names a parent the source did not include in this snapshot, so there is nothing to attach it to."));

        if (parents == ParentState.NotReconciledYet) return ReconcileDecision.Skip;

        if (profile is null || !profile.AppliesChanges) return ReconcileDecision.Skip;

        if (link.InternalId is not null && !link.NeedsReconciliation) return ReconcileDecision.Unchanged;

        return link.InternalId is null ? ReconcileDecision.Create : ReconcileDecision.Update;
    }

    // --- Per-kind mapping preconditions -------------------------------------------------------
    // Each returns null when the record can be mapped, or the conflict that stops it.

    /// <summary>
    /// Property needs two facts CanonicalProperty does not carry: the portfolio its constructor
    /// demands, and an IANA zone. The four address fields are NOT checked here — no PropFlow entity
    /// stores a postal address, so they are reported once per profile by MappingProfile.Validate()
    /// as Unmapped rather than once per record as a conflict. Raising them here would put four
    /// unfixable rows in the queue for every property in the feed.
    /// </summary>
    public static ConflictDetail? CheckProperty(CanonicalProperty record, MappingProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (profile?.TargetPortfolioId is null)
            return new ConflictDetail(ConflictReason.MissingRequiredMapping, "TargetPortfolioId", null, null,
                "Property requires a portfolio and the canonical record carries none. Set the mapping profile's target portfolio.");

        if (profile.ResolveTimeZone(record.TimeZone) is null)
            return new ConflictDetail(ConflictReason.MissingRequiredMapping, "TimeZone", record.TimeZone, null,
                "Property requires an IANA time zone. Add a time-zone rule for this value, or set the profile's default zone.");

        return null;
    }

    /// <summary>
    /// Space needs nothing from the profile; its only requirement is its parent property, which
    /// Decide handles. A building external id that PropFlow does not know is reported separately by
    /// the caller — the space still reconciles without it.
    /// </summary>
    public static ConflictDetail? CheckSpace(CanonicalSpace record, MappingProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(record);
        _ = profile;
        return null;
    }

    /// <summary>
    /// Occupancy needs a resident, and CanonicalOccupancy carries no resident external id — the
    /// reconciler synthesises one through SyntheticExternalId.ForResident, so there is no gap to
    /// report. A move-out that precedes move-in is the source contradicting itself, and Occupancy's
    /// own invariant would throw on it, so it is caught here as a mapping gap instead of being left
    /// to surface as a ValidationRefusal.
    /// </summary>
    public static ConflictDetail? CheckOccupancy(CanonicalOccupancy record, MappingProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(record);
        _ = profile;

        if (record.MovedOutOn is { } out_ && out_ < record.MovedInOn)
            return new ConflictDetail(ConflictReason.ValidationRefusal, "MovedOutOn",
                record.MovedOutOn?.ToString("O"), record.MovedInOn.ToString("O"),
                "The source reports a move-out date before the move-in date.");

        return null;
    }

    /// <summary>
    /// Work order needs a creator, which no external system can supply, and a status. The status is
    /// the case this whole mechanism exists for: CanonicalWorkOrder.Status is a bare string,
    /// WorkStatus is closed, and MapWorkStatus returns null rather than a default precisely so an
    /// unknown vendor status becomes a conflict instead of fabricated work state.
    /// </summary>
    public static ConflictDetail? CheckWorkOrder(CanonicalWorkOrder record, MappingProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (profile?.DefaultCreatorId is null)
            return new ConflictDetail(ConflictReason.MissingRequiredMapping, "DefaultCreatorId", null, null,
                "WorkItem requires a creator and an external work order has no PropFlow user. Set the mapping profile's default creator.");

        if (profile.MapWorkStatus(record.Status) is null)
            return new ConflictDetail(ConflictReason.UnmappedValue, "Status", record.Status, null,
                $"No mapping rule turns '{record.Status}' into a PropFlow work status. Add a rule; the reconciler will not guess one.");

        return null;
    }

    /// <summary>
    /// Asset distinguishes ABSENT from UNMAPPED, and conflating the two is the specific mistake
    /// this method exists to prevent. A null or blank CanonicalAsset.Kind means the source does not
    /// classify its assets, and AssetKind.Other is the honest representation of that. A non-blank
    /// value with no rule means the source DOES classify them and PropFlow does not understand the
    /// vocabulary — importing that as Other would silently discard information the source gave us.
    /// </summary>
    public static ConflictDetail? CheckAsset(CanonicalAsset record, MappingProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.Kind)) return null;

        if (profile is null || profile.MapAssetKind(record.Kind) is null)
            return new ConflictDetail(ConflictReason.UnmappedValue, "Kind", record.Kind, null,
                $"No mapping rule turns '{record.Kind}' into a PropFlow asset kind. Add a rule, or clear the value at the source to import it as Other.");

        return null;
    }

    /// <summary>
    /// A null or blank value from the source never erases a value a human put into PropFlow. The
    /// source is treated as additive: it can fill a field in and it can change one it previously
    /// filled in, but "the vendor does not track phone numbers" is not an instruction to delete the
    /// phone number an operator typed. Applied to every optional scalar the reconciler writes.
    /// </summary>
    public static string? PreferSource(string? fromSource, string? current) =>
        string.IsNullOrWhiteSpace(fromSource) ? current : fromSource.Trim();
}
