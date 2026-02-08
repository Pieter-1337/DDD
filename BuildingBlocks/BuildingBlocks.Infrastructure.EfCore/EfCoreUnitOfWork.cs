using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BuildingBlocks.Infrastructure.EfCore;

public class EfCoreUnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IEventBus? _eventBus;
    private readonly List<IIntegrationEvent> _queuedIntegrationEvents = [];
    private IDbContextTransaction? _transaction;

    public EfCoreUnitOfWork(TContext context, IEventBus? eventBus = null)
    {
        _context = context;
        _eventBus = eventBus;
    }

    public void QueueIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        _queuedIntegrationEvents.Add(integrationEvent);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Capture the queued integration events before saving
        var integrationEventsToPublish = _queuedIntegrationEvents.ToList();
        _queuedIntegrationEvents.Clear();

        var result = await _context.SaveChangesAsync(cancellationToken);

        // Publish integration events to message bus after successful save
        await PublishIntegrationEventsAsync(integrationEventsToPublish, cancellationToken);

        return result;
    }

    public IRepository<T> RepositoryFor<T>() where T : class, IEntityBase
    {
        return new EfCoreRepository<TContext, T>(_context);
    }

    private async Task PublishIntegrationEventsAsync(List<IIntegrationEvent> integrationEvents, CancellationToken cancellationToken)
    {
        if (_eventBus is null || integrationEvents.Count == 0)
        {
            return;
        }

        foreach (var integrationEvent in integrationEvents)
        {
            await PublishEventAsync(integrationEvent, cancellationToken);
        }
    }

    private async Task PublishEventAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        // Use reflection to call the generic PublishAsync method with the correct type
        var publishMethod = typeof(IEventBus)
            .GetMethod(nameof(IEventBus.PublishAsync))!
            .MakeGenericMethod(integrationEvent.GetType());

        var task = (Task)publishMethod.Invoke(_eventBus, [integrationEvent, cancellationToken])!;
        await task;
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CloseTransactionAsync(Exception? exception = null, CancellationToken cancellationToken = default)
    {
        if (_transaction is null) return;

        if (exception is not null)
        {
            await _transaction.RollbackAsync(cancellationToken);
        }
        else
        {
            await _transaction.CommitAsync(cancellationToken);
        }

        await _transaction.DisposeAsync();
        _transaction = null;
    }
}
