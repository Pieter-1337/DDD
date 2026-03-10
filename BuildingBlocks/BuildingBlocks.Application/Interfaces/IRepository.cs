using System.Linq.Expressions;
using BuildingBlocks.Domain;

namespace BuildingBlocks.Application.Interfaces;

public interface IRepository<TEntity> where TEntity : class, IEntityBase
{
    IQueryable<TEntity> GetAll(Expression<Func<TEntity, bool>>? filter = null);
    Task<IEnumerable<TEntity>> GetAllAsListAsync(Expression<Func<TEntity, bool>>? filter = null, CancellationToken ct = default);
    Task<IEnumerable<TDto>> GetAllAsDtosAsync<TDto>(Expression<Func<TEntity, bool>>? filter = null, CancellationToken ct = default) 
        where TDto : class, IEntityDto<TEntity, TDto>;
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> filter, CancellationToken ct = default);
    Task<TDto?> FirstOrDefaultAsDtoAsync<TDto>(
        Expression<Func<TEntity, bool>> filter,
        CancellationToken ct = default)
        where TDto : class, IEntityDto<TEntity, TDto>;
    Task<TDto?> FirstOrDefaultAsDtoAsync<TDto, TNavigation>(
        Expression<Func<TEntity, bool>> filter,
        Expression<Func<TEntity, TNavigation>> navigation,
        CancellationToken ct = default)
        where TNavigation : class
        where TDto : class, IEntityDto<TNavigation, TDto>;
    Task<TResult?> FirstOrDefaultWithProjectionAsync<TResult>(
        Expression<Func<TEntity, bool>> filter,
        Expression<Func<TEntity, TResult>> projection,
        CancellationToken ct = default);
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> filter, CancellationToken ct = default);
    void Add(TEntity entity);
    void Remove(TEntity entity);
}
