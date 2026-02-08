using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain;

namespace BuildingBlocks.Application.Interfaces;

public interface IUnitOfWork
{
    IRepository<T> RepositoryFor<T>() where T : class, IEntityBase;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues an integration event to be published after a successful save.
    /// Integration events are published to the message broker for cross-bounded-context communication.
    /// </summary>
    void QueueIntegrationEvent(IIntegrationEvent integrationEvent);

    /// <summary>
    /// Begins a database transaction
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the transaction if no exception, otherwise rolls back
    /// </summary>
    Task CloseTransactionAsync(Exception? exception = null, CancellationToken cancellationToken = default);
}
