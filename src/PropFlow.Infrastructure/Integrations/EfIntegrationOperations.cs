using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Integrations;
using PropFlow.Domain.Integrations;

namespace PropFlow.Infrastructure.Integrations;

// Connection CRUD plus the sync run. A sync pulls the adapter's full snapshot and upserts one
// ExternalRecordLink per external record, tracking added / updated / failed. It does not yet
// reconcile those records into Properties/Spaces/WorkItems/Assets — PF-6.10 is the external
// side only. The connection's health counters are updated in the same transaction as the links.
public sealed class EfIntegrationOperations(IntegrationStore store, IIntegrationCatalog catalog, TimeProvider clock)
    : IIntegrationOperations
{
    public async Task<IReadOnlyList<IntegrationConnectionHealth>> ListAsync(CancellationToken cancellationToken)
    {
        var connections = await store.Connections.AsNoTracking()
            .OrderBy(c => c.DisplayName).ToListAsync(cancellationToken);
        var result = new List<IntegrationConnectionHealth>(connections.Count);
        foreach (var connection in connections)
            result.Add(await HealthAsync(connection, cancellationToken));
        return result;
    }

    public async Task<IntegrationConnectionHealth?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var connection = await store.Connections.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        return connection is null ? null : await HealthAsync(connection, cancellationToken);
    }

    public async Task<(IntegrationWriteOutcome Outcome, Guid Id)> CreateAsync(
        CreateConnectionCommand command, CancellationToken cancellationToken)
    {
        var source = IntegrationConnection.NormalizeSourceSystem(command.SourceSystem);
        if (catalog.Resolve(source) is null)
            return (IntegrationWriteOutcome.UnknownSource, Guid.Empty);
        if (await store.Connections.AnyAsync(c => c.SourceSystem == source, cancellationToken))
            return (IntegrationWriteOutcome.DuplicateSource, Guid.Empty);

        var connection = new IntegrationConnection(store.OrganizationId, Guid.NewGuid(), source, command.DisplayName);
        store.Connections.Add(connection);
        await store.SaveChangesAsync(cancellationToken);
        return (IntegrationWriteOutcome.Created, connection.Id);
    }

    public async Task<IntegrationWriteOutcome> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        var connection = await store.Connections.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null) return IntegrationWriteOutcome.NotFound;
        if (enabled) connection.Enable(); else connection.Disable();
        await store.SaveChangesAsync(cancellationToken);
        return IntegrationWriteOutcome.Updated;
    }

    // A real source system can have far more records than one response should carry, so the
    // detail view pages through them.
    public const int MaxPageSize = 200;

    public async Task<RecordsPage?> RecordsAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await store.Connections.AnyAsync(c => c.Id == id, cancellationToken)) return null;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var rows = store.RecordLinks.AsNoTracking().Where(r => r.ConnectionId == id);
        var total = await rows.CountAsync(cancellationToken);
        var items = await rows
            .OrderByDescending(r => r.LastSeenAt)
            .ThenBy(r => r.Kind)
            .ThenBy(r => r.ExternalId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new RecordsPage(items, total, page, pageSize);
    }

    public async Task<SyncReport> SyncAsync(Guid id, CancellationToken cancellationToken)
    {
        var connection = await store.Connections.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null) return SyncReport.NotFound;
        if (!connection.IsEnabled) return SyncReport.Disabled;

        var now = clock.GetUtcNow();
        var adapter = catalog.Resolve(connection.SourceSystem);
        if (adapter is null)
        {
            connection.FailSync(now, $"No adapter is registered for source system '{connection.SourceSystem}'.");
            await store.SaveChangesAsync(cancellationToken);
            return new SyncReport(SyncOutcome.Failed, 0, 0, 0, 0, connection.LastError);
        }

        connection.BeginSync(now);
        await store.SaveChangesAsync(cancellationToken);

        IntegrationSnapshot snapshot;
        try
        {
            snapshot = await adapter.PullAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            connection.FailSync(clock.GetUtcNow(), ex.Message);
            await store.SaveChangesAsync(cancellationToken);
            return new SyncReport(SyncOutcome.Failed, 0, 0, 0, 0, connection.LastError);
        }

        var existing = await store.RecordLinks
            .Where(r => r.ConnectionId == id)
            .ToDictionaryAsync(r => (r.Kind, r.ExternalId), cancellationToken);

        var counters = new Counters();
        Ingest(IntegrationEntityKind.Property, snapshot.Properties, r => r.ExternalId, connection, existing, counters, now);
        Ingest(IntegrationEntityKind.Space, snapshot.Spaces, r => r.ExternalId, connection, existing, counters, now);
        Ingest(IntegrationEntityKind.Occupancy, snapshot.Occupancies, r => r.ExternalId, connection, existing, counters, now);
        Ingest(IntegrationEntityKind.WorkOrder, snapshot.WorkOrders, r => r.ExternalId, connection, existing, counters, now);
        Ingest(IntegrationEntityKind.Asset, snapshot.Assets, r => r.ExternalId, connection, existing, counters, now);

        connection.CompleteSync(clock.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken);

        // Failed is 0 here: PF-6.10 only records the external side, so there is no per-record
        // mapping step to fail. It becomes meaningful when reconciliation into the domain tables
        // lands. The persisted FailedRecords health count already reflects any Failed links.
        return new SyncReport(SyncOutcome.Completed, counters.Seen, counters.Added, counters.Updated, 0, null);
    }

    private void Ingest<T>(IntegrationEntityKind kind, IReadOnlyList<T> records, Func<T, string> externalId,
        IntegrationConnection connection, Dictionary<(IntegrationEntityKind, string), ExternalRecordLink> existing,
        Counters counters, DateTimeOffset now) where T : notnull
    {
        foreach (var record in records)
        {
            counters.Seen++;
            var key = externalId(record);
            var hash = CanonicalHash.Of(record);
            if (!existing.TryGetValue((kind, key), out var link))
            {
                link = new ExternalRecordLink(store.OrganizationId, Guid.NewGuid(), connection.Id, kind, key);
                store.RecordLinks.Add(link);
                existing[(kind, key)] = link;
                link.Observe(hash, now);
                counters.Added++;
                continue;
            }
            if (link.Observe(hash, now)) counters.Updated++;
        }
    }

    private async Task<IntegrationConnectionHealth> HealthAsync(IntegrationConnection connection, CancellationToken cancellationToken)
    {
        var tracked = await store.RecordLinks.AsNoTracking()
            .CountAsync(r => r.ConnectionId == connection.Id, cancellationToken);
        var failed = await store.RecordLinks.AsNoTracking()
            .CountAsync(r => r.ConnectionId == connection.Id && r.SyncState == SyncState.Failed, cancellationToken);
        return new IntegrationConnectionHealth(connection.Id, connection.SourceSystem, connection.DisplayName,
            connection.IsEnabled, connection.LastAttemptedAt, connection.LastSucceededAt,
            connection.ConsecutiveFailures, connection.LastError, tracked, failed);
    }

    private sealed class Counters
    {
        public int Seen;
        public int Added;
        public int Updated;
    }
}
