using PropFlow.Domain.Integrations;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class SyncRunTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Connection = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(10);

    private static SyncRun Begin(SyncTrigger trigger = SyncTrigger.Manual, int attempt = 1) =>
        SyncRun.Begin(Org, Guid.NewGuid(), Connection, trigger, T0, attempt);

    [Fact]
    public void A_begun_run_is_running_with_zero_counters_and_no_snapshot()
    {
        var run = Begin(SyncTrigger.Scheduled);

        Assert.Equal(SyncRunStatus.Running, run.Status);
        Assert.Equal(SyncTrigger.Scheduled, run.Trigger);
        Assert.Equal(1, run.AttemptNumber);
        Assert.Equal(T0, run.StartedAt);
        Assert.Equal(T0, run.HeartbeatAt);
        Assert.Null(run.CompletedAt);
        Assert.Null(run.SnapshotHash);
        Assert.Equal(SyncCounts.Zero, run.Counts);
        Assert.False(run.IsFinished);
    }

    [Fact]
    public void Begin_requires_a_connection_a_defined_trigger_and_a_positive_attempt()
    {
        Assert.Throws<ArgumentException>(() => SyncRun.Begin(Org, Guid.NewGuid(), Guid.Empty, SyncTrigger.Manual, T0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SyncRun.Begin(Org, Guid.NewGuid(), Connection, (SyncTrigger)99, T0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SyncRun.Begin(Org, Guid.NewGuid(), Connection, SyncTrigger.Retry, T0, 0));
    }

    [Fact]
    public void Completing_records_the_counters_and_the_snapshot_hash()
    {
        var run = Begin();
        run.Complete(new SyncCounts(10, 3, 2, 1, 4), new string('a', 64), T0.AddMinutes(2));

        Assert.Equal(SyncRunStatus.Completed, run.Status);
        Assert.Equal(new SyncCounts(10, 3, 2, 1, 4), run.Counts);
        Assert.Equal(new string('a', 64), run.SnapshotHash);
        Assert.Equal(T0.AddMinutes(2), run.CompletedAt);
        Assert.True(run.IsFinished);
        Assert.Null(run.Error);
    }

    [Fact]
    public void A_failed_run_keeps_the_work_it_managed_before_it_died()
    {
        var run = Begin();
        run.Fail("the provider returned 503", new SyncCounts(10, 3, 0, 7, 0), T0.AddMinutes(1));

        Assert.Equal(SyncRunStatus.Failed, run.Status);
        Assert.Equal("the provider returned 503", run.Error);
        Assert.Equal(3, run.Added);
        Assert.Equal(7, run.Failed);
    }

    [Fact]
    public void A_finished_run_cannot_be_finished_twice()
    {
        var run = Begin();
        run.Complete(SyncCounts.Zero, null, T0.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => run.Complete(SyncCounts.Zero, null, T0.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => run.Fail("late", SyncCounts.Zero, T0.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => run.Heartbeat(T0.AddMinutes(2)));
    }

    [Fact]
    public void A_run_cannot_finish_before_it_started_and_counters_cannot_be_negative()
    {
        Assert.Throws<ArgumentException>(() => Begin().Complete(SyncCounts.Zero, null, T0.AddMinutes(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Begin().Complete(new SyncCounts(1, 0, 0, -1, 0), null, T0));
    }

    // --- Reclaiming a stale run ---------------------------------------------------------------

    [Fact]
    public void A_running_run_is_not_reclaimable_before_the_timeout()
    {
        var run = Begin();

        Assert.False(run.IsReclaimable(T0, Stale));
        Assert.False(run.IsReclaimable(T0 + Stale - TimeSpan.FromTicks(1), Stale));
    }

    [Fact]
    public void A_running_run_is_reclaimable_exactly_at_the_timeout_boundary()
    {
        var run = Begin();

        // Inclusive, the same boundary as OutboxMessage.IsClaimable.
        Assert.True(run.IsReclaimable(T0 + Stale, Stale));
        Assert.True(run.IsReclaimable(T0 + Stale + TimeSpan.FromSeconds(1), Stale));
    }

    [Fact]
    public void A_heartbeat_pushes_the_reclaim_boundary_out()
    {
        var run = Begin();
        run.Heartbeat(T0.AddMinutes(9));

        Assert.False(run.IsReclaimable(T0 + Stale, Stale));
        Assert.True(run.IsReclaimable(T0.AddMinutes(19), Stale));
    }

    [Fact]
    public void A_heartbeat_never_moves_backwards()
    {
        var run = Begin();
        run.Heartbeat(T0.AddMinutes(5));
        run.Heartbeat(T0.AddMinutes(1));

        Assert.Equal(T0.AddMinutes(5), run.HeartbeatAt);
    }

    [Fact]
    public void A_finished_run_is_never_reclaimable()
    {
        var run = Begin();
        run.Complete(SyncCounts.Zero, null, T0.AddMinutes(1));

        Assert.False(run.IsReclaimable(T0.AddDays(1), Stale));
    }

    [Fact]
    public void Reclaiming_fails_the_run_with_the_reclaim_message()
    {
        var run = Begin();
        run.Reclaim(T0 + Stale, Stale);

        Assert.Equal(SyncRunStatus.Failed, run.Status);
        Assert.Equal(SyncRun.ReclaimedError, run.Error);
        Assert.Equal(T0 + Stale, run.CompletedAt);
    }

    [Fact]
    public void Reclaiming_a_run_that_is_not_stale_is_refused()
    {
        var run = Begin();

        Assert.Throws<InvalidOperationException>(() => run.Reclaim(T0.AddMinutes(1), Stale));
    }

    [Fact]
    public void A_negative_stale_timeout_is_rejected()
    {
        var run = Begin();

        Assert.Throws<ArgumentOutOfRangeException>(() => run.IsReclaimable(T0, TimeSpan.FromMinutes(-1)));
    }
}
