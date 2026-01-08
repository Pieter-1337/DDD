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
public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
{
    var patient = Patient.Create(...);
    _unitOfWork.RepositoryFor<Patient>().Add(patient);
    await _unitOfWork.SaveChangesAsync(ct);

    // Side effects mixed with main logic
    await _emailService.SendWelcomeEmail(patient.Email);  // What if this fails?
    await _analyticsService.Track("PatientCreated");       // Tight coupling!

    return patient.Id;
}
```

**Problems:**
- Handler does too much
- Hard to add new reactions without modifying handler
- What if email fails after patient is saved?

With events:
```csharp
// Entity raises event in behavior method
public static Patient Create(...)
{
    var patient = new Patient { ... };
    patient.AddDomainEvent(new PatientCreatedEvent(...));  // Event tied to behavior
    return patient;
}

// Handler is clean - just saves
public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
{
    var patient = Patient.Create(...);
    _unitOfWork.RepositoryFor<Patient>().Add(patient);
    await _unitOfWork.SaveChangesAsync(ct);  // Events auto-dispatched!
    return patient.Id;
}

// Separate handler for side effects
public class SendWelcomeEmailHandler : INotificationHandler<PatientCreatedEvent>
{
    public async Task Handle(PatientCreatedEvent e, CancellationToken ct)
    {
        await _emailService.SendWelcomeEmail(e.Email);
    }
}
```

**Benefits:**
- Handler stays focused on main operation
- Events tied to behavior - can't forget them
- Easy to add new reactions without changing handler
- Each handler can be tested independently

---

## Our Approach: Entity Collects, Auto-Dispatch

Entities collect domain events in their behavior methods. Events are automatically dispatched after `SaveChangesAsync()`.

```csharp
// Entity raises events in behavior
patient.Suspend();  // Adds PatientSuspendedEvent internally

// UnitOfWork auto-dispatches after save
await _unitOfWork.SaveChangesAsync(ct);  // Events dispatched here
```

**Why this approach?**
- Events tied to behavior - can't forget to raise them
- Handler doesn't need to know what events to publish
- Automatic dispatch ensures consistency
- Handler can still publish additional "composite" events explicitly if needed

---

## What You Need To Do

### Step 1: Base event interface

Location: `BuildingBlocks/BuildingBlocks.Domain/Events/IDomainEvent.cs`

```csharp
using MediatR;

namespace BuildingBlocks.Domain.Events;

public interface IDomainEvent : INotification
{
    DateTime OccurredOn { get; }
}
```

### Step 2: IHasDomainEvents interface

Location: `BuildingBlocks/BuildingBlocks.Domain/Events/IHasDomainEvents.cs`

```csharp
namespace BuildingBlocks.Domain.Events;

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
```

### Step 3: Entity base class with event collection

Location: `BuildingBlocks/BuildingBlocks.Domain/Entity.cs`

```csharp
using BuildingBlocks.Domain.Events;
using BuildingBlocks.Application;

namespace BuildingBlocks.Domain;

public abstract class Entity : IEntityBase, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

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

### Step 6: Patient entity with event raising

Location: `Core/Scheduling/Scheduling.Domain/Patients/Patient.cs`

```csharp
using BuildingBlocks.Domain;
using Scheduling.Domain.Patients.Events;

namespace Scheduling.Domain.Patients;

public class Patient : Entity
{
    // ... properties ...

    public static Patient Create(
        string firstName,
        string lastName,
        string email,
        DateTime dateOfBirth,
        string? phoneNumber = null)
    {
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

        // Event tied to behavior - can't forget!
        patient.AddDomainEvent(new PatientCreatedEvent(
            patient.Id,
            patient.FirstName,
            patient.LastName,
            patient.Email));

        return patient;
    }

    public void Suspend()
    {
        if (Status == PatientStatus.Suspended)
            return;

        Status = PatientStatus.Suspended;
        AddDomainEvent(new PatientSuspendedEvent(Id));  // Event tied to behavior
    }
}
```

### Step 7: Create event handlers

Location: `Core/Scheduling/Scheduling.Application/Patients/EventHandlers/PatientCreatedEventHandler.cs`

```csharp
using MediatR;
using Microsoft.Extensions.Logging;
using Scheduling.Domain.Patients.Events;

namespace Scheduling.Application.Patients.EventHandlers;

public class PatientCreatedEventHandler : INotificationHandler<PatientCreatedEvent>
{
    private readonly ILogger<PatientCreatedEventHandler> _logger;

    public PatientCreatedEventHandler(ILogger<PatientCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(PatientCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Patient created: {PatientId} - {FirstName} {LastName} ({Email})",
            notification.PatientId,
            notification.FirstName,
            notification.LastName,
            notification.Email);

        // In real app: send welcome email, notify admin, etc.

        return Task.CompletedTask;
    }
}
```

---

## Verification Checklist

- [ ] `IDomainEvent` interface exists in `BuildingBlocks/BuildingBlocks.Domain/Events/`
- [ ] `IHasDomainEvents` interface exists in `BuildingBlocks/BuildingBlocks.Domain/Events/`
- [ ] `Entity` base class has event collection and `AddDomainEvent()` method
- [ ] `PatientCreatedEvent` in `Scheduling.Domain/Patients/Events/`
- [ ] `PatientSuspendedEvent` in `Scheduling.Domain/Patients/Events/`
- [ ] Patient entity uses `AddDomainEvent()` in behavior methods

---

## Folder Structure After This Step

```
BuildingBlocks/
└── BuildingBlocks.Domain/
    ├── Entity.cs                      ← Has event collection
    ├── Events/
    │   ├── IDomainEvent.cs            ← Marker interface for MediatR
    │   └── IHasDomainEvents.cs        ← Interface for entities with events
    └── Interfaces/
        ├── IEntityBase.cs
        ├── IRepository.cs
        └── IUnitOfWork.cs
Core/
└── Scheduling/
    ├── Scheduling.Domain/
    │   └── Patients/
    │       ├── Patient.cs             ← Uses AddDomainEvent()
    │       ├── PatientStatus.cs
    │       └── Events/                ← Events in Domain layer
    │           ├── PatientCreatedEvent.cs
    │           └── PatientSuspendedEvent.cs
    └── Scheduling.Application/
        └── Patients/
            └── EventHandlers/
                └── PatientCreatedEventHandler.cs
```

---

## What You Learned

1. **Domain events** capture facts about what happened
2. **Events decouple** the main operation from side effects
3. **Entity collects events** - tied to behavior, can't forget them
4. **Auto-dispatch** after SaveChanges ensures consistency
5. **Past tense naming** - `PatientCreated`, not `CreatePatient`
6. **MediatR integration** - `IDomainEvent` extends `INotification` for publishing

→ Next: [05-repository-pattern.md](./05-repository-pattern.md) - Abstracting persistence
