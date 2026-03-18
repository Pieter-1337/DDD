# Domain Event Dispatching

## Overview

This document explains how domain events are dispatched via MediatR during `SaveChangesAsync()`. Domain events enable internal decoupling within a bounded context and serve as the bridge to integration events.

**Pattern:** Command handlers are kept clean (just entity operations + save). Domain event handlers listen for domain events via MediatR and queue integration events for cross-bounded context communication.

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
| Automatic logging on publish | Every integration event is logged with EventType and EventId for observability |
| Message bus failures are resilient | Individual event publish failures are caught and logged; command still succeeds since DB transaction is already committed |

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

        try
        {
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
        catch (Exception ex)
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType());

            _logger.LogError(ex,
                "Failed to publish integration event {EventType} with EventId {EventId}. " +
                "The database transaction was already committed. Payload: {Payload}",
                eventType,
                integrationEvent.EventId,
                payload);
        }
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
            else
            {
                await _transaction.CommitAsync(cancellationToken);
                // Publish integration events AFTER successful commit
                await PublishAndClearIntegrationEventsAsync(cancellationToken);
            }
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
            _transactionDepth = 0;
        }
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
- **Resilience:** If the message bus (RabbitMQ) is unavailable, each integration event publish is wrapped in a try-catch. The failure is logged but does not block the command flow - the database transaction has already been committed at that point.

---

## Nested Transaction Support

When a command handler calls another command via MediatR, both go through `TransactionBehavior`. The `EfCoreUnitOfWork` handles this by tracking transaction depth using a counter - only when the depth reaches 0 (outermost caller) does it commit or rollback.

### How Nesting Works

```csharp
// In EfCoreUnitOfWork
private IDbContextTransaction? _transaction;
private int _transactionDepth;

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
            _queuedIntegrationEvents.Clear();
        }
        else
        {
            await _transaction.CommitAsync(cancellationToken);
            await PublishAndClearIntegrationEventsAsync(cancellationToken);
        }
    }
    finally
    {
        await _transaction.DisposeAsync();
        _transaction = null;
        _transactionDepth = 0;
    }
}
```

**Why a depth counter instead of a boolean flag?**

The depth counter approach handles nested command calls correctly by tracking how many levels deep we are. Each `BeginTransactionAsync()` increments the counter, and each `CloseTransactionAsync()` decrements it. Only when the counter reaches 0 (outermost caller) does the actual commit or rollback occur. This ensures the transaction lifetime is correctly managed regardless of nesting depth.

### Key Behaviors

| Scenario | Behavior |
|----------|----------|
| First command calls `BeginTransactionAsync()` | Starts transaction, depth = 1 |
| Nested command calls `BeginTransactionAsync()` | Detects existing transaction, depth++ |
| Nested command calls `CloseTransactionAsync()` | Decrements depth, does nothing if depth > 0 |
| Outer command calls `CloseTransactionAsync()` | Decrements depth to 0, commits/rollbacks |
| Integration events | Published only when depth reaches 0 (outermost caller) |

For detailed examples and usage guidance, see [Pipeline Behaviors - Nested Transaction Handling](../phase-3-cqrs/05-pipeline-behaviors.md#nested-transaction-handling).

---

## Domain Event Handlers

Domain event handlers implement MediatR's `INotificationHandler<T>`. They are responsible for:
1. Internal side effects (logging, auditing, cache invalidation)
2. Queueing integration events for cross-bounded context communication

### Example: PatientCreatedEventHandler

```csharp
// Scheduling.Application/Patients/EventHandlers/PatientCreatedEventHandler.cs
using BuildingBlocks.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Scheduling.Domain.Patients.Events;
using Shared.IntegrationEvents.Scheduling;

namespace Scheduling.Application.Patients.EventHandlers;

public class PatientCreatedEventHandler : INotificationHandler<PatientCreatedEvent>
{
    private readonly ILogger<PatientCreatedEventHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public PatientCreatedEventHandler(
        ILogger<PatientCreatedEventHandler> logger,
        IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public Task Handle(PatientCreatedEvent notification, CancellationToken cancellationToken)
    {
        // 1. Internal side effect: logging
        _logger.LogInformation("Patient created: {PatientId}", notification.PatientId);

        // 2. Queue integration event for cross-BC communication
        _unitOfWork.QueueIntegrationEvent(new PatientCreatedIntegrationEvent
        {
            PatientId = notification.PatientId,
            Email = notification.Email,
            FullName = $"{notification.FirstName} {notification.LastName}",
            DateOfBirth = notification.DateOfBirth
        });

        return Task.CompletedTask;
    }
}
```

### Example: PatientSuspendedEventHandler

```csharp
// Scheduling.Application/Patients/EventHandlers/PatientSuspendedEventHandler.cs
using BuildingBlocks.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Scheduling.Domain.Patients.Events;
using Shared.IntegrationEvents.Scheduling;

namespace Scheduling.Application.Patients.EventHandlers;

public class PatientSuspendedEventHandler : INotificationHandler<PatientSuspendedEvent>
{
    private readonly ILogger<PatientSuspendedEventHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public PatientSuspendedEventHandler(
        ILogger<PatientSuspendedEventHandler> logger,
        IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public Task Handle(PatientSuspendedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Handling PatientSuspendedEvent for patient {PatientId}",
            notification.PatientId);

        // Internal side effect: audit log, cache invalidation, etc.

        // Queue integration event for cross-BC communication
        _unitOfWork.QueueIntegrationEvent(new PatientSuspendedIntegrationEvent
        {
            PatientId = notification.PatientId,
            SuspendedAt = notification.OccurredAt
        });

        return Task.CompletedTask;
    }
}
```

### Example: PatientActivatedEventHandler

```csharp
// Scheduling.Application/Patients/EventHandlers/PatientActivatedEventHandler.cs
using BuildingBlocks.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Scheduling.Domain.Patients.Events;
using Shared.IntegrationEvents.Scheduling;

namespace Scheduling.Application.Patients.EventHandlers;

public class PatientActivatedEventHandler : INotificationHandler<PatientActivatedEvent>
{
    private readonly ILogger<PatientActivatedEventHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public PatientActivatedEventHandler(
        ILogger<PatientActivatedEventHandler> logger,
        IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public Task Handle(PatientActivatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Handling PatientActivatedEvent for patient {PatientId}",
            notification.PatientId);

        // Internal side effect: audit log, cache invalidation, etc.

        // Queue integration event for cross-BC communication
        _unitOfWork.QueueIntegrationEvent(new PatientActivatedIntegrationEvent
        {
            PatientId = notification.PatientId,
            ActivatedAt = notification.OccurredAt
        });

        return Task.CompletedTask;
    }
}
```

**Key principle:** Domain event handlers bridge the gap between internal domain events and external integration events. This keeps command handlers clean and focused on the core domain operation.

---

## Current Pattern: Domain Event Handlers Queue Integration Events

This project uses domain event handlers to queue integration events. This keeps command handlers clean and separates concerns appropriately.

### The Pattern

```csharp
// 1. Entity raises domain event during state change
public static Patient Create(string firstName, string lastName, string email, DateTime dateOfBirth)
{
    var patient = new Patient { /* ... */ };
    patient.AddDomainEvent(new PatientCreatedEvent(
        patient.Id, email, firstName, lastName, dateOfBirth));
    return patient;
}

// 2. Command handler is clean - just entity operations + save
public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
{
    var patient = Patient.Create(
        cmd.FirstName, cmd.LastName, cmd.Email, cmd.DateOfBirth);

    _uow.RepositoryFor<Patient>().Add(patient);
    await _uow.SaveChangesAsync(ct);  // Triggers domain event dispatch
    return patient.Id;
}

// 3. Domain event handler queues integration event
public class PatientCreatedEventHandler : INotificationHandler<PatientCreatedEvent>
{
    private readonly ILogger<PatientCreatedEventHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public PatientCreatedEventHandler(ILogger<PatientCreatedEventHandler> logger, IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public Task Handle(PatientCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Patient created: {PatientId}", notification.PatientId);

        _unitOfWork.QueueIntegrationEvent(new PatientCreatedIntegrationEvent
        {
            PatientId = notification.PatientId,
            Email = notification.Email,
            FullName = $"{notification.FirstName} {notification.LastName}",
            DateOfBirth = notification.DateOfBirth
        });

        return Task.CompletedTask;
    }
}
```

### Benefits

| Benefit | Description |
|---------|-------------|
| Clean command handlers | Just entity operations + save, no side effect logic |
| Single responsibility | Each handler has one job |
| Easy to extend | Add new handlers without modifying existing code |
| Testable | Each handler can be tested in isolation |
| Clear separation | Domain events vs integration events have distinct purposes |

---

## Complete Flow Diagram

### Domain Event Handler Pattern (Current Approach)

```
Entity.Create() -> AddDomainEvent(PatientCreatedEvent)
    |
    v
SaveChangesAsync()
    |
    +-- 1. DispatchDomainEventsAsync() -> MediatR
    |       |
    |       +-- PatientCreatedEventHandler
    |               +-- Logs event
    |               +-- _uow.QueueIntegrationEvent(PatientCreatedIntegrationEvent)
    |
    +-- 2. _context.SaveChangesAsync() (DB save)
    |
    +-- 3. [after commit] -> RabbitMQ
            |
            +-- Billing context receives event
            +-- Notifications context receives event
```

### Full Request Flow (With TransactionBehavior)

```
POST /patients
    |
    v
TransactionBehavior
    |
    +-- BeginTransactionAsync()
    |
    +-- CreatePatientCommandHandler
    |       |
    |       +-- Patient.Create(...)
    |       |       +-- AddDomainEvent(PatientCreatedEvent)
    |       |
    |       +-- _uow.RepositoryFor<Patient>().Add(patient)
    |       |
    |       +-- _uow.SaveChangesAsync()
    |               |
    |               +-- DispatchDomainEventsAsync() [BEFORE DB save]
    |               |       +-- PatientCreatedEventHandler
    |               |               +-- Log (internal side effect)
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

**Key timing:**
1. Domain events dispatch BEFORE the database save (handlers can modify state)
2. Integration events publish AFTER the transaction commits (guarantees data consistency)

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

[TestMethod]
public async Task PatientActivatedEventHandler_ShouldQueueIntegrationEvent()
{
    // Arrange
    var mockUow = new Mock<IUnitOfWork>();
    var handler = new PatientActivatedEventHandler(_logger, mockUow.Object);

    var domainEvent = new PatientActivatedEvent(Guid.NewGuid());

    // Act
    await handler.Handle(domainEvent, CancellationToken.None);

    // Assert
    mockUow.Verify(
        x => x.QueueIntegrationEvent(It.IsAny<PatientActivatedIntegrationEvent>()),
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
|           +-- PatientActivatedEvent.cs
|
+-- Scheduling.Application/
    +-- Patients/
        +-- EventHandlers/
            +-- PatientCreatedEventHandler.cs
            +-- PatientSuspendedEventHandler.cs
            +-- PatientActivatedEventHandler.cs

Shared/
+-- IntegrationEvents/
    +-- Scheduling/
        +-- PatientCreatedIntegrationEvent.cs
        +-- PatientSuspendedIntegrationEvent.cs
        +-- PatientActivatedIntegrationEvent.cs
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
