using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;

namespace PropFlow.Infrastructure.Communications;

public sealed class EfOutbox(CommunicationsStore store, TimeProvider clock) : IOutbox
{
    public async Task<bool> EnqueueAsync(OutboxSubmission submission, CancellationToken cancellationToken)
    {
        if (await store.OutboxMessages.AnyAsync(x => x.IdempotencyKey == submission.IdempotencyKey, cancellationToken))
            return false;

        store.OutboxMessages.Add(OutboxMessage.Create(store.OrganizationId, Guid.NewGuid(), submission.Channel,
            submission.RecipientAddress, submission.Subject, submission.Body, submission.IdempotencyKey, clock.GetUtcNow(),
            submission.WorkId, submission.ResidentVisible));

        try
        {
            await store.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent enqueue won the race for this idempotency key.
            store.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<IReadOnlyList<bool>> EnqueueBatchAsync(IReadOnlyList<OutboxSubmission> submissions, CancellationToken cancellationToken)
    {
        if (submissions.Count == 0) return [];
        await using var transaction = await store.Database.BeginTransactionAsync(cancellationToken);
        var keys = submissions.Select(x => x.IdempotencyKey).ToArray();
        var existing = (await store.OutboxMessages.Where(x => keys.Contains(x.IdempotencyKey))
            .Select(x => x.IdempotencyKey).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var result = new bool[submissions.Count];
        for (var index = 0; index < submissions.Count; index++)
        {
            var submission = submissions[index];
            if (!existing.Add(submission.IdempotencyKey)) continue;
            result[index] = true;
            store.OutboxMessages.Add(OutboxMessage.Create(store.OrganizationId, Guid.NewGuid(), submission.Channel,
                submission.RecipientAddress, submission.Subject, submission.Body, submission.IdempotencyKey, clock.GetUtcNow(),
                submission.WorkId, submission.ResidentVisible));
        }
        try { await store.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return result; }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }
}
