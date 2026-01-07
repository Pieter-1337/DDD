# Domain Event Dispatching

## Overview

Domain events are collected in entities but need to be **dispatched** after the database save succeeds. This ensures:

1. Events only fire if the save succeeds
2. Handlers can react to committed changes
3. Side effects (emails, notifications) happen after data is persisted

---

## The Pattern

```
1. Entity raises event     → patient.AddDomainEvent(new PatientCreatedEvent(...))
2. Repository saves        → await _unitOfWork.SaveChangesAsync()
3. UnitOfWork intercepts   → Saves to DB, then collects all events from tracked entities
4. Save to database        → Transaction commits
5. Dispatch events         → MediatR publishes to handlers
6. Clear events            → Prevent duplicate dispatching
```

---

## What You Need To Do

### Step 1: MediatR package (via Central Package Management)

MediatR is already included in the shared `BuildingBlocks.Domain` and `BuildingBlocks.Infrastructure` projects via Central Package Management.

`Directory.Packages.props` (at solution root):
```xml
<PackageVersion Include="MediatR" Version="12.4.1" />
```

### Step 2: IDomainEvent extends INotification (already done)

Location: `BuildingBlocks/BuildingBlocks.Domain/Events/IDomainEvent.cs`

```csharp
using MediatR;

namespace BuildingBlocks.Domain.Events;

public interface IDomainEvent : INotification
{
    DateTime OccurredOn { get; }
}
```

This allows MediatR to publish domain events.

### Step 3: IHasDomainEvents interface (already exists)

Location: `BuildingBlocks/BuildingBlocks.Domain/Events/IHasDomainEvents.cs`

```csharp
namespace BuildingBlocks.Domain.Events;

public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
```

The `Entity` base class implements both `IEntityBase` and `IHasDomainEvents`.

### Step 4: IDomainEventDispatcher interface (already exists)

Location: `BuildingBlocks/BuildingBlocks.Infrastructure/Events/IDomainEventDispatcher.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure.Events;

public interface IDomainEventDispatcher
{
    Task DispatchEventsAsync(DbContext context, CancellationToken ct);
}
```

### Step 5: DomainEventDispatcher implementation (already exists)

Location: `BuildingBlocks/BuildingBlocks.Infrastructure/Events/DomainEventDispatcher.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Domain.Events;
using BuildingBlocks.Domain.Interfaces;

namespace BuildingBlocks.Infrastructure.Events;

public class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IMediator _mediator;

    public DomainEventDispatcher(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task DispatchEventsAsync(DbContext context, CancellationToken ct = default)
    {
        // Get all entities with domain events
        var entitiesWithEvents = context.ChangeTracker
            .Entries<IEntityBase>()
            .Where(e => e.Entity is IHasDomainEvents entityWithEvents
                && entityWithEvents.DomainEvents.Any())
            .Select(e => (IHasDomainEvents)e.Entity)
            .ToList();

        // Collect all events
        var domainEvents = entitiesWithEvents
            .SelectMany(e => e.DomainEvents)
            .ToList();

        // Clear events from entities (prevent re-dispatching)
        foreach (var entity in entitiesWithEvents)
        {
            entity.ClearDomainEvents();
        }

        // Dispatch each event
        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent, ct);
        }
    }
}
```

### Step 6: UnitOfWork calls dispatcher (already implemented)

Location: `BuildingBlocks/BuildingBlocks.Infrastructure/UnitOfWork.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Domain.Interfaces;
using BuildingBlocks.Infrastructure.Events;

namespace BuildingBlocks.Infrastructure;

public class UnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public UnitOfWork(TContext context, IDomainEventDispatcher domainEventDispatcher)
    {
        _context = context;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Save first (so events only fire on success)
        var result = await _context.SaveChangesAsync(cancellationToken);

        // Then dispatch events
        await _domainEventDispatcher.DispatchEventsAsync(_context, cancellationToken);

        return result;
    }

    public IRepository<T> RepositoryFor<T>() where T : class, IEntityBase
    {
       return new Repository<TContext, T>(_context);
    }
}
```

### Step 7: Register services in Infrastructure

Location: `Core/Scheduling/Scheduling.Infrastructure/ServiceCollectionExtensions.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Domain.Interfaces;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Events;
using Scheduling.Infrastructure.Persistence;

namespace Scheduling.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<SchedulingDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IUnitOfWork, UnitOfWork<SchedulingDbContext>>();

        return services;
    }
}
```

### Step 8: Register MediatR in Application layer

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

### Step 9: Wire up in Program.cs

Location: `WebApi/Program.cs`

```csharp
using Scheduling.Infrastructure;
using Scheduling.Application;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Add Infrastructure (DbContext, UnitOfWork, EventDispatcher)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddSchedulingInfrastructure(connectionString);

// Add Application (MediatR handlers)
builder.Services.AddSchedulingApplication();

var app = builder.Build();
// ... rest of file
```

### Step 10: Create event handlers

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

## Testing It

1. Run the WebApi project: `dotnet run --project WebApi`
2. Create a patient via POST to `/api/patients`
3. Check the logs - you should see the event handler message

---

## Folder Structure

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
        ├── IDomainEventDispatcher.cs      ← Interface
        └── DomainEventDispatcher.cs       ← Implementation
Core/
└── Scheduling/
    ├── Scheduling.Application/
    │   ├── ServiceCollectionExtensions.cs
    │   └── Patients/
    │       └── EventHandlers/
    │           └── PatientCreatedEventHandler.cs
    └── Scheduling.Infrastructure/
        ├── ServiceCollectionExtensions.cs
        └── Persistence/
            └── SchedulingDbContext.cs
```

---

## Verification Checklist

- [ ] `IDomainEvent` extends MediatR's `INotification`
- [ ] `IDomainEventDispatcher` interface exists
- [ ] `DomainEventDispatcher` implementation exists
- [ ] `UnitOfWork` calls dispatcher after `SaveChangesAsync()`
- [ ] Infrastructure registers `IDomainEventDispatcher`
- [ ] Application registers MediatR handlers
- [ ] Event handlers receive events when entities are saved

---

## Phase 2 Complete!

You now have:
- EF Core DbContext with proper configuration
- Generic Repository and UnitOfWork
- Database migrations
- Domain event dispatching via MediatR

**Next: Phase 3 - CQRS Pattern**

We'll implement:
- Commands and Command Handlers
- Queries and Query Handlers
- MediatR pipeline behaviors
- Validation with FluentValidation
