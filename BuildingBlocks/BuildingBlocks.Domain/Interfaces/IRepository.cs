using System.Linq.Expressions;

namespace BuildingBlocks.Domain.Interfaces;

public interface IRepository<TEntity>
{
    IQueryable<TEntity> GetAll(Expression<Func<TEntity, bool>>? filter = null);
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> filter, CancellationToken ct = default);
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
    void Add(TEntity entity);
    void Remove(TEntity entity);
}
