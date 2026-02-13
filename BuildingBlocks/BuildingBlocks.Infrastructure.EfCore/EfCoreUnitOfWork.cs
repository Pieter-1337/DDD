using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain;
using BuildingBlocks.Domain.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BuildingBlocks.Infrastructure.EfCore;

public class EfCoreUnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IEventBus? _eventBus;
    private readonly IMediator? _mediator;
    private readonly ILogger<EfCoreUnitOfWork<TContext>> _logger;
    private readonly List<IIntegrationEvent> _queuedIntegrationEvents = [];
    private IDbContextTransaction? _transaction;
    private int _transactionDepth; // Track nested transaction depth

    public EfCoreUnitOfWork(
        TContext context,
        IEventBus? eventBus = null,
        IMediator? mediator = null,
        ILogger<EfCoreUnitOfWork<TContext>>? logger = null)
    {
        _context = context;
        _eventBus = eventBus;
        _mediator = mediator;
        _logger = logger ?? NullLogger<EfCoreUnitOfWork<TContext>>.Instance;
    }

    public void QueueIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        _queuedIntegrationEvents.Add(integrationEvent);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Dispatch domain events BEFORE saving (allows handlers to modify state)
        await DispatchDomainEventsAsync(cancellationToken);

        var result = await _context.SaveChangesAsync(cancellationToken);

        // Only publish integration events if NOT in a transaction
        // If in a transaction, they'll be published after commit in CloseTransactionAsync
        if (_transaction is null)
        {
            await PublishAndClearIntegrationEventsAsync(cancellationToken);
        }

        return result;
    }

    public IRepository<T> RepositoryFor<T>() where T : class, IEntityBase
    {
        return new EfCoreRepository<TContext, T>(_context);
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        if (_mediator is null)
        {
            return;
        }

        // Find all entities with domain events
        var entitiesWithEvents = _context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        // Collect all domain events
        var domainEvents = entitiesWithEvents
            .SelectMany(e => e.DomainEvents)
            .ToList();

        // Clear events from entities before publishing (prevents re-processing)
        foreach (var entity in entitiesWithEvents)
        {
            entity.ClearDomainEvents();
        }

        // Publish each domain event via MediatR
        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }
    }

    private async Task PublishAndClearIntegrationEventsAsync(CancellationToken cancellationToken)
    {
        if (_eventBus is null || _queuedIntegrationEvents.Count == 0)
        {
            return;
        }

        var eventsToPublish = _queuedIntegrationEvents.ToList();
        _queuedIntegrationEvents.Clear();

        foreach (var integrationEvent in eventsToPublish)
        {
            await PublishEventAsync(integrationEvent, cancellationToken);
        }
    }

    private async Task PublishEventAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var eventType = integrationEvent.GetType().Name;

        _logger.LogInformation(
            "Publishing integration event {EventType} with EventId {EventId}",
            eventType,
            integrationEvent.EventId);

        // Use reflection to call the generic PublishAsync method with the correct type
        var publishMethod = typeof(IEventBus)
            .GetMethod(nameof(IEventBus.PublishAsync))!
            .MakeGenericMethod(integrationEvent.GetType());

        var task = (Task)publishMethod.Invoke(_eventBus, [integrationEvent, cancellationToken])!;
        await task;

        _logger.LogDebug(
            "Successfully published integration event {EventType} with EventId {EventId}",
            eventType,
            integrationEvent.EventId);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _transactionDepth++;

        // Don't start a new transaction if one is already active
        if (_transactionDepth > 1)
        {
            return; // Already in a transaction, reuse it
        }

        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CloseTransactionAsync(Exception? exception = null, CancellationToken cancellationToken = default)
    {
        if (_transaction is null) return;

        _transactionDepth--;

        // Still in nested calls, don't commit/rollback yet
        if (_transactionDepth > 0)
        {
            return;
        }

        // Only commit/rollback when depth reaches 0 (outermost call)
        if (exception is not null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            // Discard queued integration events on rollback
            _queuedIntegrationEvents.Clear();
        }
        else
        {
            await _transaction.CommitAsync(cancellationToken);
            // Publish integration events AFTER successful commit
            await PublishAndClearIntegrationEventsAsync(cancellationToken);
        }

        await _transaction.DisposeAsync();
        _transaction = null;
    }
}
