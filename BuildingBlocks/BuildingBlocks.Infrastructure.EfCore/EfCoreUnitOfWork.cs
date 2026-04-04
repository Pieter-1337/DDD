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
    private readonly ICommitStrategy? _commitStrategy;
    private readonly ILogger<EfCoreUnitOfWork<TContext>> _logger;
    private readonly List<IIntegrationEvent> _queuedIntegrationEvents = [];
    private IDbContextTransaction? _transaction;
    private int _transactionDepth; // Track nested transaction depth

    public EfCoreUnitOfWork(
        TContext context,
        IEventBus? eventBus = null,
        ICommitStrategy? commitStrategy = null,
        IMediator? mediator = null,
        ILogger<EfCoreUnitOfWork<TContext>>? logger = null)
    {
        _context = context;
        _eventBus = eventBus;
        _commitStrategy = commitStrategy;
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

        // Publish integration events to the outbox BEFORE saving.
        // IEventBus.PublishAsync queues messages for the outbox (MassTransit writes to
        // OutboxMessage table, Wolverine buffers in memory). The actual commit + delivery
        // happens in CloseTransactionAsync at depth 0, either via the commit strategy
        // (Wolverine) or the regular transaction commit (MassTransit).
        await PublishIntegrationEventsToOutboxAsync(cancellationToken);

        return await _context.SaveChangesAsync(cancellationToken);
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

    private async Task PublishIntegrationEventsToOutboxAsync(CancellationToken cancellationToken)
    {
        if (_eventBus is null || _queuedIntegrationEvents.Count == 0)
        {
            return;
        }

        foreach (var integrationEvent in _queuedIntegrationEvents)
        {
            var eventType = integrationEvent.GetType().Name;

            _logger.LogInformation(
                "Writing integration event {EventType} with EventId {EventId} to outbox",
                eventType,
                integrationEvent.EventId);

            // With Bus Outbox, this writes to the OutboxMessage table (not RabbitMQ directly)
            // Use reflection to call the generic PublishAsync method with the correct type
            var publishMethod = typeof(IEventBus)
                .GetMethod(nameof(IEventBus.PublishAsync))!
                .MakeGenericMethod(integrationEvent.GetType());

            var task = (Task)publishMethod.Invoke(_eventBus, [integrationEvent, cancellationToken])!;
            await task;
        }

        _queuedIntegrationEvents.Clear();
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

        try
        {
            // Only commit/rollback when depth reaches 0 (outermost call)
            if (exception is not null)
            {
                await _transaction.RollbackAsync(cancellationToken);
                // Discard queued integration events on rollback
                _queuedIntegrationEvents.Clear();
            }
            else if (_commitStrategy is not null)
            {
                // Delegate commit to the commit strategy (e.g., Wolverine outbox
                // persists outbox messages + commits + delivers in one atomic operation).
                await _commitStrategy.CommitAsync(cancellationToken);
            }
            else
            {
                // MassTransit path: commit persists domain data + outbox entries atomically.
                // The background BusOutboxDeliveryService delivers outbox entries to RabbitMQ.
                await _transaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
            _transactionDepth = 0;
        }
    }
}
