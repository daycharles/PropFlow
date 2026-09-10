using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Infrastructure.Communications;

// Delivers the outbox for every organization, each on its own tenant context using the
// restricted runtime role. Separated from the hosted service so it can be exercised directly.
public sealed class OutboxRelay(IConfiguration configuration, IEnumerable<IMessageSender> senders,
    TimeProvider clock, CommunicationsOptions options, ILogger<OutboxRelay> logger)
{
    public CommunicationsOptions Options => options;

    public async Task<int> RelayPendingAsync(CancellationToken cancellationToken)
    {
        var connection = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connection))
        {
            logger.LogWarning("Outbox relay skipped: no Database connection is configured.");
            return 0;
        }

        List<Guid> organizations;
        await using (var identity = DatabaseProvisioner.CreateIdentityStore(connection))
        {
            organizations = await identity.Organizations.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken);
        }

        var delivered = 0;
        foreach (var organization in organizations)
        {
            await using var store = DatabaseProvisioner.CreateCommunicationsStore(connection, organization);
            var retentionCutoff = clock.GetUtcNow() - options.RetentionPeriod;
            await store.OutboxMessages
                .Where(x => x.CreatedAt < retentionCutoff &&
                    (x.Status == OutboxStatus.Sent || x.Status == OutboxStatus.Failed))
                .ExecuteDeleteAsync(cancellationToken);
            var count = await new OutboxProcessor(store, senders, clock, options).ProcessPendingAsync(cancellationToken);
            if (count > 0)
                logger.LogInformation("Delivered {Count} outbox message(s) for organization {Organization}.", count, organization);
            delivered += count;
        }

        return delivered;
    }
}
