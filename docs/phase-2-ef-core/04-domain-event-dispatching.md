# Domain Event Dispatching

## Overview

Domain events are collected by entities and **automatically dispatched** by the UnitOfWork after a successful save.

```csharp
// Entity collects events in behavior
patient.Suspend();  // Adds PatientSuspendedEvent internally

// UnitOfWork auto-dispatches after save
await _unitOfWork.SaveChangesAsync(ct);  // Events dispatched here
```

---

## Why Auto-Dispatch?

### Alternative: Explicit Publishing

```csharp
// Handler has to remember to publish
patient.Suspend();
await _unitOfWork.SaveChangesAsync(ct);
await _mediator.Publish(new PatientSuspendedEvent(patient.Id), ct);  // Can forget!
```

**Problems with explicit:**
- Can forget to publish events
- Event not tied to behavior
- Duplicate code across handlers

### Our Approach: Entity Collects, Auto-Dispatch

```csharp
// Entity - event tied to behavior
public void Suspend()
{
    if (Status == PatientStatus.Suspended)
        return;

    Status = PatientStatus.Suspended;
    AddDomainEvent(new PatientSuspendedEvent(Id));  // Can't forget!
}

// Handler - clean, no event publishing needed
public async Task Handle(SuspendPatientCommand cmd, CancellationToken ct)
{
    var patient = await _repo.GetByIdAsync(cmd.PatientId, ct);
    patient.Suspend();
    await _unitOfWork.SaveChangesAsync(ct);  // Events auto-dispatched
}
```

**Benefits:**
- Events tied to behavior - can't forget them
- Handler stays clean
- Automatic dispatch ensures consistency
- Can still publish additional events explicitly if needed

---

## What You Need To Do

### Step 1: UnitOfWork with Auto-Dispatch

Location: `BuildingBlocks/BuildingBlocks.Infrastructure/UnitOfWork.cs`

```csharp
using BuildingBlocks.Application;
using BuildingBlocks.Domain.Events;
using BuildingBlocks.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure;

public class UnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IMediator _mediator;

    public UnitOfWork(TContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _context.SaveChangesAsync(cancellationToken);
        await DispatchDomainEventsAsync(cancellationToken);
        return result;
    }

    public IRepository<T> RepositoryFor<T>() where T : class, IEntityBase
    {
        return new Repository<TContext, T>(_context);
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        var entitiesWithEvents = _context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = entitiesWithEvents
            .SelectMany(e => e.DomainEvents)
            .ToList();

        // Clear events before dispatching to avoid re-dispatching
        foreach (var entity in entitiesWithEvents)
        {
            entity.ClearDomainEvents();
        }

        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }
    }
}
```

**Key points:**
- Injects `IMediator` for publishing
- After `SaveChangesAsync`, collects all entities with events
- Clears events before dispatching (prevents re-dispatch if handler saves)
- Publishes each event via MediatR

### Step 2: Ignore DomainEvents in EF Configuration

Location: `Core/Scheduling/Scheduling.Infrastructure/Persistence/Configurations/PatientConfiguration.cs`

```csharp
public void Configure(EntityTypeBuilder<Patient> builder)
{
    builder.ToTable("Patients");
    builder.HasKey(p => p.Id);
    builder.Ignore(p => p.DomainEvents);  // Don't persist events

    // ... rest of configuration
}
```

### Step 3: Wire up in Program.cs

Location: `WebApi/Program.cs`

```csharp
using Scheduling.Infrastructure;
using Scheduling.Application;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Add Infrastructure (DbContext, UnitOfWork with IMediator)
builder.Services.AddSchedulingInfrastructure(connectionString);

// Add Application (MediatR handlers)
builder.Services.AddSchedulingApplication();

var app = builder.Build();
// ...
```

### Step 4: Create event handlers

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

## Multiple Event Handlers

MediatR supports multiple handlers for the same event:

```csharp
// Handler 1: Logging
public class LogPatientCreatedHandler : INotificationHandler<PatientCreatedEvent>
{
    public Task Handle(PatientCreatedEvent e, CancellationToken ct)
    {
        _logger.LogInformation("Patient {Id} created", e.PatientId);
        return Task.CompletedTask;
    }
}

// Handler 2: Send email
public class SendWelcomeEmailHandler : INotificationHandler<PatientCreatedEvent>
{
    public async Task Handle(PatientCreatedEvent e, CancellationToken ct)
    {
        await _emailService.SendWelcomeEmail(e.Email);
    }
}
```

All handlers run when the event is dispatched.

---

## Explicit Events for Composite Scenarios

You can still publish events explicitly for scenarios not tied to single behavior:

```csharp
public async Task Handle(ProcessPatientAccountCommand cmd, CancellationToken ct)
{
    var patient = await _repo.GetByIdAsync(cmd.PatientId, ct);

    patient.Suspend();           // Adds PatientSuspendedEvent
    patient.UpdateNotes("...");  // No event

    await _unitOfWork.SaveChangesAsync(ct);  // Auto-dispatches PatientSuspendedEvent

    // Additional composite event
    await _mediator.Publish(new PatientAccountProcessedEvent(patient.Id), ct);
}
```

---

## Folder Structure

```
BuildingBlocks/
├── BuildingBlocks.Domain/
│   ├── Entity.cs                          ← Has event collection
│   ├── Interfaces/
│   │   └── IEntityBase.cs
│   └── Events/
│       ├── IDomainEvent.cs
│       └── IHasDomainEvents.cs
│
BuildingBlocks.Application/
├── IRepository.cs                         ← Repository interface
└── IUnitOfWork.cs                         ← Unit of Work interface
│
BuildingBlocks/
└── BuildingBlocks.Infrastructure/
    ├── Repository.cs
    └── UnitOfWork.cs                      ← Auto-dispatches events

Core/Scheduling/
├── Scheduling.Domain/
│   └── Patients/
│       ├── Patient.cs                     ← Uses AddDomainEvent()
│       └── Events/
│           ├── PatientCreatedEvent.cs
│           └── PatientSuspendedEvent.cs
└── Scheduling.Application/
    └── Patients/
        └── EventHandlers/
            └── PatientCreatedEventHandler.cs
```

---

## Verification Checklist

- [ ] UnitOfWork injects IMediator
- [ ] UnitOfWork dispatches events after SaveChangesAsync
- [ ] EF configuration ignores DomainEvents property
- [ ] Event handlers implement `INotificationHandler<TEvent>`
- [ ] Events are dispatched when published

---

## Phase 2 Complete!

You now have:
- EF Core DbContext with proper configuration
- Generic Repository and UnitOfWork
- Database migrations
- **Auto-dispatch of domain events after save**

**Next: Phase 3 - CQRS Pattern**

We'll implement:
- Commands and Command Handlers
- Queries and Query Handlers
- MediatR pipeline behaviors
- Validation with FluentValidation
