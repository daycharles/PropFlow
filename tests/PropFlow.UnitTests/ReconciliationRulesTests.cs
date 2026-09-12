using PropFlow.Application.Integrations;
using PropFlow.Domain.Integrations;
using Xunit;

namespace PropFlow.UnitTests;

// The reconciler's decision table, exercised without a database. PF-S19.10's replay assertions are
// written against exactly these rules, so each row here is a contract, not an implementation note.
public sealed class ReconciliationRulesTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Connection = Guid.NewGuid();
    private static readonly Guid Portfolio = Guid.NewGuid();
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Run = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static ExternalRecordLink Link(IntegrationEntityKind kind = IntegrationEntityKind.Property) =>
        new(Org, Guid.NewGuid(), Connection, kind, "EXT-1");

    private static ExternalRecordLink SeenLink(string hash = "hash-a")
    {
        var link = Link();
        link.Observe(hash, T0, Run);
        return link;
    }

    private static ExternalRecordLink ReconciledLink(string hash = "hash-a")
    {
        var link = SeenLink(hash);
        link.Reconcile(Guid.NewGuid(), hash, Run, T0);
        return link;
    }

    private static MappingProfile PropertyProfile(bool autoApply)
    {
        var profile = new MappingProfile(Org, Guid.NewGuid(), Connection, IntegrationEntityKind.Property, T0);
        profile.SetDefaults(Portfolio, null, "America/New_York", T0);
        if (autoApply) profile.PromoteToAutoApply(T0);
        return profile;
    }

    private static MappingProfile WorkOrderProfile(bool autoApply, params (string Source, string Target)[] rules)
    {
        var profile = new MappingProfile(Org, Guid.NewGuid(), Connection, IntegrationEntityKind.WorkOrder, T0);
        profile.SetDefaults(null, Creator, null, T0);
        foreach (var (source, target) in rules)
            profile.AddRule(new MappingRule(Org, Guid.NewGuid(), profile.Id,
                MappingSourceField.WorkOrderStatus, source, target), T0);
        if (autoApply) profile.PromoteToAutoApply(T0);
        return profile;
    }

    private static CanonicalProperty Property(string? timeZone = "America/New_York") =>
        new("EXT-1", "Cedar Court", "100 Cedar St", "Norfolk", "VA", "23510", timeZone);

    private static CanonicalWorkOrder WorkOrder(string status = "Open") =>
        new("EXT-1", "P-1", null, "Leaking faucet", null, status, T0, null);

    private static CanonicalAsset Asset(string? kind) =>
        new("EXT-1", "P-1", null, "Water heater", kind, null, null, null, null);

    // --- Decide: the sequence ------------------------------------------------------------------

    [Fact]
    public void A_mapping_gap_is_a_conflict_even_under_report_only()
    {
        var gap = new ConflictDetail(ConflictReason.UnmappedValue, "Status", "Awaiting Parts", null, "no rule");

        var decision = ReconciliationRules.Decide(SeenLink(), PropertyProfile(autoApply: false),
            ParentState.Resolved, gap);

        // Reporting is the entire point of ReportOnly, so step 1 precedes the mode check.
        Assert.Equal(ReconcileAction.Conflict, decision.Action);
        Assert.Same(gap, decision.Conflict);
    }

    [Fact]
    public void A_parent_missing_from_the_whole_snapshot_is_a_conflict()
    {
        var decision = ReconciliationRules.Decide(SeenLink(), PropertyProfile(autoApply: true),
            ParentState.MissingFromSnapshot, null);

        Assert.Equal(ReconcileAction.Conflict, decision.Action);
        Assert.Equal(ConflictReason.MissingParentLink, decision.Conflict!.Reason);
    }

    [Fact]
    public void A_parent_that_merely_did_not_reconcile_this_run_is_a_skip_not_a_conflict()
    {
        var decision = ReconciliationRules.Decide(SeenLink(), PropertyProfile(autoApply: true),
            ParentState.NotReconciledYet, null);

        // One missing portfolio id must not cascade into a queue entry for every child record.
        Assert.Equal(ReconcileAction.Skip, decision.Action);
        Assert.Null(decision.Conflict);
    }

    [Fact]
    public void A_report_only_profile_writes_nothing()
    {
        var decision = ReconciliationRules.Decide(SeenLink(), PropertyProfile(autoApply: false),
            ParentState.Resolved, null);

        Assert.Equal(ReconcileAction.Skip, decision.Action);
    }

    [Fact]
    public void A_missing_profile_writes_nothing()
    {
        var decision = ReconciliationRules.Decide(SeenLink(), null, ParentState.Resolved, null);

        Assert.Equal(ReconcileAction.Skip, decision.Action);
    }

    [Fact]
    public void An_auto_apply_profile_creates_when_there_is_no_linked_row()
    {
        var decision = ReconciliationRules.Decide(SeenLink(), PropertyProfile(autoApply: true),
            ParentState.Resolved, null);

        Assert.Equal(ReconcileAction.Create, decision.Action);
    }

    [Fact]
    public void An_auto_apply_profile_updates_when_the_source_moved_on()
    {
        var link = ReconciledLink("hash-a");
        link.Observe("hash-b", T0.AddHours(1), Run);

        var decision = ReconciliationRules.Decide(link, PropertyProfile(autoApply: true),
            ParentState.Resolved, null);

        Assert.Equal(ReconcileAction.Update, decision.Action);
    }

    [Fact]
    public void An_already_reconciled_unchanged_record_is_the_replay_safe_fast_path()
    {
        var decision = ReconciliationRules.Decide(ReconciledLink(), PropertyProfile(autoApply: true),
            ParentState.Resolved, null);

        Assert.Equal(ReconcileAction.Unchanged, decision.Action);
    }

    [Fact]
    public void A_crash_between_the_domain_write_and_the_hash_write_re_decides_update_on_the_next_run()
    {
        // Run 1 wrote the PropFlow row and died before Reconcile committed, so the link has an
        // InternalId from an earlier run but a ReconciledHash that is behind ContentHash.
        var link = ReconciledLink("hash-a");
        link.Observe("hash-b", T0.AddHours(1), Run);
        Assert.True(link.NeedsReconciliation);

        var decision = ReconciliationRules.Decide(link, PropertyProfile(autoApply: true),
            ParentState.Resolved, null);

        // Update, onto the SAME InternalId — which is what makes the replay converge.
        Assert.Equal(ReconcileAction.Update, decision.Action);
    }

    // --- Property preconditions ------------------------------------------------------------------

    [Fact]
    public void A_property_without_a_target_portfolio_is_a_conflict()
    {
        var bare = new MappingProfile(Org, Guid.NewGuid(), Connection, IntegrationEntityKind.Property, T0);

        var gap = ReconciliationRules.CheckProperty(Property(), bare);

        Assert.Equal(ConflictReason.MissingRequiredMapping, gap!.Reason);
        Assert.Equal("TargetPortfolioId", gap.Field);
    }

    [Fact]
    public void A_property_with_no_resolvable_time_zone_is_a_conflict()
    {
        var profile = new MappingProfile(Org, Guid.NewGuid(), Connection, IntegrationEntityKind.Property, T0);
        profile.SetDefaults(Portfolio, null, null, T0);

        var gap = ReconciliationRules.CheckProperty(Property("Central Standard Time"), profile);

        Assert.Equal("TimeZone", gap!.Field);
        Assert.Equal("Central Standard Time", gap.ObservedValue);
    }

    [Fact]
    public void A_fully_configured_property_profile_has_no_gap_and_address_fields_raise_nothing()
    {
        // The four address fields have nowhere to go, but they are reported once per profile by
        // MappingProfile.Validate(), not once per record here — four unfixable queue rows per
        // property would drown the queue.
        Assert.Null(ReconciliationRules.CheckProperty(Property(), PropertyProfile(autoApply: true)));
    }

    // --- Work order preconditions -----------------------------------------------------------------

    [Fact]
    public void A_work_order_without_a_default_creator_is_a_conflict()
    {
        var bare = new MappingProfile(Org, Guid.NewGuid(), Connection, IntegrationEntityKind.WorkOrder, T0);

        var gap = ReconciliationRules.CheckWorkOrder(WorkOrder(), bare);

        Assert.Equal("DefaultCreatorId", gap!.Field);
    }

    [Fact]
    public void An_unmapped_work_order_status_is_a_conflict_and_never_a_default()
    {
        var profile = WorkOrderProfile(autoApply: false, ("Open", "New"));

        var gap = ReconciliationRules.CheckWorkOrder(WorkOrder("Awaiting Parts"), profile);

        Assert.Equal(ConflictReason.UnmappedValue, gap!.Reason);
        Assert.Equal("Status", gap.Field);
        Assert.Equal("Awaiting Parts", gap.ObservedValue);
    }

    [Fact]
    public void A_mapped_work_order_status_has_no_gap()
    {
        Assert.Null(ReconciliationRules.CheckWorkOrder(WorkOrder("Open"),
            WorkOrderProfile(autoApply: true, ("Open", "New"))));
    }

    // --- Asset: absent is not unmapped --------------------------------------------------------------

    [Fact]
    public void An_absent_asset_kind_is_not_a_conflict_and_falls_back_to_other()
    {
        var profile = new MappingProfile(Org, Guid.NewGuid(), Connection, IntegrationEntityKind.Asset, T0);

        Assert.Null(ReconciliationRules.CheckAsset(Asset(null), profile));
        Assert.Null(ReconciliationRules.CheckAsset(Asset("   "), profile));
        // Even with no profile at all: the source simply does not classify its assets.
        Assert.Null(ReconciliationRules.CheckAsset(Asset(null), null));
    }

    [Fact]
    public void A_present_but_unmapped_asset_kind_is_a_conflict()
    {
        var profile = new MappingProfile(Org, Guid.NewGuid(), Connection, IntegrationEntityKind.Asset, T0);
        profile.AddRule(new MappingRule(Org, Guid.NewGuid(), profile.Id,
            MappingSourceField.AssetKind, "furnace", "Hvac"), T0);

        // The source DOES classify, and PropFlow does not understand its vocabulary. Degrading this
        // to Other would silently discard information the source gave us.
        var gap = ReconciliationRules.CheckAsset(Asset("Dumbwaiter"), profile);

        Assert.Equal(ConflictReason.UnmappedValue, gap!.Reason);
        Assert.Equal("Kind", gap.Field);
        Assert.Null(ReconciliationRules.CheckAsset(Asset("FURNACE"), profile));
    }

    [Fact]
    public void A_present_asset_kind_with_no_profile_at_all_is_a_conflict()
    {
        Assert.NotNull(ReconciliationRules.CheckAsset(Asset("WaterHeater"), null));
    }

    // --- Occupancy ---------------------------------------------------------------------------------

    [Fact]
    public void An_occupancy_that_ends_before_it_starts_is_a_conflict_rather_than_a_thrown_invariant()
    {
        var record = new CanonicalOccupancy("EXT-1", "S-1", "Alex Turner", null, null,
            new DateOnly(2026, 3, 1), new DateOnly(2026, 1, 1));

        var gap = ReconciliationRules.CheckOccupancy(record, null);

        Assert.Equal(ConflictReason.ValidationRefusal, gap!.Reason);
        Assert.Equal("MovedOutOn", gap.Field);
    }

    [Fact]
    public void A_normal_occupancy_has_no_gap_because_the_resident_id_is_synthesised()
    {
        var record = new CanonicalOccupancy("EXT-1", "S-1", "Alex Turner", "a@example.test", null,
            new DateOnly(2026, 3, 1), null);

        Assert.Null(ReconciliationRules.CheckOccupancy(record, null));
        Assert.Null(ReconciliationRules.CheckSpace(new CanonicalSpace("EXT-1", "P-1", "101", null), null));
    }

    // --- The additive rule ---------------------------------------------------------------------------

    [Fact]
    public void A_null_or_blank_source_value_never_erases_what_propflow_already_has()
    {
        Assert.Equal("+15550100101", ReconciliationRules.PreferSource(null, "+15550100101"));
        Assert.Equal("+15550100101", ReconciliationRules.PreferSource("   ", "+15550100101"));
        Assert.Equal("+15550100202", ReconciliationRules.PreferSource("+15550100202", "+15550100101"));
        Assert.Equal("+15550100202", ReconciliationRules.PreferSource("  +15550100202 ", null));
        Assert.Null(ReconciliationRules.PreferSource(null, null));
    }
}
