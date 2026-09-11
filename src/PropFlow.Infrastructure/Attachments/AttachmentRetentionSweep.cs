using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PropFlow.Application.Attachments;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Infrastructure.Attachments;

// Enforces attachment retention: an attachment whose RetainUntil has passed is deleted — the row
// and the stored blob — for every organization, each on its own tenant context using the
// restricted runtime role. RetainUntil is opt-in; a null retention never expires. Separated from
// the hosted service so it can be exercised directly in a test.
public sealed class AttachmentRetentionSweep(
    IConfiguration configuration,
    IAttachmentStorage storage,
    TimeProvider clock,
    ILogger<AttachmentRetentionSweep> logger)
{
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var connection = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connection))
        {
            logger.LogWarning("Attachment retention sweep skipped: no Database connection is configured.");
            return 0;
        }

        List<Guid> organizations;
        await using (var identity = DatabaseProvisioner.CreateIdentityStore(connection))
        {
            organizations = await identity.Organizations.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken);
        }

        var now = clock.GetUtcNow();
        var removed = 0;
        foreach (var organization in organizations)
        {
            await using var store = DatabaseProvisioner.CreateOperationsStore(connection, organization);
            await store.ComplianceEvidence.Where(x => x.RetainUntil != null && x.RetainUntil < now)
                .ExecuteDeleteAsync(cancellationToken);
            var expired = await store.Attachments.AsNoTracking()
                .Where(x => x.RetainUntil != null && x.RetainUntil < now)
                .Select(x => x.StorageKey)
                .ToListAsync(cancellationToken);
            if (expired.Count == 0) continue;

            // Blob first: a crash after this leaves an orphaned row the next sweep re-deletes; a
            // crash after the row delete would otherwise strand the blob forever.
            foreach (var storageKey in expired)
                await storage.DeleteAsync(storageKey, cancellationToken);
            await store.Attachments.Where(x => x.RetainUntil != null && x.RetainUntil < now)
                .ExecuteDeleteAsync(cancellationToken);

            removed += expired.Count;
            logger.LogInformation("Retention sweep removed {Count} expired attachment(s) for organization {Organization}.",
                expired.Count, organization);
        }

        return removed;
    }
}
