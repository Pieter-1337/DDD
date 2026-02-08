# Domain Events

## What Are Domain Events?

Domain events are notifications that something significant happened in the domain. They capture **facts** about state changes within your bounded context.

```
Domain Event = "Something happened that domain experts care about"
```

**Examples:**
- `PatientCreatedEvent` - A new patient was registered
- `PatientSuspendedEvent` - A patient's status changed to suspended
- `AppointmentScheduledEvent` - An appointment was booked
- `AppointmentCancelledEvent` - An appointment was cancelled

---

## Why Domain Events?

### The Problem: Tightly Coupled Side Effects

Without domain events, behavior methods become polluted with side effects:

```csharp
// Bad: Entity knows too much
public class Patient
{
    public void Suspend(
        IEmailService emailService,       // Dependency on infrastructure
        IAuditLogger auditLogger,         // Another dependency
        IBillingService billingService)   // Yet another
    {
        Status = PatientStatus.Suspended;

        // Side effects mixed with domain logic
        emailService.SendSuspensionNotice(this);
        auditLogger.Log($"Patient {Id} suspended");
        billingService.PauseInvoicing(Id);
    }
}
```

**Problems:**
- Entity depends on infrastructure services
- Hard to test (must mock everything)
- Adding new reactions requires changing the entity
- Violates Single Responsibility Principle

### The Solution: Domain Events for Decoupling

With domain events, the entity just records what happened:

```csharp
// Good: Entity raises event, handlers react
public class Patient : Entity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public void Suspend()
    {
        if (Status == PatientStatus.Suspended)
            return;

        Status = PatientStatus.Suspended;

        // Just record what happened
        AddDomainEvent(new PatientSuspendedEvent(Id, DateTime.UtcNow));
    }

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

Handlers react to the event independently:

```csharp
// Each handler does one thing
public class SendSuspensionNoticeHandler : INotificationHandler<PatientSuspendedEvent>
{
    public Task Handle(PatientSuspendedEvent evt, CancellationToken ct)
    {
        // Send email notification
    }
}

public class AuditPatientSuspensionHandler : INotificationHandler<PatientSuspendedEvent>
{
    public Task Handle(PatientSuspendedEvent evt, CancellationToken ct)
    {
        // Log audit entry
    }
}

public class PauseBillingHandler : INotificationHandler<PatientSuspendedEvent>
{
    public Task Handle(PatientSuspendedEvent evt, CancellationToken ct)
    {
        // Pause invoicing
    }
}
```

**Benefits:**
- Entity stays focused on domain logic
- Easy to add new reactions (just add a handler)
- Each handler is independently testable
- Follows Open/Closed Principle

---

## Domain Events Infrastructure

### IDomainEvent Interface

```csharp
// BuildingBlocks/BuildingBlocks.Domain/IDomainEvent.cs
using MediatR;

namespace BuildingBlocks.Domain;

/// <summary>
/// Marker interface for domain events.
/// Domain events are dispatched internally via MediatR.
/// </summary>
public interface IDomainEvent : INotification
{
    /// <summary>
    /// When the event occurred
    /// </summary>
    DateTime OccurredAt { get; }
}
```

### IHasDomainEvents Interface

```csharp
// BuildingBlocks/BuildingBlocks.Domain/IHasDomainEvents.cs
namespace BuildingBlocks.Domain;

/// <summary>
/// Indicates an entity can raise domain events.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
```

### Domain Event Base Class

```csharp
// BuildingBlocks/BuildingBlocks.Domain/DomainEventBase.cs
namespace BuildingBlocks.Domain;

public abstract record DomainEventBase : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

---

## Implementing Domain Events in Entities

### Step 1: Entity Raises Events

```csharp
// Scheduling.Domain/Patients/Patient.cs
using BuildingBlocks.Domain;

namespace Scheduling.Domain.Patients;

public class Patient : Entity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public string Email { get; private set; }
    public PatientStatus Status { get; private set; }

    // Expose domain events
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;
    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    private Patient() { }

    public static Patient Create(
        string firstName,
        string lastName,
        string email,
        DateTime dateOfBirth)
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            DateOfBirth = dateOfBirth,
            Status = PatientStatus.Active
        };

        // Raise domain event
        patient.AddDomainEvent(new PatientCreatedEvent(patient.Id, patient.Email));

        return patient;
    }

    public void Suspend()
    {
        if (Status == PatientStatus.Suspended)
            return;

        Status = PatientStatus.Suspended;

        // Raise domain event
        AddDomainEvent(new PatientSuspendedEvent(Id));
    }
}
```

### Step 2: Define Domain Events

```csharp
// Scheduling.Domain/Patients/Events/PatientCreatedEvent.cs
using BuildingBlocks.Domain;

namespace Scheduling.Domain.Patients.Events;

public record PatientCreatedEvent(
    Guid PatientId,
    string Email
) : DomainEventBase;
```

```csharp
// Scheduling.Domain/Patients/Events/PatientSuspendedEvent.cs
using BuildingBlocks.Domain;

namespace Scheduling.Domain.Patients.Events;

public record PatientSuspendedEvent(
    Guid PatientId
) : DomainEventBase;
```

### Step 3: Handle Domain Events

Domain event handlers use MediatR's `INotificationHandler<T>`. Handlers are responsible for:
- Internal side effects (logging, auditing, cache invalidation)
- Queueing integration events for cross-bounded context communication

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
        // Internal side effect: logging
        _logger.LogInformation("Patient created: {PatientId}", notification.PatientId);

        // Queue integration event for cross-BC communication
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

---

## Domain Events vs Integration Events

| Aspect | Domain Events | Integration Events |
|--------|---------------|-------------------|
| **Scope** | Within bounded context | Across bounded contexts |
| **Transport** | In-memory (MediatR) | Message broker (RabbitMQ) |
| **Durability** | Not durable | Durable (persisted in queue) |
| **Timing** | Dispatched before/after save | Published after save |
| **Handlers** | `INotificationHandler<T>` | `IConsumer<T>` (MassTransit) |
| **Location** | `BC.Domain/Events/` | `Shared/IntegrationEvents/` |

### When to Use Each

```
Domain Events:
- Internal side effects within the same context
- Audit logging
- Cache invalidation
- Sending notifications
- Triggering internal workflows

Integration Events:
- Cross-context communication
- Notifying other microservices
- External system integration
- Event sourcing across boundaries
```

---

## Folder Structure

```
Core/Scheduling/
+-- Scheduling.Domain/
|   +-- Patients/
|       +-- Patient.cs              <- Entity with AddDomainEvent()
|       +-- PatientStatus.cs
|       +-- Events/
|           +-- PatientCreatedEvent.cs
|           +-- PatientSuspendedEvent.cs
|
+-- Scheduling.Application/
    +-- Patients/
        +-- EventHandlers/
            +-- PatientCreatedEventHandler.cs
            +-- PatientSuspendedEventHandler.cs
```

---

## The Full Picture

Domain events and integration events work together. The complete flow:

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

**Key points:**
- Entity raises domain event during state change
- Domain event handler performs internal side effects AND queues integration event
- Integration events are published only after the transaction commits
- This keeps command handlers clean while ensuring cross-BC communication

---

## Current Project Implementation

This project uses **domain event handlers to queue integration events**. Command handlers are kept clean, focusing only on entity operations:

```csharp
// Command handler - clean and focused
public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
{
    var patient = Patient.Create(
        cmd.FirstName,
        cmd.LastName,
        cmd.Email,
        cmd.DateOfBirth);

    _uow.RepositoryFor<Patient>().Add(patient);
    await _uow.SaveChangesAsync(ct);  // Triggers domain event dispatch
    return patient.Id;
}
```

Domain event handlers listen via MediatR and queue integration events:

```csharp
// Domain event handler - handles side effects
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

        // Queue integration event for cross-BC communication
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

**Benefits of this approach:**
- Command handlers stay simple and focused on the core operation
- Side effects are handled in dedicated, testable handlers
- Easy to add new reactions without modifying command handlers
- Clear separation between domain operations and integration concerns

---

## Verification Checklist

- [ ] `IDomainEvent` interface created (inherits `INotification`)
- [ ] `IHasDomainEvents` interface created
- [ ] `DomainEventBase` record created
- [ ] Entity implements `IHasDomainEvents`
- [ ] Entity has `AddDomainEvent()` method
- [ ] Domain events defined in `BC.Domain/Entity/Events/`
- [ ] Event handlers implement `INotificationHandler<T>`

---

> Next: [05-repository-pattern.md](./05-repository-pattern.md) - Repository pattern for persistence abstraction
