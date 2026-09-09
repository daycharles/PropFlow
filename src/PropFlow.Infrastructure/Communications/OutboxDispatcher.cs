using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropFlow.Application.Communications;

namespace PropFlow.Infrastructure.Communications;

// Runs the outbox relay on a fixed interval. Delivery logic and per-tenant iteration live in
// OutboxRelay; this type only owns the schedule and failure isolation.
public sealed class OutboxDispatcher(OutboxRelay relay, CommunicationsOptions options, ILogger<OutboxDispatcher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PollInterval);
        do
        {
            try
            {
                await relay.RelayPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox dispatch cycle failed.");
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
