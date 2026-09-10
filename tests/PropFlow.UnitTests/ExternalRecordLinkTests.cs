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
}
