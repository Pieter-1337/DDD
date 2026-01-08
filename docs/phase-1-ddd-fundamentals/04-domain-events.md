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
// Handler - focused on main operation
public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
{
    var patient = Patient.Create(...);
    _unitOfWork.RepositoryFor<Patient>().Add(patient);
    await _unitOfWork.SaveChangesAsync(ct);

    // Publish event - others can react
    await _mediator.Publish(new PatientCreatedEvent(patient.Id, patient.Email!), ct);

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

// Another handler - analytics
public class TrackPatientCreatedHandler : INotificationHandler<PatientCreatedEvent>
{
    public Task Handle(PatientCreatedEvent e, CancellationToken ct)
    {
        _analytics.Track("PatientCreated", e.PatientId);
        return Task.CompletedTask;
    }
}
```

**Benefits:**
- Handler stays focused on main operation
- Easy to add new reactions without changing handler
- Each handler can be tested independently

---

## Our Approach: Explicit Publishing

We use **explicit event publishing** in command handlers rather than automatic dispatching. This gives full control over when and if events are published.

```csharp
// Handler explicitly publishes after successful save
await _unitOfWork.SaveChangesAsync(ct);
await _mediator.Publish(new PatientCreatedEvent(...), ct);
```

**Why explicit?**
- Full control - you decide when to publish
- Clear intent - events are visible in the handler
- No magic - easier to debug and reason about
- Flexible - can choose not to publish in certain cases

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

**Note:** `IDomainEvent` extends MediatR's `INotification` so events can be published via `_mediator.Publish()`.

### Step 2: Create PatientCreatedEvent

Location: `Core/Scheduling/Scheduling.Application/Patients/Events/PatientCreatedEvent.cs`

```csharp
using BuildingBlocks.Domain.Events;

namespace Scheduling.Application.Patients.Events;

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

### Step 3: Create PatientSuspendedEvent

Location: `Core/Scheduling/Scheduling.Application/Patients/Events/PatientSuspendedEvent.cs`

```csharp
using BuildingBlocks.Domain.Events;

namespace Scheduling.Application.Patients.Events;

public record PatientSuspendedEvent(Guid PatientId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
```

### Step 4: Entity base class (simple)

Location: `BuildingBlocks/BuildingBlocks.Domain/Entity.cs`

```csharp
using BuildingBlocks.Domain.Interfaces;

namespace BuildingBlocks.Domain;

public abstract class Entity : IEntityBase
{
    public Guid Id { get; set; }
}
```

**Note:** Entity is simple - just provides Id. No event collection. Events are published explicitly by handlers.

### Step 5: Patient entity (no event collection)

Location: `Core/Scheduling/Scheduling.Domain/Patients/Patient.cs`

```csharp
using BuildingBlocks.Domain;

namespace Scheduling.Domain.Patients;

public class Patient : Entity
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
        // ... more validation ...

        return new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email!.Trim().ToLowerInvariant(),
            PhoneNumber = phoneNumber?.Trim(),
            DateOfBirth = dateOfBirth,
            Status = PatientStatus.Active
        };
        // Note: No event raised here - handler will publish explicitly
    }

    public void Suspend()
    {
        if (Status == PatientStatus.Suspended)
            return;

        Status = PatientStatus.Suspended;
        // Note: No event raised here - handler will publish explicitly
    }
}
```

### Step 6: Publishing events in handlers (Phase 3 preview)

```csharp
public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;

    public CreatePatientCommandHandler(IUnitOfWork unitOfWork, IMediator mediator)
    {
        _unitOfWork = unitOfWork;
        _mediator = mediator;
    }

    public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
    {
        var patient = Patient.Create(cmd.FirstName, cmd.LastName, cmd.Email, cmd.DateOfBirth, cmd.PhoneNumber);

        _unitOfWork.RepositoryFor<Patient>().Add(patient);
        await _unitOfWork.SaveChangesAsync(ct);

        // Explicit publish after successful save
        await _mediator.Publish(new PatientCreatedEvent(
            patient.Id,
            patient.FirstName!,
            patient.LastName!,
            patient.Email!), ct);

        return patient.Id;
    }
}
```

### Step 7: Create event handlers

Location: `Core/Scheduling/Scheduling.Application/Patients/EventHandlers/PatientCreatedEventHandler.cs`

```csharp
using MediatR;
using Microsoft.Extensions.Logging;
using Scheduling.Application.Patients.Events;

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
- [ ] `PatientCreatedEvent` in `Scheduling.Application/Patients/Events/`
- [ ] `PatientSuspendedEvent` in `Scheduling.Application/Patients/Events/`
- [ ] Entity base class is simple (just Id)
- [ ] Patient entity has no event collection
- [ ] Events published explicitly by handlers

---

## Folder Structure After This Step

```
BuildingBlocks/
└── BuildingBlocks.Domain/
    ├── Entity.cs                      ← Simple, just Id
    ├── Events/
    │   └── IDomainEvent.cs            ← Marker interface for MediatR
    └── Interfaces/
        ├── IEntityBase.cs
        ├── IRepository.cs
        └── IUnitOfWork.cs
Core/
└── Scheduling/
    ├── Scheduling.Domain/
    │   └── Patients/
    │       ├── Patient.cs             ← No event collection
    │       └── PatientStatus.cs
    └── Scheduling.Application/
        └── Patients/
            ├── Events/                ← Events live in Application
            │   ├── PatientCreatedEvent.cs
            │   └── PatientSuspendedEvent.cs
            └── EventHandlers/
                └── PatientCreatedEventHandler.cs
```

---

## What You Learned

1. **Domain events** capture facts about what happened
2. **Events decouple** the main operation from side effects
3. **Explicit publishing** - handlers decide when to publish
4. **Past tense naming** - `PatientCreated`, not `CreatePatient`
5. **MediatR integration** - `IDomainEvent` extends `INotification` for publishing

→ Next: [05-repository-pattern.md](./05-repository-pattern.md) - Abstracting persistence
