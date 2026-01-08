using System.Linq.Expressions;
using BuildingBlocks.Application;
using BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure;

public class Repository<TContext, TEntity> : IRepository<TEntity>
    where TEntity : class, IEntityBase
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly DbSet<TEntity> _dbSet;

    public Repository(TContext context)
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
            .Select(TDto.ToDto)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbSet.AnyAsync(e => e.Id == id, ct);
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
