using BuildingBlocks.Domain;

namespace BuildingBlocks.Application;

public interface IUnitOfWork
{
    IRepository<T> RepositoryFor<T>() where T : class, IEntityBase;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
