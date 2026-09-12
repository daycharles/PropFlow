using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropFlow.Application.Integrations;

namespace PropFlow.Infrastructure.Integrations;

// Runs the integration sync relay on a fixed interval. Scheduling and per-connection iteration
// live in IntegrationSyncRelay; this type owns only the schedule and failure isolation. Modelled
// line for line on OutboxDispatcher.
//
// Registered in Program.cs under `if (!builder.Environment.IsEnvironment("Testing"))`, exactly as
// the outbox poller is, and for the reason .claude/rules/traps.md records: integration tests drive
// the relay directly so a background timer cannot race an assertion. That was audited and closed
// once already (docs/audit-communications.md, C1). Do not "restore" a registration here.
public sealed class IntegrationSyncDispatcher(
    IntegrationSyncRelay relay,
    IntegrationSyncOptions options,
    ILogger<IntegrationSyncDispatcher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PollInterval);
        do
        {
            try
            {
                await relay.SyncDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Integration sync cycle failed.");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
