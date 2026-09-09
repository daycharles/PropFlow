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
            submission.RecipientAddress, submission.Subject, submission.Body, submission.IdempotencyKey, clock.GetUtcNow()));

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
}
