# Domain Event Dispatching

## Overview

This document explains how domain events are dispatched via MediatR during `SaveChangesAsync()`. Domain events enable internal decoupling within a bounded context.

**Note:** This project currently uses integration events only. This document describes how domain event dispatching would work if implemented.

---

## Architecture

```
Entity raises domain event
    |
    v
SaveChangesAsync()
    |
    +-- 1. Collect domain events from entities
    |
    +-- 2. Clear domain events from entities
    |
    +-- 3. Save changes to database
    |
    +-- 4. DispatchDomainEventsAsync() via MediatR
    |       |
    |       +-- Handler 1 (audit logging)
    |       +-- Handler 2 (send notification)
    |       +-- Handler 3 (queue integration event)
    |
    +-- 5. PublishQueuedIntegrationEventsAsync() via MassTransit
            |
            +-- RabbitMQ -> Other bounded contexts
```

---

## Implementation

### Step 1: EfCoreUnitOfWork with Domain Event Dispatching

The UnitOfWork collects domain events from entities and dispatches them after save:

Location: `BuildingBlocks/BuildingBlocks.Infrastructure.EfCore/EfCoreUnitOfWork.cs`

```csharp
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure.EfCore;

public class EfCoreUnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IMediator _mediator;
    private readonly IEventBus? _eventBus;
    private readonly List<IIntegrationEvent> _queuedIntegrationEvents = [];

    public EfCoreUnitOfWork(
        TContext context,
        IMediator mediator,
        IEventBus? eventBus = null)
    {
        _context = context;
        _mediator = mediator;
        _eventBus = eventBus;
    }

    public void QueueIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        _queuedIntegrationEvents.Add(integrationEvent);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 1. Collect domain events before saving
        var domainEvents = CollectDomainEvents();

        // 2. Save changes to database
        var result = await _context.SaveChangesAsync(cancellationToken);

        // 3. Dispatch domain events (internal, via MediatR)
        await DispatchDomainEventsAsync(domainEvents, cancellationToken);

        // 4. Publish integration events (external, via MassTransit)
        await PublishQueuedIntegrationEventsAsync(cancellationToken);

        return result;
    }

    private List<IDomainEvent> CollectDomainEvents()
    {
        var entitiesWithEvents = _context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = entitiesWithEvents
            .SelectMany(e => e.DomainEvents)
            .ToList();

        // Clear events from entities to prevent re-dispatch
        foreach (var entity in entitiesWithEvents)
        {
            entity.ClearDomainEvents();
        }

        return domainEvents;
    }

    private async Task DispatchDomainEventsAsync(
        List<IDomainEvent> domainEvents,
        CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }
    }

    private async Task PublishQueuedIntegrationEventsAsync(CancellationToken cancellationToken)
    {
        if (_eventBus is null || _queuedIntegrationEvents.Count == 0)
            return;

        var events = _queuedIntegrationEvents.ToList();
        _queuedIntegrationEvents.Clear();

        foreach (var integrationEvent in events)
        {
            await _eventBus.PublishAsync(integrationEvent, cancellationToken);
        }
    }

    public IRepository<T> RepositoryFor<T>() where T : class, IEntityBase
    {
        return new EfCoreRepository<TContext, T>(_context);
    }

    // Transaction methods omitted for brevity...
}
```

**Key points:**
- Domain events collected from entities via `IHasDomainEvents`
- Events cleared from entities before dispatch (prevents duplicates)
- Domain events dispatched via MediatR (`INotificationHandler<T>`)
- Integration events published via MassTransit (`IEventBus`)

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

### With Domain Events (Full Pattern)

```
POST /patients/{id}/suspend
    |
    v
SuspendPatientCommandHandler
    |
    +-- patient.Suspend()
    |       |
    |       +-- AddDomainEvent(PatientSuspendedEvent)
    |
    +-- _uow.SaveChangesAsync()
            |
            +-- Save to DB
            |
            +-- DispatchDomainEventsAsync()
            |       |
            |       +-- PatientSuspendedEventHandler
            |               |
            |               +-- Audit log (internal)
            |               +-- QueueIntegrationEvent() (external)
            |
            +-- PublishQueuedIntegrationEventsAsync()
                    |
                    +-- RabbitMQ -> Billing, Notifications
```

### Without Domain Events (Current Approach)

```
POST /patients/{id}/suspend
    |
    v
SuspendPatientCommandHandler
    |
    +-- patient.Suspend()
    |
    +-- _uow.QueueIntegrationEvent(PatientSuspendedIntegrationEvent)
    |
    +-- _uow.SaveChangesAsync()
            |
            +-- Save to DB
            |
            +-- PublishQueuedIntegrationEventsAsync()
                    |
                    +-- RabbitMQ -> Billing, Notifications
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
