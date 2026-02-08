# Event Publishing

## Overview

Integration events are queued in command handlers and published to RabbitMQ after `SaveChangesAsync()` succeeds.

```csharp
// Command handler queues integration event
_uow.QueueIntegrationEvent(new PatientCreatedIntegrationEvent(...));

// UnitOfWork publishes to RabbitMQ after save
await _uow.SaveChangesAsync(ct);
```

---

## Event Publishing Flow

```
Command Handler
      |
      | 1. Create/modify entity
      | 2. _uow.RepositoryFor<T>().Add(entity)
      | 3. _uow.QueueIntegrationEvent(event)
      | 4. await _uow.SaveChangesAsync()
      v
+-------------------+
| UnitOfWork        |
|                   |
| SaveChangesAsync: |
| 1. Save to DB     |
| 2. If success,    |
|    publish queued |
|    events via     |
|    IEventBus      |
+-------------------+
      |
      v
+-------------------+
|    RabbitMQ       |
| (via MassTransit) |
+-------------------+
      |
      +---> Consumer 1
      +---> Consumer 2
      +---> Consumer 3
```

---

## Implementation

### Step 1: IUnitOfWork with Event Queuing

The `IUnitOfWork` interface includes a method to queue integration events:

Location: `BuildingBlocks/BuildingBlocks.Application/Interfaces/IUnitOfWork.cs`

```csharp
using BuildingBlocks.Application.Messaging;

namespace BuildingBlocks.Application.Interfaces;

public interface IUnitOfWork
{
    IRepository<T> RepositoryFor<T>() where T : class, IEntityBase;

    /// <summary>
    /// Queues an integration event to be published after a successful save.
    /// Events are published to RabbitMQ via MassTransit.
    /// </summary>
    void QueueIntegrationEvent(IIntegrationEvent integrationEvent);

    /// <summary>
    /// Saves changes to the database and publishes queued integration events.
    /// Events are only published if the save succeeds.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CloseTransactionAsync(Exception? exception = null, CancellationToken cancellationToken = default);
}
```

### Step 2: EfCoreUnitOfWork Implementation

The UnitOfWork implementation queues events and publishes them after save:

Location: `BuildingBlocks/BuildingBlocks.Infrastructure.EfCore/EfCoreUnitOfWork.cs`

```csharp
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Messaging;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure.EfCore;

public class EfCoreUnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IEventBus? _eventBus;
    private readonly List<IIntegrationEvent> _queuedEvents = [];

    public EfCoreUnitOfWork(TContext context, IEventBus? eventBus = null)
    {
        _context = context;
        _eventBus = eventBus;
    }

    public void QueueIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        _queuedEvents.Add(integrationEvent);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _context.SaveChangesAsync(cancellationToken);

        // After successful save, publish queued integration events
        await PublishQueuedEventsAsync(cancellationToken);

        return result;
    }

    private async Task PublishQueuedEventsAsync(CancellationToken cancellationToken)
    {
        if (_eventBus is null || _queuedEvents.Count == 0)
            return;

        foreach (var @event in _queuedEvents)
        {
            await _eventBus.PublishAsync(@event, cancellationToken);
        }

        _queuedEvents.Clear();
    }

    public IRepository<T> RepositoryFor<T>() where T : class, IEntityBase
    {
        return new EfCoreRepository<TContext, T>(_context);
    }

    // Transaction methods...
}
```

**Key points:**
- `QueueIntegrationEvent()` adds to an internal list
- `SaveChangesAsync()` saves to DB first, then publishes events
- Events only published if save succeeds
- `IEventBus` is optional (null when MassTransit not configured, e.g., in tests)

### Step 3: Using in Command Handlers

Command handlers queue events before saving:

```csharp
public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    private readonly IUnitOfWork _uow;

    public CreatePatientCommandHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
    {
        // 1. Create entity
        var patient = Patient.Create(
            cmd.FirstName,
            cmd.LastName,
            cmd.Email,
            cmd.DateOfBirth);

        // 2. Add to repository
        _uow.RepositoryFor<Patient>().Add(patient);

        // 3. Queue integration event
        _uow.QueueIntegrationEvent(new PatientCreatedIntegrationEvent(
            patient.Id,
            cmd.FirstName,
            cmd.LastName,
            cmd.Email,
            cmd.DateOfBirth));

        // 4. Save and publish
        await _uow.SaveChangesAsync(ct);

        return patient.Id;
    }
}
```

### Step 4: Suspend Patient Example

```csharp
public class SuspendPatientCommandHandler : IRequestHandler<SuspendPatientCommand, Unit>
{
    private readonly IUnitOfWork _uow;

    public async Task<Unit> Handle(SuspendPatientCommand cmd, CancellationToken ct)
    {
        var patient = await _uow.RepositoryFor<Patient>()
            .GetByIdAsync(cmd.PatientId, ct);

        patient!.Suspend();

        // Queue event for the state change
        _uow.QueueIntegrationEvent(new PatientSuspendedIntegrationEvent(
            patient.Id,
            DateTime.UtcNow));

        await _uow.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
```

---

## Entity Base Class

The Entity base class is simple - just identity:

```csharp
namespace BuildingBlocks.Domain;

public abstract class Entity : IEntityBase
{
    public Guid Id { get; set; }
}
```

Entities focus purely on behavior and state.

---

## Multiple Events Per Operation

You can queue multiple events in a single operation:

```csharp
public async Task Handle(ProcessPatientAccountCommand cmd, CancellationToken ct)
{
    var patient = await _uow.RepositoryFor<Patient>()
        .GetByIdAsync(cmd.PatientId, ct);

    patient!.Suspend();

    // Queue multiple events
    _uow.QueueIntegrationEvent(new PatientSuspendedIntegrationEvent(patient.Id));
    _uow.QueueIntegrationEvent(new PatientAccountProcessedIntegrationEvent(
        patient.Id,
        ProcessingStatus.Suspended));

    await _uow.SaveChangesAsync(ct);  // Both events published after save
}
```

---

## Testing Considerations

For tests without RabbitMQ, `IEventBus` can be null:

```csharp
// In test setup
var unitOfWork = new EfCoreUnitOfWork<SchedulingDbContext>(
    context,
    eventBus: null);  // No events published during tests
```

Or mock `IEventBus` to verify events were queued:

```csharp
var mockEventBus = new Mock<IEventBus>();

// ... run test ...

mockEventBus.Verify(
    x => x.PublishAsync(
        It.Is<PatientCreatedIntegrationEvent>(e => e.PatientId == expectedId),
        It.IsAny<CancellationToken>()),
    Times.Once);
```

---

## Folder Structure

```
BuildingBlocks/
+-- BuildingBlocks.Application/
|   +-- Interfaces/
|   |   +-- IUnitOfWork.cs              <- Includes QueueIntegrationEvent()
|   +-- Messaging/
|       +-- IEventBus.cs                <- Publisher abstraction
|       +-- IIntegrationEvent.cs        <- Event marker interface
|       +-- IntegrationEventBase.cs     <- Base class
|
+-- BuildingBlocks.Infrastructure.EfCore/
    +-- EfCoreUnitOfWork.cs             <- Publishes after SaveChangesAsync

Shared/
+-- IntegrationEvents/
    +-- Scheduling/
        +-- PatientCreatedIntegrationEvent.cs
        +-- PatientSuspendedIntegrationEvent.cs

Core/Scheduling/
+-- Scheduling.Domain/
|   +-- Patients/
|       +-- Patient.cs
|
+-- Scheduling.Infrastructure/
    +-- Consumers/
        +-- PatientCreatedEventConsumer.cs
```

---

## Verification Checklist

- [ ] `IUnitOfWork` has `QueueIntegrationEvent()` method
- [ ] `EfCoreUnitOfWork` queues events and publishes after save
- [ ] Command handlers queue events before `SaveChangesAsync()`
- [ ] Integration events defined in `Shared/IntegrationEvents/`
- [ ] Events only published on successful save

---

## Phase 2 Complete!

You now have:
- EF Core DbContext with proper configuration
- Generic Repository and UnitOfWork
- Database migrations
- **Integration event publishing after save**

**Next: Phase 3 - CQRS Pattern**

We'll implement:
- Commands and Command Handlers
- Queries and Query Handlers
- MediatR pipeline behaviors
- Validation with FluentValidation
