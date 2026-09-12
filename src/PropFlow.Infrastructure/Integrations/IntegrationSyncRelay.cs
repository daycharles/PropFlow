using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PropFlow.Application.Integrations;
using PropFlow.Domain.Integrations;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Infrastructure.Integrations;

// Syncs every organization's due connections, each on its own tenant context using the restricted
// runtime role. Separated from the hosted service so integration tests can drive it directly
// instead of waiting on a timer — the same split as OutboxRelay / OutboxDispatcher, and for the
// same reason (.claude/rules/traps.md: a background timer racing an assertion is flakiness this
// repo has already paid for once).
//
// Two sweeps per organization, in this order:
//
//   1. Reclaim. A Running SyncRun whose heartbeat has gone quiet past StaleRunTimeout belonged to
//      a worker that died mid-pull. It holds the IX_SyncRuns_ActiveClaim row, so until it is
//      failed the connection can never sync again. Reclaiming is what makes a crashed process
//      recoverable WITHOUT a human clicking anything.
//   2. Attempt. Connections whose backoff and interval say they are due.
public sealed class IntegrationSyncRelay(
    IConfiguration configuration,
    IIntegrationCatalog catalog,
    TimeProvider clock,
    IntegrationSyncOptions options,
    ILogger<IntegrationSyncRelay> logger)
{
    public IntegrationSyncOptions Options => options;

    public async Task<int> SyncDueAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("Integration sync skipped: no Database connection is configured.");
            return 0;
        }

        List<Guid> organizations;
        await using (var identity = DatabaseProvisioner.CreateIdentityStore(connectionString))
        {
            organizations = await identity.Organizations.AsNoTracking()
                .Select(x => x.Id).ToListAsync(cancellationToken);
        }

        var synced = 0;
        foreach (var organization in organizations)
            synced += await SyncOrganizationAsync(connectionString, organization, cancellationToken);
        return synced;
    }

    private async Task<int> SyncOrganizationAsync(string connectionString, Guid organization,
        CancellationToken cancellationToken)
    {
        await using var store = DatabaseProvisioner.CreateIntegrationStore(connectionString, organization);
        await ReclaimStaleRunsAsync(store, organization, cancellationToken);

        var now = clock.GetUtcNow();
        var due = await store.Connections.AsNoTracking().ToListAsync(cancellationToken);
        var synced = 0;

        foreach (var connection in due)
        {
            if (!options.ShouldAttempt(connection.IsEnabled, connection.ConsecutiveFailures,
                    connection.LastAttemptedAt, connection.LastSucceededAt, now))
                continue;

            // A fresh pair of contexts per connection: EfIntegrationOperations tracks entities, and
            // one failing connection must not poison the next one's change tracker.
            await using var scoped = DatabaseProvisioner.CreateIntegrationStore(connectionString, organization);
            await using var operations = DatabaseProvisioner.CreateOperationsStore(connectionString, organization);
            var reconciler = new EfIntegrationReconciler(scoped, operations, clock);
            var sync = new EfIntegrationOperations(scoped, catalog, clock, reconciler);

            // AttemptNumber is the connection's consecutive-failure depth plus one, so a SyncRun row
            // says on its face whether it was a first try or the fourth attempt at a failing
            // provider. This is the column earning its place: without it, "attempt 4 of an outage"
            // and "the first run after a fix" are indistinguishable in run history, because
            // ConsecutiveFailures is current state that gets reset the moment one run succeeds.
            var attempt = connection.ConsecutiveFailures + 1;
            var trigger = connection.ConsecutiveFailures > 0 ? SyncTrigger.Retry : SyncTrigger.Scheduled;

            SyncReport report;
            try
            {
                report = await sync.SyncAsync(connection.Id, trigger, attempt, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One connection's failure must not stop the rest of the organization.
                logger.LogError(exception, "Integration sync failed for connection {Connection}.", connection.Id);
                continue;
            }

            switch (report.Outcome)
            {
                case SyncOutcome.Completed:
                    synced++;
                    logger.LogInformation(
                        "Synced connection {Connection}: {Seen} seen, {Added} added, {Updated} updated, {Conflicted} conflicted, {Retired} retired.",
                        connection.Id, report.Seen, report.Added, report.Updated, report.Conflicted, report.Retired);
                    break;
                case SyncOutcome.AlreadyRunning:
                    // Another dispatcher holds the claim. Expected under more than one instance,
                    // and the correct outcome rather than an error.
                    break;
                case SyncOutcome.Failed:
                    logger.LogWarning("Connection {Connection} failed to sync: {Error}", connection.Id, report.Error);
                    break;
                default:
                    break;
            }
        }

        return synced;
    }

    // A worker that died mid-pull leaves a Running row holding the connection's claim. Nothing else
    // can release it, so without this sweep one crash disables a connection permanently and the
    // only recovery is a human noticing. SyncRun.IsReclaimable is the pure predicate and reads
    // HeartbeatAt, so a slow-but-progressing run is not reclaimed out from under itself.
    private async Task ReclaimStaleRunsAsync(IntegrationStore store, Guid organization, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var cutoff = now - options.StaleRunTimeout;
        var candidates = await store.SyncRuns
            .Where(x => x.Status == SyncRunStatus.Running && x.HeartbeatAt <= cutoff)
            .ToListAsync(ct);

        var reclaimed = 0;
        foreach (var run in candidates)
        {
            if (!run.IsReclaimable(now, options.StaleRunTimeout)) continue;
            run.Reclaim(now, options.StaleRunTimeout);
            reclaimed++;
        }

        if (reclaimed == 0) return;

        try
        {
            await store.SaveChangesAsync(ct);
            logger.LogWarning("Reclaimed {Count} stale sync run(s) for organization {Organization}.",
                reclaimed, organization);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The original worker came back and finished the run between the read and the save.
            // Its answer is better than ours, so drop the reclaim and move on.
            store.ChangeTracker.Clear();
        }
    }
}
