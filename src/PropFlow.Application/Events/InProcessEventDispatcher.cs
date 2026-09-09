using PropFlow.Domain;

namespace PropFlow.Application.Events;

public interface IDomainEventHandler
{
    Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken);
}

// Sequential, fail-fast dispatch. Durability/retries belong to the future transactional outbox.
public sealed class InProcessEventDispatcher(IEnumerable<IDomainEventHandler> handlers)
{
    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler.HandleAsync(domainEvent, cancellationToken);
        }
    }
}
