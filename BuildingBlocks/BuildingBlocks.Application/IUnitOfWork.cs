using BuildingBlocks.Domain;

namespace BuildingBlocks.Application;

public interface IUnitOfWork
{
    IRepository<T> RepositoryFor<T>() where T : class, IEntityBase;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a database transaction
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the transaction if no exception, otherwise rolls back
    /// </summary>
    Task CloseTransactionAsync(Exception? exception = null, CancellationToken cancellationToken = default);
}
