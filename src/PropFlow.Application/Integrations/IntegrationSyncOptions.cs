namespace PropFlow.Application.Integrations;

// Bound from the "IntegrationSync" configuration section, on the CommunicationsOptions template.
// Defaults suit a single instance with the mock and sandbox adapters; operators tune them without
// a redeploy.
public sealed class IntegrationSyncOptions
{
    public const string SectionName = "IntegrationSync";

    // How often the dispatcher wakes up. It is not how often a connection syncs — that is
    // SyncInterval, checked per connection on each tick.
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);

    // Minimum gap between two successful syncs of the same connection. Pulls are whole-source
    // snapshots (no adapter sets SupportsIncrementalPull), so this is deliberately not seconds.
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromMinutes(15);

    // How long a Running run may go without a heartbeat before another dispatcher writes it off.
    // Feeds SyncRun.IsReclaimable. Must comfortably exceed the longest expected pull: reclaiming a
    // run that is merely slow releases the claim while the original is still writing.
    public TimeSpan StaleRunTimeout { get; set; } = TimeSpan.FromMinutes(15);

    // Backoff after a failure, doubling per consecutive failure up to MaxRetryDelay. A provider
    // outage must not turn into a request every PollInterval for as long as it lasts.
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(4);

    // Consecutive failures before the scheduler stops attempting a connection. It is not disabled
    // and nothing is deleted: a manual sync still runs, and one success clears the counter. The
    // point is that a connection whose credential was revoked stops generating traffic and starts
    // being visible on the health screen instead.
    public int MaxConsecutiveFailures { get; set; } = 10;

    public void Validate()
    {
        if (PollInterval <= TimeSpan.Zero) throw new InvalidOperationException("IntegrationSync:PollInterval must be positive.");
        if (SyncInterval < TimeSpan.Zero) throw new InvalidOperationException("IntegrationSync:SyncInterval cannot be negative.");
        if (StaleRunTimeout <= TimeSpan.Zero) throw new InvalidOperationException("IntegrationSync:StaleRunTimeout must be positive.");
        if (RetryDelay <= TimeSpan.Zero) throw new InvalidOperationException("IntegrationSync:RetryDelay must be positive.");
        if (MaxRetryDelay < RetryDelay) throw new InvalidOperationException("IntegrationSync:MaxRetryDelay cannot be below RetryDelay.");
        if (MaxConsecutiveFailures < 1) throw new InvalidOperationException("IntegrationSync:MaxConsecutiveFailures must be at least 1.");
    }

    /// <summary>
    /// How long to wait after <paramref name="consecutiveFailures"/> failures in a row. Exponential
    /// from RetryDelay, capped at MaxRetryDelay. Pure, so the boundary is unit-testable: zero
    /// failures is RetryDelay, and the doubling saturates rather than overflowing.
    /// </summary>
    public TimeSpan BackoffFor(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0) return RetryDelay;
        // Cap the shift before it is applied: 1 << 62 already exceeds any sane MaxRetryDelay, and
        // shifting past 63 is undefined rather than saturating.
        var doublings = Math.Min(consecutiveFailures - 1, 32);
        var ticks = RetryDelay.Ticks * (1L << doublings);
        return ticks <= 0 || ticks > MaxRetryDelay.Ticks ? MaxRetryDelay : TimeSpan.FromTicks(ticks);
    }

    /// <summary>
    /// Whether the scheduler should attempt this connection now. Pure — everything it needs is
    /// already on the connection's health counters, which is what lets the whole policy be tested
    /// without a database or a clock.
    /// </summary>
    public bool ShouldAttempt(bool isEnabled, int consecutiveFailures, DateTimeOffset? lastAttemptedAt,
        DateTimeOffset? lastSucceededAt, DateTimeOffset now)
    {
        if (!isEnabled) return false;
        if (consecutiveFailures >= MaxConsecutiveFailures) return false;

        if (consecutiveFailures > 0)
            // Failing: wait out the backoff from the last attempt, not the last success.
            return lastAttemptedAt is not { } attempted || attempted <= now - BackoffFor(consecutiveFailures);

        // Healthy: wait out the ordinary interval from the last success.
        return lastSucceededAt is not { } succeeded || succeeded <= now - SyncInterval;
    }
}
