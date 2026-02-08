# Domain Event Dispatching

## Overview

This document explains how domain events are dispatched via MediatR during `SaveChangesAsync()`. Domain events enable internal decoupling within a bounded context.

**Note:** This project currently uses integration events only. This document describes how domain event dispatching would work if implemented.

---

## Architecture

### With TransactionBehavior (Commands)

When a command is wrapped in a transaction by `TransactionBehavior`, the flow ensures integration events are only published after a successful commit:

```
TransactionBehavior.Handle()
  |
  +-- BeginTransactionAsync()
  |
  +-- Handler runs
  |     |
  |     +-- Entity.DoSomething()
  |     |       +-- AddDomainEvent(SomethingHappenedEvent)
  |     |
  |     +-- _uow.QueueIntegrationEvent(SomethingHappenedIntegrationEvent)
  |     |
  |     +-- _uow.SaveChangesAsync()
  |           |
  |           +-- 1. DispatchDomainEventsAsync() via MediatR (BEFORE DB save)
  |           |       +-- Handler 1 (audit logging)
  |           |       +-- Handler 2 (send notification)
  |           |       +-- Handler 3 (queue more integration events)
  |           |
  |           +-- 2. _context.SaveChangesAsync() (DB save, in transaction)
  |           |
  |           +-- 3. [integration events remain queued - NOT published yet]
  |
  +-- CloseTransactionAsync()
        |
        +-- Success? -> CommitAsync()
        |               |
        |               +-- PublishAndClearIntegrationEventsAsync() via MassTransit
        |                       +-- RabbitMQ -> Other bounded contexts
        |
        +-- Exception? -> RollbackAsync()
                          |
                          +-- _queuedIntegrationEvents.Clear() (events discarded)
```

### Without Transaction (Queries, Non-Command Flows)

When there is no active transaction, integration events are published immediately in `SaveChangesAsync()`:

```
SaveChangesAsync() (no transaction)
    |
    +-- 1. DispatchDomainEventsAsync() via MediatR
    |
    +-- 2. _context.SaveChangesAsync() (DB save)
    |
    +-- 3. PublishAndClearIntegrationEventsAsync() via MassTransit (immediate)
            +-- RabbitMQ -> Other bounded contexts
```

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Domain events dispatch BEFORE DB save | Allows handlers to modify state that gets saved in the same transaction |
| Integration events publish AFTER commit | Guarantees data is persisted before notifying other systems |
| Rollback discards queued events | Prevents publishing events for operations that failed |
| No transaction = immediate publish | Maintains backwards compatibility for non-command flows |

---

## Implementation

### Step 1: EfCoreUnitOfWork with Domain Event Dispatching

The UnitOfWork collects domain events from entities, dispatches them via MediatR, and coordinates integration event publishing with transactions.

Location: `BuildingBlocks/BuildingBlocks.Infrastructure.EfCore/EfCoreUnitOfWork.cs`

```csharp
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain;
using BuildingBlocks.Domain.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BuildingBlocks.Infrastructure.EfCore;

public class EfCoreUnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IEventBus? _eventBus;
    private readonly IMediator? _mediator;
    private readonly List<IIntegrationEvent> _queuedIntegrationEvents = [];
    private IDbContextTransaction? _transaction;

    public EfCoreUnitOfWork(TContext context, IEventBus? eventBus = null, IMediator? mediator = null)
    {
        _context = context;
        _eventBus = eventBus;
        _mediator = mediator;
    }

    public void QueueIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        _queuedIntegrationEvents.Add(integrationEvent);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 1. Dispatch domain events BEFORE saving (allows handlers to modify state)
        await DispatchDomainEventsAsync(cancellationToken);

        // 2. Save changes to database
        var result = await _context.SaveChangesAsync(cancellationToken);

        // 3. Only publish integration events if NOT in a transaction
        //    If in a transaction, they'll be published after commit in CloseTransactionAsync
        if (_transaction is null)
        {
            await PublishAndClearIntegrationEventsAsync(cancellationToken);
        }

        return result;
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        if (_mediator is null) return;

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
        if (_eventBus is null || _queuedIntegrationEvents.Count == 0) return;

        var eventsToPublish = _queuedIntegrationEvents.ToList();
        _queuedIntegrationEvents.Clear();

        foreach (var integrationEvent in eventsToPublish)
        {
            await _eventBus.PublishAsync(integrationEvent, cancellationToken);
        }
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

    public IRepository<T> RepositoryFor<T>() where T : class, IEntityBase
    {
        return new EfCoreRepository<TContext, T>(_context);
    }
}
```

**Key points:**
- Domain events are dispatched BEFORE the database save (within the transaction)
- If a domain event handler fails, the entire transaction rolls back
- Integration events are queued but NOT published during `SaveChangesAsync()` when in a transaction
- Integration events are published AFTER `CommitAsync()` in `CloseTransactionAsync()`
- On rollback, queued integration events are discarded (never published)
- Without a transaction, integration events publish immediately in `SaveChangesAsync()`

---

## Domain Event Handlers

Domain event handlers implement MediatR's `INotificationHandler<T>`:

```csharp
// Scheduling.Application/Patients/EventHandlers/PatientSuspendedEventHandler.cs
using MediatR;
using Scheduling.Domain.Patients.Events;

namespace Scheduling.Application.Patients.EventHandlers;

public class PatientSuspendedEventHandler : INotificationHandler<PatientSuspendedEvent>
{
    private readonly ILogger<PatientSuspendedEventHandler> _logger;
    private readonly IUnitOfWork _uow;

    public PatientSuspendedEventHandler(
        ILogger<PatientSuspendedEventHandler> logger,
        IUnitOfWork uow)
    {
        _logger = logger;
        _uow = uow;
    }

    public Task Handle(PatientSuspendedEvent evt, CancellationToken ct)
    {
        _logger.LogInformation(
            "Handling PatientSuspendedEvent for patient {PatientId}",
            evt.PatientId);

        // Internal side effect: audit log, cache invalidation, etc.

        // Cross-context communication: queue integration event
        _uow.QueueIntegrationEvent(new PatientSuspendedIntegrationEvent
        {
            PatientId = evt.PatientId,
            SuspendedAt = evt.OccurredAt
        });

        return Task.CompletedTask;
    }
}
```

**Note:** A domain event handler can queue an integration event when the reaction needs to cross bounded context boundaries.

---

## When to Use Domain Events vs Direct Integration Events

### Option A: Domain Events + Integration Events (Full Pattern)

```csharp
// Entity raises domain event
public void Suspend()
{
    Status = PatientStatus.Suspended;
    AddDomainEvent(new PatientSuspendedEvent(Id));
}

// Domain event handler queues integration event
public class PatientSuspendedEventHandler : INotificationHandler<PatientSuspendedEvent>
{
    public Task Handle(PatientSuspendedEvent evt, CancellationToken ct)
    {
        // Internal: audit log
        _auditLogger.Log(...);

        // External: notify other contexts
        _uow.QueueIntegrationEvent(new PatientSuspendedIntegrationEvent(...));

        return Task.CompletedTask;
    }
}
```

**Use when:**
- You need internal side effects (audit, cache, notifications)
- You want entities focused purely on domain logic
- Multiple internal handlers need to react

### Option B: Direct Integration Events (Current Project Approach)

```csharp
// Command handler queues integration event directly
public async Task<Unit> Handle(SuspendPatientCommand cmd, CancellationToken ct)
{
    var patient = await _uow.RepositoryFor<Patient>().GetByIdAsync(cmd.PatientId, ct);

    patient!.Suspend();

    // Queue integration event directly in handler
    _uow.QueueIntegrationEvent(new PatientSuspendedIntegrationEvent
    {
        PatientId = patient.Id,
        SuspendedAt = DateTime.UtcNow
    });

    await _uow.SaveChangesAsync(ct);
    return Unit.Value;
}
```

**Use when:**
- All reactions are external (other bounded contexts)
- No internal side effects needed
- Simpler architecture is preferred
- Starting out and want fewer abstractions

---

## Flow Comparison

### With Domain Events (Full Pattern) - Inside TransactionBehavior

```
POST /patients/{id}/suspend
    |
    v
TransactionBehavior
    |
    +-- BeginTransactionAsync()
    |
    +-- SuspendPatientCommandHandler
    |       |
    |       +-- patient.Suspend()
    |       |       +-- AddDomainEvent(PatientSuspendedEvent)
    |       |
    |       +-- _uow.SaveChangesAsync()
    |               |
    |               +-- DispatchDomainEventsAsync() [BEFORE DB save]
    |               |       +-- PatientSuspendedEventHandler
    |               |               +-- Audit log (internal)
    |               |               +-- QueueIntegrationEvent() (queued, not published)
    |               |
    |               +-- _context.SaveChangesAsync() [DB save in transaction]
    |               |
    |               +-- [integration events still queued]
    |
    +-- CloseTransactionAsync()
            |
            +-- CommitAsync()
            |
            +-- PublishAndClearIntegrationEventsAsync() [AFTER commit]
                    +-- RabbitMQ -> Billing, Notifications
```

### Without Domain Events (Current Approach) - Inside TransactionBehavior

```
POST /patients/{id}/suspend
    |
    v
TransactionBehavior
    |
    +-- BeginTransactionAsync()
    |
    +-- SuspendPatientCommandHandler
    |       |
    |       +-- patient.Suspend()
    |       |
    |       +-- _uow.QueueIntegrationEvent(PatientSuspendedIntegrationEvent)
    |       |
    |       +-- _uow.SaveChangesAsync()
    |               |
    |               +-- _context.SaveChangesAsync() [DB save in transaction]
    |               |
    |               +-- [integration events still queued]
    |
    +-- CloseTransactionAsync()
            |
            +-- CommitAsync()
            |
            +-- PublishAndClearIntegrationEventsAsync() [AFTER commit]
                    +-- RabbitMQ -> Billing, Notifications
```

### Rollback Scenario

```
POST /patients/{id}/suspend
    |
    v
TransactionBehavior
    |
    +-- BeginTransactionAsync()
    |
    +-- SuspendPatientCommandHandler
    |       |
    |       +-- patient.Suspend()
    |       +-- _uow.QueueIntegrationEvent(...)
    |       +-- _uow.SaveChangesAsync() --> EXCEPTION!
    |
    +-- CloseTransactionAsync(exception)
            |
            +-- RollbackAsync()
            |
            +-- _queuedIntegrationEvents.Clear() [events DISCARDED]
            |
            +-- [nothing published to RabbitMQ]
```

---

## Testing Considerations

### Testing Domain Event Handlers

```csharp
[TestMethod]
public async Task Handle_ShouldQueueIntegrationEvent()
{
    // Arrange
    var mockUow = new Mock<IUnitOfWork>();
    var handler = new PatientSuspendedEventHandler(_logger, mockUow.Object);

    var domainEvent = new PatientSuspendedEvent(Guid.NewGuid());

    // Act
    await handler.Handle(domainEvent, CancellationToken.None);

    // Assert
    mockUow.Verify(
        x => x.QueueIntegrationEvent(It.IsAny<PatientSuspendedIntegrationEvent>()),
        Times.Once);
}
```

### Testing Integration Event Publishing

```csharp
[TestMethod]
public async Task SaveChangesAsync_ShouldPublishQueuedEvents()
{
    // Arrange
    var mockEventBus = new Mock<IEventBus>();
    var uow = new EfCoreUnitOfWork<TestContext>(_context, _mediator, mockEventBus.Object);

    var integrationEvent = new PatientSuspendedIntegrationEvent { PatientId = Guid.NewGuid() };
    uow.QueueIntegrationEvent(integrationEvent);

    // Act
    await uow.SaveChangesAsync();

    // Assert
    mockEventBus.Verify(
        x => x.PublishAsync(integrationEvent, It.IsAny<CancellationToken>()),
        Times.Once);
}
```

---

## Folder Structure

```
BuildingBlocks/
+-- BuildingBlocks.Domain/
|   +-- IDomainEvent.cs              <- Marker interface
|   +-- IHasDomainEvents.cs          <- Interface for entities with events
|   +-- DomainEventBase.cs           <- Base record
|
+-- BuildingBlocks.Application/
|   +-- Interfaces/
|   |   +-- IUnitOfWork.cs           <- Includes QueueIntegrationEvent()
|   +-- Messaging/
|       +-- IEventBus.cs             <- Integration event publisher
|       +-- IIntegrationEvent.cs     <- Integration event marker
|
+-- BuildingBlocks.Infrastructure.EfCore/
    +-- EfCoreUnitOfWork.cs          <- Dispatches domain & integration events

Core/Scheduling/
+-- Scheduling.Domain/
|   +-- Patients/
|       +-- Patient.cs               <- Implements IHasDomainEvents
|       +-- Events/
|           +-- PatientCreatedEvent.cs
|           +-- PatientSuspendedEvent.cs
|
+-- Scheduling.Application/
    +-- Patients/
        +-- EventHandlers/
            +-- PatientCreatedEventHandler.cs
            +-- PatientSuspendedEventHandler.cs

Shared/
+-- IntegrationEvents/
    +-- Scheduling/
        +-- PatientCreatedIntegrationEvent.cs
        +-- PatientSuspendedIntegrationEvent.cs
```

---

## Verification Checklist

- [ ] `EfCoreUnitOfWork` collects domain events from entities
- [ ] Domain events cleared from entities after collection
- [ ] Domain events dispatched via MediatR after save
- [ ] Integration events published via MassTransit after domain events
- [ ] Domain event handlers can queue integration events
- [ ] Events only dispatched/published on successful save

---

## Phase 2 Complete!

You now have:
- EF Core DbContext with proper configuration
- Generic Repository and UnitOfWork
- Database migrations
- **Domain event dispatching via MediatR**
- **Integration event publishing via MassTransit**

**Next: Phase 3 - CQRS Pattern**

We'll implement:
- Commands and Command Handlers
- Queries and Query Handlers
- MediatR pipeline behaviors
- Validation with FluentValidation
