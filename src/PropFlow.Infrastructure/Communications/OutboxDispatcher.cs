using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropFlow.Application.Communications;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Infrastructure.Communications;

// Polls the outbox for every organization on a fixed interval using the restricted runtime
// role. Each organization is processed in its own tenant context, so row-level security still
// applies and no privileged connection is needed.
public sealed class OutboxDispatcher(
    IConfiguration configuration,
    IEnumerable<IMessageSender> senders,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connection))
        {
            logger.LogWarning("Outbox dispatcher idle: no Database connection is configured.");
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchAllTenantsAsync(connection, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox dispatch cycle failed.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DispatchAllTenantsAsync(string connection, CancellationToken cancellationToken)
    {
        List<Guid> organizations;
        await using (var identity = DatabaseProvisioner.CreateIdentityStore(connection))
        {
            organizations = await identity.Organizations.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken);
        }

        foreach (var organization in organizations)
        {
            await using var store = DatabaseProvisioner.CreateCommunicationsStore(connection, organization);
            var processed = await new OutboxProcessor(store, senders, clock).ProcessPendingAsync(cancellationToken);
            if (processed > 0)
                logger.LogInformation("Dispatched {Count} outbox message(s) for organization {Organization}.", processed, organization);
        }
    }
}
