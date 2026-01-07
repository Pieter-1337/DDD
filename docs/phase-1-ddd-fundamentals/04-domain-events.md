# Domain Events

## What Are Domain Events?

Domain events capture **something that happened** in your domain that other parts of the system might care about.

```
Patient Created → Send welcome email
Appointment Cancelled → Notify doctor, refund payment
Patient Suspended → Cancel upcoming appointments
```

They're named in **past tense** because they represent facts that already occurred.

---

## Why Domain Events?

Without events:
```csharp
public void Suspend()
{
    Status = PatientStatus.Suspended;
    _emailService.SendSuspensionEmail(Email);      // Domain depends on infrastructure!
    _appointmentService.CancelUpcoming(Id);         // Tight coupling!
}
```

**Problems:**
- Domain layer depends on email service, appointment service
- Hard to test
- Hard to add new reactions (logging, analytics, etc.)

With events:
```csharp
public void Suspend()
{
    Status = PatientStatus.Suspended;
    AddDomainEvent(new PatientSuspendedEvent(Id));  // Just record what happened
}

// Somewhere else (Application layer):
public class PatientSuspendedEventHandler
{
    public async Task Handle(PatientSuspendedEvent e)
    {
        await _emailService.SendSuspensionEmail(e.PatientId);
        await _appointmentService.CancelUpcoming(e.PatientId);
    }
}
```

**Benefits:**
- Domain stays pure (no infrastructure dependencies)
- Easy to add new handlers without changing domain
- Easy to test domain in isolation

---

## What You Need To Do

The domain event infrastructure lives in the shared `BuildingBlocks` projects. Domain entities inherit from the shared `Entity` base class in `BuildingBlocks.Domain` which provides event support.

### Step 1: Base event interface (already exists in BuildingBlocks.Domain)

Location: `BuildingBlocks/BuildingBlocks.Domain/Events/IDomainEvent.cs`

```csharp
using MediatR;

namespace BuildingBlocks.Domain.Events;

public interface IDomainEvent : INotification
{
    DateTime OccurredOn { get; }
}
```

**Note:** `IDomainEvent` extends MediatR's `INotification` so events can be published via MediatR.

### Step 2: Interface for entities with events (already exists in BuildingBlocks.Domain)

Location: `BuildingBlocks/BuildingBlocks.Domain/Events/IHasDomainEvents.cs`

```csharp
namespace BuildingBlocks.Domain.Events;

public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
```

### Step 3: Base entity with event support (already exists in BuildingBlocks.Domain)

Location: `BuildingBlocks/BuildingBlocks.Domain/Entity.cs`

```csharp
using BuildingBlocks.Domain.Events;
using BuildingBlocks.Domain.Interfaces;

namespace BuildingBlocks.Domain;

public abstract class Entity : IEntityBase, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; set; }
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
```

**Key points:**
- Implements `IEntityBase` (provides `Id`) and `IHasDomainEvents`
- Domain entities inherit from this class
- Events are collected via `AddDomainEvent()` and cleared after dispatching

### Step 4: Create PatientCreatedEvent

Location: `Core/Scheduling/Scheduling.Domain/Patients/Events/PatientCreatedEvent.cs`

```csharp
using BuildingBlocks.Domain.Events;

namespace Scheduling.Domain.Patients.Events;

public record PatientCreatedEvent(
    Guid PatientId,
    string FirstName,
    string LastName,
    string Email
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
```

### Step 5: Create PatientSuspendedEvent

Location: `Core/Scheduling/Scheduling.Domain/Patients/Events/PatientSuspendedEvent.cs`

```csharp
using BuildingBlocks.Domain.Events;

namespace Scheduling.Domain.Patients.Events;

public record PatientSuspendedEvent(Guid PatientId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
```

### Step 6: Update Patient to inherit from Entity and raise events

Location: `Core/Scheduling/Scheduling.Domain/Patients/Patient.cs`

```csharp
using BuildingBlocks.Domain;
using Scheduling.Domain.Patients.Events;

namespace Scheduling.Domain.Patients;

public class Patient : Entity  // Inherits from shared Entity base class
{
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public DateTime DateOfBirth { get; private set; }
    public PatientStatus Status { get; private set; }

    private Patient() { }

    public static Patient Create(
        string? firstName,
        string? lastName,
        string? email,
        DateTime dateOfBirth,
        string? phoneNumber = null)
    {
        // Validation - enforce invariants
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required", nameof(firstName));

        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name is required", nameof(lastName));

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required", nameof(email));

        if (!email.Contains('@'))
            throw new ArgumentException("Invalid email format", nameof(email));

        if (dateOfBirth > DateTime.UtcNow)
            throw new ArgumentException("Date of birth cannot be in the future", nameof(dateOfBirth));

        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            PhoneNumber = phoneNumber?.Trim(),
            DateOfBirth = dateOfBirth,
            Status = PatientStatus.Active
        };

        // Raise event
        patient.AddDomainEvent(new PatientCreatedEvent(
            patient.Id,
            patient.FirstName,
            patient.LastName,
            patient.Email
        ));

        return patient;
    }

    public void Suspend()
    {
        if (Status == PatientStatus.Suspended)
            return;

        Status = PatientStatus.Suspended;

        // Raise event
        AddDomainEvent(new PatientSuspendedEvent(Id));
    }

    // ... rest of methods ...
}
```

### Step 7: Add tests for events

Location: `Core/Scheduling/Scheduling.Domain.Tests/Patients/PatientTests.cs`

```csharp
using FluentAssertions;
using Scheduling.Domain.Patients;
using Scheduling.Domain.Patients.Events;

namespace Scheduling.Domain.Tests.Patients;

public class PatientTests
{
    [Fact]
    public void Create_ShouldRaisePatientCreatedEvent()
    {
        // Arrange & Act
        var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));

        // Assert
        patient.DomainEvents.Should().ContainSingle();
        patient.DomainEvents.First().Should().BeOfType<PatientCreatedEvent>();

        var @event = (PatientCreatedEvent)patient.DomainEvents.First();
        @event.PatientId.Should().Be(patient.Id);
        @event.Email.Should().Be("test@example.com");
    }

    [Fact]
    public void Suspend_ShouldRaisePatientSuspendedEvent()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));
        patient.ClearDomainEvents(); // Clear the Created event

        // Act
        patient.Suspend();

        // Assert
        patient.DomainEvents.Should().ContainSingle();
        patient.DomainEvents.First().Should().BeOfType<PatientSuspendedEvent>();
    }

    [Fact]
    public void Suspend_WhenAlreadySuspended_ShouldNotRaiseEvent()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));
        patient.Suspend();
        patient.ClearDomainEvents();

        // Act
        patient.Suspend(); // Second time

        // Assert
        patient.DomainEvents.Should().BeEmpty(); // No new event
    }
}
```

---

## How Events Get Dispatched (Preview)

Events are collected in the entity, then dispatched via MediatR when you save. This is handled by the `DomainEventDispatcher` in the `BuildingBlocks.Infrastructure` project:

```csharp
// BuildingBlocks/BuildingBlocks.Infrastructure/Events/DomainEventDispatcher.cs
public class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IMediator _mediator;

    public DomainEventDispatcher(IMediator mediator) => _mediator = mediator;

    public async Task DispatchEventsAsync(DbContext context, CancellationToken ct = default)
    {
        var entitiesWithEvents = context.ChangeTracker
            .Entries<IEntityBase>()
            .Where(e => e.Entity is IHasDomainEvents entityWithEvents
                && entityWithEvents.DomainEvents.Any())
            .Select(e => (IHasDomainEvents)e.Entity)
            .ToList();

        var domainEvents = entitiesWithEvents
            .SelectMany(e => e.DomainEvents)
            .ToList();

        foreach (var entity in entitiesWithEvents)
            entity.ClearDomainEvents();

        foreach (var domainEvent in domainEvents)
            await _mediator.Publish(domainEvent, ct);
    }
}
```

The `UnitOfWork` calls this dispatcher after saving changes. We'll see this in detail in Phase 2.

---

## Verification Checklist

- [ ] `IDomainEvent` interface exists in `BuildingBlocks/BuildingBlocks.Domain/Events/`
- [ ] `IHasDomainEvents` interface exists in `BuildingBlocks/BuildingBlocks.Domain/Events/`
- [ ] `Entity` base class exists in `BuildingBlocks/BuildingBlocks.Domain/`
- [ ] `Patient` inherits from `Entity`
- [ ] `PatientCreatedEvent` raised in `Create()`
- [ ] `PatientSuspendedEvent` raised in `Suspend()`
- [ ] Tests verify events are raised
- [ ] Tests pass

---

## Folder Structure After This Step

```
BuildingBlocks/
├── BuildingBlocks.Domain/                 ← Pure domain abstractions
│   ├── Entity.cs
│   ├── Events/
│   │   ├── IDomainEvent.cs
│   │   └── IHasDomainEvents.cs
│   └── Interfaces/
│       ├── IEntityBase.cs
│       ├── IRepository.cs
│       └── IUnitOfWork.cs
└── BuildingBlocks.Infrastructure/         ← Infrastructure implementations
    ├── Repository.cs
    ├── UnitOfWork.cs
    └── Events/
        ├── IDomainEventDispatcher.cs
        └── DomainEventDispatcher.cs
Core/
└── Scheduling/
    ├── Scheduling.Domain/
    │   └── Patients/
    │       ├── Patient.cs
    │       ├── PatientStatus.cs
    │       └── Events/
    │           ├── PatientCreatedEvent.cs
    │           └── PatientSuspendedEvent.cs
    └── Scheduling.Domain.Tests/
        └── Patients/
            └── PatientTests.cs
```

---

## What You Learned

1. **Domain events** capture facts about what happened
2. **Events decouple** the domain from side effects
3. **Events are raised** in the domain, **dispatched** by infrastructure
4. **Past tense naming** - `PatientCreated`, not `CreatePatient`
5. **MediatR integration** - `IDomainEvent` extends `INotification` for publishing

→ Next: [05-repository-pattern.md](./05-repository-pattern.md) - Abstracting persistence
