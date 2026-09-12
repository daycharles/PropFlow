using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Application.Integrations;
using PropFlow.Domain.Integrations;

namespace PropFlow.Infrastructure.Integrations;

// Connection CRUD plus the sync run.
//
// A sync claims a SyncRun, pulls the adapter's full snapshot, upserts one ExternalRecordLink per
// external record, hands the snapshot to EfIntegrationReconciler to drive into the operations
// schema, sweeps the links the source stopped reporting, and closes the run with real counters.
//
// The run row is inserted and SAVED BEFORE the adapter is contacted — the same claim-then-commit
// shape as OutboxProcessor.cs:38-48, and for the same reason: two dispatchers must not sync one
// connection at once. Exclusivity is the IX_SyncRuns_ActiveClaim partial unique index, not an
// in-memory check, so the loser is whoever the database rejects. Do not move that save after the
// pull to avoid the exception; the exception is the claim working.
public sealed class EfIntegrationOperations(
    IntegrationStore store,
    IIntegrationCatalog catalog,
    TimeProvider clock,
    EfIntegrationReconciler reconciler)
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

    public Task<SyncReport> SyncAsync(Guid id, CancellationToken cancellationToken) =>
        SyncAsync(id, SyncTrigger.Manual, 1, cancellationToken);

    public async Task<SyncReport> SyncAsync(Guid id, SyncTrigger trigger, int attemptNumber,
        CancellationToken cancellationToken)
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

        // THE CLAIM. Inserted and committed before PullAsync, exactly as OutboxProcessor claims an
        // outbox message before contacting a provider. IX_SyncRuns_ActiveClaim makes a second
        // Running row for this connection a unique violation, so the loser finds out here and
        // backs out rather than running a duplicate sync.
        var run = SyncRun.Begin(store.OrganizationId, Guid.NewGuid(), connection.Id, trigger, now, attemptNumber);
        store.SyncRuns.Add(run);
        connection.BeginSync(now);
        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsActiveClaimViolation(exception))
        {
            store.ChangeTracker.Clear();
            return SyncReport.AlreadyRunning;
        }

        IntegrationSnapshot snapshot;
        try
        {
            snapshot = await adapter.PullAsync(new IntegrationPullContext(connection.Id), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = Sanitize(ex.Message);
            connection.FailSync(clock.GetUtcNow(), error);
            run.Fail(error, SyncCounts.Zero, clock.GetUtcNow());
            await store.SaveChangesAsync(cancellationToken);
            return new SyncReport(SyncOutcome.Failed, 0, 0, 0, 0, connection.LastError, RunId: run.Id);
        }

        var existing = await store.RecordLinks
            .Where(r => r.ConnectionId == id)
            .ToDictionaryAsync(r => (r.Kind, r.ExternalId), cancellationToken);

        var seen = 0;
        seen += Ingest(IntegrationEntityKind.Property, snapshot.Properties, r => r.ExternalId, connection, existing, run.Id, now);
        seen += Ingest(IntegrationEntityKind.Space, snapshot.Spaces, r => r.ExternalId, connection, existing, run.Id, now);
        seen += Ingest(IntegrationEntityKind.Occupancy, snapshot.Occupancies, r => r.ExternalId, connection, existing, run.Id, now);
        seen += Ingest(IntegrationEntityKind.WorkOrder, snapshot.WorkOrders, r => r.ExternalId, connection, existing, run.Id, now);
        seen += Ingest(IntegrationEntityKind.Asset, snapshot.Assets, r => r.ExternalId, connection, existing, run.Id, now);
        // Occupancies bring a resident each, under a synthetic external id, because
        // CanonicalOccupancy carries the person but no id for them. The link has to exist before
        // the reconciler looks it up. Residents are not counted in Seen: they are not records the
        // source sent, they are records PropFlow derived.
        foreach (var occupancy in snapshot.Occupancies)
            EnsureLink(IntegrationEntityKind.Resident, SyntheticExternalId.ForResident(occupancy.ExternalId),
                connection, existing);

        run.Heartbeat(clock.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken);

        var reconciliation = await reconciler.ReconcileAsync(connection, adapter.Descriptor, snapshot,
            existing, run.Id, cancellationToken);

        var counts = new SyncCounts(seen, reconciliation.Added, reconciliation.Updated,
            reconciliation.Failed, reconciliation.Conflicted);
        connection.CompleteSync(clock.GetUtcNow());
        run.Complete(counts, CanonicalHash.Of(snapshot), clock.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken);

        return new SyncReport(SyncOutcome.Completed, seen, reconciliation.Added, reconciliation.Updated,
            reconciliation.Failed, null, reconciliation.Conflicted, reconciliation.Retired, run.Id);
    }

    // The claim index is the only unique constraint a Running SyncRun insert can violate, but the
    // name is checked rather than assumed so an unrelated constraint failure still surfaces.
    private static bool IsActiveClaimViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName is "IX_SyncRuns_ActiveClaim";

    // IntegrationConnection.FailSync rejects control characters and anything over ErrorMaxLength,
    // so an adapter exception with a multi-line or very long message used to throw *inside* the
    // catch block above and escape SyncAsync as a 500. Harmless while the only adapter was the
    // in-memory mock, which never throws; reachable as of PF-S19.07, where a transport or
    // serializer exception can carry either. The adapter contract asks for a short single-line
    // IntegrationPullException, but a store cannot rely on every adapter honouring it.
    public static string Sanitize(string message)
    {
        var builder = new StringBuilder(message.Length);
        foreach (var ch in message)
        {
            var next = char.IsControl(ch) ? ' ' : ch;
            // Collapse runs of whitespace so a stack-shaped message stays one readable line.
            if (next is ' ' && (builder.Length is 0 || builder[^1] is ' ')) continue;
            builder.Append(next);
        }
        while (builder.Length > 0 && builder[^1] is ' ') builder.Length--;

        if (builder.Length is 0) return "The adapter failed without a message.";
        return builder.Length <= IntegrationConnection.ErrorMaxLength
            ? builder.ToString()
            : builder.ToString(0, IntegrationConnection.ErrorMaxLength - 1) + "…";
    }

    // Records what the source currently says about each external id. This is the "last SEEN" side
    // only: it moves ContentHash and never ReconciledHash, so a record whose content changed comes
    // out of here with NeedsReconciliation true and the reconciler decides what to do about it.
    // Returns how many records were seen.
    private int Ingest<T>(IntegrationEntityKind kind, IReadOnlyList<T> records, Func<T, string> externalId,
        IntegrationConnection connection, Dictionary<(IntegrationEntityKind, string), ExternalRecordLink> existing,
        Guid runId, DateTimeOffset now) where T : notnull
    {
        foreach (var record in records)
        {
            var link = EnsureLink(kind, externalId(record), connection, existing);
            link.Observe(CanonicalHash.Of(record), now, runId);
        }
        return records.Count;
    }

    private ExternalRecordLink EnsureLink(IntegrationEntityKind kind, string externalId,
        IntegrationConnection connection, Dictionary<(IntegrationEntityKind, string), ExternalRecordLink> existing)
    {
        if (existing.TryGetValue((kind, externalId), out var link)) return link;
        link = new ExternalRecordLink(store.OrganizationId, Guid.NewGuid(), connection.Id, kind, externalId);
        store.RecordLinks.Add(link);
        existing[(kind, externalId)] = link;
        return link;
    }

    private async Task<IntegrationConnectionHealth> HealthAsync(IntegrationConnection connection, CancellationToken cancellationToken)
    {
        var tracked = await store.RecordLinks.AsNoTracking()
            .CountAsync(r => r.ConnectionId == connection.Id, cancellationToken);
        var failed = await store.RecordLinks.AsNoTracking()
            .CountAsync(r => r.ConnectionId == connection.Id && r.SyncState == SyncState.Failed, cancellationToken);
        var openConflicts = await store.Conflicts.AsNoTracking()
            .CountAsync(c => c.ConnectionId == connection.Id && c.Status == ConflictStatus.Open, cancellationToken);
        return new IntegrationConnectionHealth(connection.Id, connection.SourceSystem, connection.DisplayName,
            connection.IsEnabled, connection.LastAttemptedAt, connection.LastSucceededAt,
            connection.ConsecutiveFailures, connection.LastError, tracked, failed, openConflicts);
    }
}
