using System.Linq.Expressions;
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure.EfCore;

public class EfCoreRepository<TContext, TEntity> : IRepository<TEntity>
    where TEntity : class, IEntityBase
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly DbSet<TEntity> _dbSet;

    public EfCoreRepository(TContext context)
    {
        _context = context;
        _dbSet = _context.Set<TEntity>();
    }

    public IQueryable<TEntity> GetAll(Expression<Func<TEntity, bool>>? filter = null)
    {
        var query = _dbSet.AsQueryable();

        if (filter != null)
            query = query.Where(filter);

        return query;
    }

    public async Task<IEnumerable<TEntity>> GetAllAsListAsync(Expression<Func<TEntity, bool>>? filter = null, CancellationToken ct = default)
    {
        return await GetAll(filter).ToListAsync(ct);
    }

    public async Task<IEnumerable<TDto>> GetAllAsDtosAsync<TDto>(
        Expression<Func<TEntity, bool>>? filter = null,
        CancellationToken ct = default)
        where TDto : class, IEntityDto<TEntity, TDto>
    {
        return await GetAll(filter)
            .Select(TDto.Project)
            .ToListAsync(ct);
    }
    public async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        //First try from local cache, else actual query
        return _dbSet.Local.FindEntry(id)?.Entity ??
            await FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> filter, CancellationToken ct = default)
    {
        return await GetAll(filter)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<TDto?> FirstOrDefaultAsDtoAsync<TDto>(
        Expression<Func<TEntity, bool>> filter,
        CancellationToken ct = default)
        where TDto : class, IEntityDto<TEntity, TDto>
    {
        return await GetAll(filter)
            .Select(TDto.Project)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<TDto?> FirstOrDefaultAsDtoAsync<TDto, TNavigation>(
      Expression<Func<TEntity, bool>> filter,
      Expression<Func<TEntity, TNavigation>> navigation,
      CancellationToken ct = default)
      where TNavigation : class
      where TDto : class, IEntityDto<TNavigation, TDto>
    {
        return await GetAll(filter)
            .Select(navigation)      // Entity → Navigation property
            .Select(TDto.Project)     // Navigation → DTO
            .FirstOrDefaultAsync(ct);
    }

    public async Task<TResult?> FirstOrDefaultWithProjectionAsync<TResult>(
    Expression<Func<TEntity, bool>> filter,
    Expression<Func<TEntity, TResult>> projection,
    CancellationToken ct = default)
    {
        return await GetAll(filter)
            .Select(projection)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbSet.AnyAsync(e => e.Id == id, ct);
    }

    public async Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> filter, CancellationToken ct = default)
    {
        return await _dbSet.AnyAsync(filter, ct);
    }

    public void Add(TEntity entity)
    {
        _dbSet.Add(entity);
    }

    public void Remove(TEntity entity)
    {
        _dbSet.Remove(entity);
    }
}
