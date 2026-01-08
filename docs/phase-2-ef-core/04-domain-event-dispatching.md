# Domain Event Publishing

## Overview

Domain events are published **explicitly** by command handlers after a successful save. This gives full control over when events fire.

```csharp
// In command handler
await _unitOfWork.SaveChangesAsync(ct);                    // 1. Save first
await _mediator.Publish(new PatientCreatedEvent(...), ct); // 2. Then publish
```

---

## Why Explicit Publishing?

### Alternative: Automatic Dispatching

Some implementations automatically dispatch events after `SaveChanges()`:

```csharp
// UnitOfWork with automatic dispatch
public async Task<int> SaveChangesAsync(CancellationToken ct)
{
    var result = await _context.SaveChangesAsync(ct);
    await DispatchDomainEvents();  // Automatic - hidden magic
    return result;
}
```

**Problems with automatic:**
- Hidden behavior - not obvious when events fire
- Less control - can't skip events in certain cases
- Harder to debug

### Our Approach: Explicit Publishing

```csharp
// Handler has full control
public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
{
    var patient = Patient.Create(...);
    _unitOfWork.RepositoryFor<Patient>().Add(patient);
    await _unitOfWork.SaveChangesAsync(ct);

    // Explicit - you see exactly what happens
    await _mediator.Publish(new PatientCreatedEvent(
        patient.Id, patient.FirstName!, patient.LastName!, patient.Email!), ct);

    return patient.Id;
}
```

**Benefits:**
- Clear intent - events are visible in the code
- Full control - decide when/if to publish
- No magic - easier to debug and understand
- Flexible - can conditionally skip events

---

## What You Need To Do

### Step 1: Ensure MediatR is registered

Location: `Core/Scheduling/Scheduling.Application/ServiceCollectionExtensions.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ServiceCollectionExtensions).Assembly));

        return services;
    }
}
```

### Step 2: Wire up in Program.cs

Location: `WebApi/Program.cs`

```csharp
using Scheduling.Infrastructure;
using Scheduling.Application;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Add Infrastructure (DbContext, UnitOfWork)
builder.Services.AddSchedulingInfrastructure(connectionString);

// Add Application (MediatR handlers)
builder.Services.AddSchedulingApplication();

var app = builder.Build();
// ...
```

### Step 3: Create event handlers

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

### Step 4: Publish events in command handlers (Phase 3)

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
        var patient = Patient.Create(
            cmd.FirstName, cmd.LastName, cmd.Email, cmd.DateOfBirth, cmd.PhoneNumber);

        _unitOfWork.RepositoryFor<Patient>().Add(patient);
        await _unitOfWork.SaveChangesAsync(ct);

        // Publish event after successful save
        await _mediator.Publish(new PatientCreatedEvent(
            patient.Id,
            patient.FirstName!,
            patient.LastName!,
            patient.Email!), ct);

        return patient.Id;
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

// Handler 3: Analytics
public class TrackPatientCreatedHandler : INotificationHandler<PatientCreatedEvent>
{
    public Task Handle(PatientCreatedEvent e, CancellationToken ct)
    {
        _analytics.Track("PatientCreated", e.PatientId);
        return Task.CompletedTask;
    }
}
```

All handlers run when the event is published.

---

## Folder Structure

```
Core/Scheduling/
├── Scheduling.Application/
│   ├── ServiceCollectionExtensions.cs
│   └── Patients/
│       ├── Commands/                      ← Publish events here
│       │   └── CreatePatient/
│       │       └── CreatePatientCommandHandler.cs
│       ├── Events/                        ← Event definitions
│       │   ├── PatientCreatedEvent.cs
│       │   └── PatientSuspendedEvent.cs
│       └── EventHandlers/                 ← Handle events here
│           └── PatientCreatedEventHandler.cs
└── Scheduling.Domain/
    └── Patients/
        ├── Patient.cs
        └── PatientStatus.cs
```

---

## Verification Checklist

- [ ] MediatR registered in Application layer
- [ ] Event handlers implement `INotificationHandler<TEvent>`
- [ ] Command handlers inject `IMediator`
- [ ] Events published after `SaveChangesAsync()` succeeds
- [ ] Event handlers receive events when published

---

## Phase 2 Complete!

You now have:
- EF Core DbContext with proper configuration
- Generic Repository and UnitOfWork
- Database migrations
- Event handlers ready for explicit publishing

**Next: Phase 3 - CQRS Pattern**

We'll implement:
- Commands and Command Handlers (with event publishing)
- Queries and Query Handlers
- MediatR pipeline behaviors
- Validation with FluentValidation
