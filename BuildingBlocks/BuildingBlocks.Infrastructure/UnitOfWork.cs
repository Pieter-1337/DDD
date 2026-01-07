using BuildingBlocks.Domain.Interfaces;
using BuildingBlocks.Infrastructure.Events;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure;

public class UnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public UnitOfWork(TContext context, IDomainEventDispatcher domainEventDispatcher)
    {
        _context = context;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Save first (so events only fire on success)
        var result = await _context.SaveChangesAsync(cancellationToken);

        // Then dispatch events
        await _domainEventDispatcher.DispatchEventsAsync(_context, cancellationToken);

        return result;
    }

    public IRepository<T> RepositoryFor<T>() where T : class, IEntityBase
    {
        return new Repository<TContext, T>(_context);
    }
}
