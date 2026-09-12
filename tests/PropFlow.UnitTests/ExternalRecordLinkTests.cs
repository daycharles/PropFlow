using PropFlow.Domain.Integrations;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class ExternalRecordLinkTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Connection = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static ExternalRecordLink New() =>
        new(Org, Guid.NewGuid(), Connection, IntegrationEntityKind.Property, "EXT-1");

    [Fact]
    public void A_new_link_is_pending_with_no_hash()
    {
        var link = New();
        Assert.Equal(SyncState.Pending, link.SyncState);
        Assert.Null(link.ContentHash);
        Assert.Null(link.LastSeenAt);
    }

    [Fact]
    public void Requires_a_connection_and_an_external_id()
    {
        Assert.Throws<ArgumentException>(() => new ExternalRecordLink(Org, Guid.NewGuid(), Guid.Empty, IntegrationEntityKind.Asset, "x"));
        Assert.Throws<ArgumentException>(() => new ExternalRecordLink(Org, Guid.NewGuid(), Connection, IntegrationEntityKind.Asset, "  "));
    }

    [Fact]
    public void First_observation_counts_as_changed_and_marks_it_synced()
    {
        var link = New();
        var changed = link.Observe("hash-a", T0);

        Assert.True(changed);
        Assert.Equal(SyncState.Synced, link.SyncState);
        Assert.Equal("hash-a", link.ContentHash);
        Assert.Equal(T0, link.LastSeenAt);
    }

    [Fact]
    public void Re_observing_the_same_hash_is_not_a_change_but_still_updates_last_seen()
    {
        var link = New();
        link.Observe("hash-a", T0);
        var changed = link.Observe("hash-a", T0.AddHours(1));

        Assert.False(changed);
        Assert.Equal(T0.AddHours(1), link.LastSeenAt);
    }

    [Fact]
    public void A_different_hash_is_a_change()
    {
        var link = New();
        link.Observe("hash-a", T0);
        Assert.True(link.Observe("hash-b", T0.AddHours(1)));
        Assert.Equal("hash-b", link.ContentHash);
    }

    [Fact]
    public void Marking_failed_sets_the_state_and_error_and_clears_on_next_clean_observe()
    {
        var link = New();
        link.MarkFailed("mapping blew up", T0);
        Assert.Equal(SyncState.Failed, link.SyncState);
        Assert.Equal("mapping blew up", link.LastError);

        link.Observe("hash-a", T0.AddHours(1));
        Assert.Equal(SyncState.Synced, link.SyncState);
        Assert.Null(link.LastError);
    }

    // --- PF-S19.03: reconciliation ------------------------------------------------------------

    private static readonly Guid Run1 = Guid.NewGuid();
    private static readonly Guid Run2 = Guid.NewGuid();
    private static readonly Guid Internal = Guid.NewGuid();

    [Fact]
    public void A_link_with_no_reconciliation_always_needs_one()
    {
        var link = New();
        Assert.True(link.NeedsReconciliation);
        Assert.Null(link.InternalId);
        Assert.Null(link.ReconciledHash);

        link.Observe("hash-a", T0);
        Assert.True(link.NeedsReconciliation);
    }

    [Fact]
    public void Reconciling_records_the_internal_row_and_clears_the_need()
    {
        var link = New();
        link.Observe("hash-a", T0, Run1);
        link.Reconcile(Internal, "hash-a", Run1, T0.AddSeconds(1));

        Assert.Equal(Internal, link.InternalId);
        Assert.Equal("hash-a", link.ReconciledHash);
        Assert.Equal("hash-a", link.ContentHash);
        Assert.Equal(Run1, link.LastRunId);
        Assert.Equal(T0.AddSeconds(1), link.LastReconciledAt);
        Assert.Equal(SyncState.Synced, link.SyncState);
        Assert.False(link.NeedsReconciliation);
    }

    [Fact]
    public void A_new_sighting_puts_the_link_behind_again_without_losing_the_internal_row()
    {
        var link = New();
        link.Observe("hash-a", T0, Run1);
        link.Reconcile(Internal, "hash-a", Run1, T0);

        link.Observe("hash-b", T0.AddHours(1), Run2);

        Assert.True(link.NeedsReconciliation);
        Assert.Equal(Internal, link.InternalId);
        Assert.Equal("hash-a", link.ReconciledHash);
    }

    [Fact]
    public void A_crash_between_the_domain_write_and_the_hash_write_replays_onto_the_same_row()
    {
        var link = New();
        link.Observe("hash-b", T0, Run1);
        // Run 1 wrote the PropFlow row and died before Reconcile committed: ReconciledHash is
        // still behind, so run 2 re-decides "update" and re-applies to the SAME internal id.
        Assert.True(link.NeedsReconciliation);

        link.Observe("hash-b", T0.AddHours(1), Run2);
        link.Reconcile(Internal, "hash-b", Run2, T0.AddHours(1));

        Assert.Equal(Internal, link.InternalId);
        Assert.False(link.NeedsReconciliation);
    }

    [Fact]
    public void A_link_cannot_be_rebound_to_a_different_internal_row()
    {
        var link = New();
        link.Observe("hash-a", T0, Run1);
        link.Reconcile(Internal, "hash-a", Run1, T0);

        Assert.Throws<InvalidOperationException>(() =>
            link.Reconcile(Guid.NewGuid(), "hash-a", Run2, T0.AddHours(1)));
    }

    [Fact]
    public void Reconciling_requires_an_internal_id_and_a_run()
    {
        var link = New();
        Assert.Throws<ArgumentException>(() => link.Reconcile(Guid.Empty, "hash-a", Run1, T0));
        Assert.Throws<ArgumentException>(() => link.Reconcile(Internal, "hash-a", Guid.Empty, T0));
    }

    [Fact]
    public void Marking_conflicted_keeps_the_internal_row_and_the_reconciled_hash()
    {
        var link = New();
        link.Observe("hash-a", T0, Run1);
        link.Reconcile(Internal, "hash-a", Run1, T0);
        link.Observe("hash-b", T0.AddHours(1), Run2);
        link.MarkConflicted("status 'Awaiting Parts' is unmapped", T0.AddHours(1), Run2);

        Assert.Equal(SyncState.Conflicted, link.SyncState);
        Assert.Equal(Internal, link.InternalId);
        Assert.Equal("hash-a", link.ReconciledHash);
        Assert.Equal(Run2, link.LastRunId);
    }

    [Fact]
    public void Retiring_is_not_a_delete_and_a_later_sighting_brings_the_link_back()
    {
        var link = New();
        link.Observe("hash-a", T0, Run1);
        link.Reconcile(Internal, "hash-a", Run1, T0);

        link.Retire(Run2, T0.AddDays(1));
        Assert.Equal(SyncState.Retired, link.SyncState);
        Assert.Equal(T0.AddDays(1), link.RetiredAt);
        Assert.Equal(Run2, link.LastRunId);
        Assert.Null(link.RetiredByUserId);
        Assert.Equal(Internal, link.InternalId);

        link.Observe("hash-a", T0.AddDays(2), Run2);
        Assert.Equal(SyncState.Synced, link.SyncState);
        Assert.Equal(Internal, link.InternalId);
        Assert.False(link.NeedsReconciliation);
    }

    [Fact]
    public void Retiring_requires_a_run_and_a_manual_retire_requires_an_actor()
    {
        Assert.Throws<ArgumentException>(() => New().Retire(Guid.Empty, T0));
        Assert.Throws<ArgumentException>(() => New().RetireManually(Guid.Empty, T0));
    }

    [Fact]
    public void A_manual_retire_records_the_actor_instead_of_a_run()
    {
        var link = New();
        link.Observe("hash-a", T0, Run1);
        var actor = Guid.NewGuid();

        link.RetireManually(actor, T0.AddDays(1));

        Assert.Equal(SyncState.Retired, link.SyncState);
        Assert.Equal(actor, link.RetiredByUserId);
        Assert.Equal(T0.AddDays(1), link.RetiredAt);
        // The run that last actually touched the record, not a fabricated one.
        Assert.Equal(Run1, link.LastRunId);
    }

    [Fact]
    public void The_synthetic_resident_id_is_derived_reversible_and_storable()
    {
        var residentId = SyntheticExternalId.ForResident("OCC-1");

        Assert.Equal("OCC-1#resident", residentId);
        Assert.Equal(residentId, SyntheticExternalId.ForResident("OCC-1"));
        Assert.True(SyntheticExternalId.IsSyntheticResident(residentId));
        Assert.Equal("OCC-1", SyntheticExternalId.OccupancyOf(residentId));
        Assert.False(SyntheticExternalId.IsSyntheticResident("OCC-1"));
        Assert.Null(SyntheticExternalId.OccupancyOf("OCC-1"));

        // The longest occupancy id still yields a link the column can hold.
        var longest = new string('o', SyntheticExternalId.MaxSourceLength);
        var link = new ExternalRecordLink(Org, Guid.NewGuid(), Connection, IntegrationEntityKind.Resident,
            SyntheticExternalId.ForResident(longest));
        Assert.Equal(ExternalRecordLink.ExternalIdMaxLength, link.ExternalId.Length);
    }

    [Fact]
    public void An_occupancy_id_too_long_to_extend_is_refused_rather_than_silently_truncated()
    {
        var tooLong = new string('o', SyntheticExternalId.MaxSourceLength + 1);

        Assert.Throws<ArgumentException>(() => SyntheticExternalId.ForResident(tooLong));
    }
}
