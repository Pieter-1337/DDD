using System.Linq.Expressions;
using BuildingBlocks.Domain;

namespace BuildingBlocks.Application;

public interface IRepository<TEntity> where TEntity : class, IEntityBase
{
    IQueryable<TEntity> GetAll(Expression<Func<TEntity, bool>>? filter = null);
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> filter, CancellationToken ct = default);

    Task<TDto?> FirstOrDefaultAsDtoAsync<TDto>(
        Expression<Func<TEntity, bool>> filter,
        CancellationToken ct = default)
        where TDto : class, IEntityDto<TEntity, TDto>;

    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
    void Add(TEntity entity);
    void Remove(TEntity entity);
}
