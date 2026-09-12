using PropFlow.Application.Integrations;
using Xunit;

namespace PropFlow.UnitTests;

// The scheduler's policy, which is pure so the whole of it is testable without a database or a
// real clock: when a connection is due, how far a failing one backs off, and when it is given up on.
public sealed class IntegrationSyncOptionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static IntegrationSyncOptions Options() => new()
    {
        SyncInterval = TimeSpan.FromMinutes(15),
        RetryDelay = TimeSpan.FromMinutes(2),
        MaxRetryDelay = TimeSpan.FromHours(4),
        MaxConsecutiveFailures = 10
    };

    // --- Defaults and validation ------------------------------------------------------------

    [Fact]
    public void The_defaults_validate()
    {
        new IntegrationSyncOptions().Validate();
    }

    [Fact]
    public void Nonsense_configuration_is_refused_at_startup()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new IntegrationSyncOptions { PollInterval = TimeSpan.Zero }.Validate());
        Assert.Throws<InvalidOperationException>(() =>
            new IntegrationSyncOptions { StaleRunTimeout = TimeSpan.Zero }.Validate());
        Assert.Throws<InvalidOperationException>(() =>
            new IntegrationSyncOptions { MaxConsecutiveFailures = 0 }.Validate());
        // A ceiling below the floor would make the backoff shrink with each failure.
        Assert.Throws<InvalidOperationException>(() => new IntegrationSyncOptions
        {
            RetryDelay = TimeSpan.FromMinutes(10), MaxRetryDelay = TimeSpan.FromMinutes(1)
        }.Validate());
    }

    // --- Backoff ------------------------------------------------------------------------------

    [Fact]
    public void Backoff_doubles_per_consecutive_failure()
    {
        var options = Options();

        Assert.Equal(TimeSpan.FromMinutes(2), options.BackoffFor(0));
        Assert.Equal(TimeSpan.FromMinutes(2), options.BackoffFor(1));
        Assert.Equal(TimeSpan.FromMinutes(4), options.BackoffFor(2));
        Assert.Equal(TimeSpan.FromMinutes(8), options.BackoffFor(3));
        Assert.Equal(TimeSpan.FromMinutes(16), options.BackoffFor(4));
    }

    [Fact]
    public void Backoff_saturates_at_the_ceiling_rather_than_overflowing()
    {
        var options = Options();

        Assert.Equal(TimeSpan.FromHours(4), options.BackoffFor(10));
        Assert.Equal(TimeSpan.FromHours(4), options.BackoffFor(100));
        // The shift is capped before it is applied, so an absurd failure count cannot wrap the
        // tick arithmetic into a negative (and therefore immediate) delay.
        Assert.Equal(TimeSpan.FromHours(4), options.BackoffFor(int.MaxValue));
    }

    // --- Scheduling -----------------------------------------------------------------------------

    [Fact]
    public void A_disabled_connection_is_never_attempted()
    {
        Assert.False(Options().ShouldAttempt(isEnabled: false, 0, null, null, Now));
    }

    [Fact]
    public void A_connection_that_never_succeeded_is_attempted_immediately()
    {
        Assert.True(Options().ShouldAttempt(isEnabled: true, 0, null, null, Now));
    }

    [Fact]
    public void A_healthy_connection_waits_out_the_sync_interval_from_its_last_success()
    {
        var options = Options();

        Assert.False(options.ShouldAttempt(true, 0, Now.AddMinutes(-5), Now.AddMinutes(-5), Now));
        Assert.False(options.ShouldAttempt(true, 0, Now.AddMinutes(-14), Now.AddMinutes(-14), Now));
        Assert.True(options.ShouldAttempt(true, 0, Now.AddMinutes(-15), Now.AddMinutes(-15), Now));
        Assert.True(options.ShouldAttempt(true, 0, Now.AddHours(-1), Now.AddHours(-1), Now));
    }

    [Fact]
    public void A_failing_connection_backs_off_from_its_last_attempt_not_its_last_success()
    {
        var options = Options();
        // Succeeded an hour ago, has failed three times since: the third failure means an 8-minute
        // backoff, measured from the attempt. Using the success would let a failing provider be
        // hammered every tick.
        var lastSuccess = Now.AddHours(-1);

        Assert.False(options.ShouldAttempt(true, 3, Now.AddMinutes(-7), lastSuccess, Now));
        Assert.True(options.ShouldAttempt(true, 3, Now.AddMinutes(-8), lastSuccess, Now));
    }

    [Fact]
    public void The_backoff_lengthens_as_an_outage_continues()
    {
        var options = Options();
        var attempted = Now.AddMinutes(-10);

        // Ten minutes since the last attempt is enough at 3 failures (8 min) but not at 5 (32 min).
        Assert.True(options.ShouldAttempt(true, 3, attempted, null, Now));
        Assert.False(options.ShouldAttempt(true, 5, attempted, null, Now));
    }

    [Fact]
    public void A_connection_that_has_failed_too_many_times_is_given_up_on_by_the_scheduler()
    {
        var options = Options();

        Assert.True(options.ShouldAttempt(true, 9, Now.AddDays(-1), null, Now));
        // Not disabled and nothing deleted — a manual sync still runs, and one success clears the
        // counter. It simply stops generating traffic and starts being visible on the health screen.
        Assert.False(options.ShouldAttempt(true, 10, Now.AddDays(-1), null, Now));
        Assert.False(options.ShouldAttempt(true, 50, Now.AddDays(-1), null, Now));
    }
}
