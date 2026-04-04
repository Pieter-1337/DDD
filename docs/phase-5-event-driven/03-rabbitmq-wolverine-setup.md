# RabbitMQ + Wolverine Setup

This document provides a complete guide for setting up Wolverine as a messaging framework for event-driven communication between bounded contexts. Wolverine is an MIT-licensed alternative to MassTransit that uses convention-based handler discovery and minimal configuration.

---

## 1. Overview: Why Wolverine and RabbitMQ?

### Why Wolverine?

Wolverine is a modern .NET messaging framework by Jeremy Miller (author of Marten, Lamar, and StructureMap) that eliminates boilerplate code through conventions.

**Without Wolverine** (raw RabbitMQ):
```csharp
var factory = new ConnectionFactory { HostName = "localhost" };
using var connection = factory.CreateConnection();
using var channel = connection.CreateModel();

channel.QueueDeclare("patient-created", durable: true, exclusive: false, autoDelete: false);
var body = JsonSerializer.SerializeToUtf8Bytes(message);
channel.BasicPublish(exchange: "", routingKey: "patient-created", body: body);
```

**With Wolverine**:
```csharp
await _messageBus.PublishAsync(new PatientCreatedIntegrationEvent { ... });
```

### Key Characteristics

| Feature | Description |
|---------|-------------|
| **License** | MIT (free forever for commercial and personal use) |
| **Handler discovery** | Convention-based — no explicit registration needed |
| **Method injection** | Dependencies can be injected into handler method parameters |
| **No base classes** | Plain classes with `Handle()` methods |
| **Auto-provisioning** | Automatically creates queues, exchanges, and outbox tables |
| **Transport support** | RabbitMQ, Azure Service Bus, Amazon SQS |
| **Active maintenance** | Actively maintained by Jeremy Miller |

### Why RabbitMQ?

RabbitMQ is a proven message broker that provides reliable message delivery between distributed services. We'll run it in Docker for easy setup and teardown during development.

### When to Use Wolverine

Wolverine is a good choice when:
- You want MIT-licensed open source forever
- You prefer convention over configuration
- You're using Marten for event sourcing (native integration)
- You want method injection for handler dependencies
- You prefer simpler handler code (no interfaces/base classes)

Wolverine provides:
- RabbitMQ, Azure Service Bus, Amazon SQS support
- Retry policies and dead letter queues
- Message serialization
- Testing harnesses

---

## 2. Wolverine Licensing

**Wolverine is MIT-licensed** — free forever for commercial and personal use. There are no licensing tiers, no production restrictions, and no cost changes planned.

This makes Wolverine a compelling alternative to messaging frameworks that have moved to commercial licensing models.

> **Note**: For an alternative messaging framework with mature enterprise patterns and saga support, see [02-rabbitmq-masstransit-setup.md](./02-rabbitmq-masstransit-setup.md).

---

## 3. Architecture: Project Structure for Messaging

This setup separates messaging concerns across multiple projects, following Clean Architecture and the Dependency Inversion Principle.

### Core Principle

All events are:
- Published to RabbitMQ via Wolverine
- Defined in `Shared/IntegrationEvents/`
- Queued in command handlers via `_uow.QueueIntegrationEvent()`
- Published after `SaveChangesAsync()` succeeds

| Project | Purpose | Contains |
|---------|---------|----------|
| **BuildingBlocks.Application/Messaging** | Application-layer abstractions | `IEventBus`, `IIntegrationEvent`, `IntegrationEventBase` |
| **BuildingBlocks.Application/Interfaces** | Unit of work with event queuing | `IUnitOfWork.QueueIntegrationEvent()` |
| **BuildingBlocks.Infrastructure.Wolverine** | Wolverine provider | `WolverineEventBus`, `AddWolverineEventBus()` extension |
| **Shared/IntegrationEvents** | Integration event definitions | `PatientCreatedIntegrationEvent`, etc. |
| **[BC].Infrastructure/Consumers/Wolverine** | Wolverine handlers | Handles incoming integration events |
| **[BC].WebApi** | Composition root | Wolverine registration with Bounded context handlers |

### Key Interfaces

**IEventBus** (BuildingBlocks.Application.Messaging):
```csharp
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class, IIntegrationEvent;
}
```

**IUnitOfWork** (BuildingBlocks.Application.Interfaces):
```csharp
public interface IUnitOfWork
{
    // ... repository methods ...

    /// <summary>
    /// Queues an integration event to be published after a successful save.
    /// </summary>
    void QueueIntegrationEvent(IIntegrationEvent integrationEvent);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

**IIntegrationEvent** (BuildingBlocks.Application.Messaging):
```csharp
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTime OccurredOn { get; }
}
```

### Publishing Flow

1. Command handler creates entity
2. Command handler calls `_uow.QueueIntegrationEvent(new PatientCreatedIntegrationEvent(...))`
3. Command handler calls `await _uow.SaveChangesAsync()`
4. `SaveChangesAsync()` commits the database transaction
5. After successful commit, queued integration events are published to RabbitMQ via `IEventBus`

This ensures events are only published if the database save succeeds.

### Where Wolverine Gets Registered

Wolverine is registered in `WebApplications/Scheduling.WebApi/Program.cs`

```csharp
// WebApplications/Scheduling.WebApi/Program.cs
builder.AddWolverineEventBus(opts =>
{
    // Handler discovery is filtered by IIntegrationEvent in WolverineExtensions
    // Just specify which assemblies to scan
    opts.Discovery.IncludeAssembly(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});
```

### What Lives Where

**BuildingBlocks.Application/Messaging** provides:
- `IEventBus` - abstraction for publishing integration events
- `IIntegrationEvent` - marker interface
- `IntegrationEventBase` - base class with `EventId` and `OccurredOn`

**BuildingBlocks.Infrastructure.Wolverine** provides:
- `WolverineEventBus` - implements `IEventBus` abstraction
- `AddWolverineEventBus()` - extension method for host registration
- RabbitMQ transport configuration with AutoProvision

**Shared/IntegrationEvents** provides:
- Integration event definitions (e.g., `PatientCreatedIntegrationEvent`)
- These are the public contracts between bounded contexts

**[BC].Infrastructure/Consumers/Wolverine** provides:
- Wolverine handlers (e.g., `PatientCreatedHandler`)
- These get discovered via `Discovery.IncludeAssembly(assembly)` and `CustomizeHandlerDiscovery()` filter

**[BC].WebApi/Host** does:
- Calls `AddWolverineEventBus()` once
- Passes in assemblies containing handlers

### Project Structure Diagram

```
src/
+-- BuildingBlocks/
|   +-- BuildingBlocks.Application/
|   |   +-- Messaging/                        # Messaging abstractions
|   |   |   +-- IEventBus.cs                  # Publisher interface
|   |   |   +-- IIntegrationEvent.cs          # Marker interface
|   |   |   +-- IntegrationEventBase.cs       # Base class with EventId, OccurredOn
|   |   |
|   |   +-- Interfaces/
|   |       +-- IUnitOfWork.cs                # Includes QueueIntegrationEvent()
|   |
|   +-- BuildingBlocks.Infrastructure.Wolverine/
|       +-- WolverineEventBus.cs              # IEventBus implementation
|       +-- WolverineExtensions.cs            # AddWolverineEventBus() extension
|
+-- Shared/
|   +-- IntegrationEvents/                    # Integration event definitions
|       +-- Scheduling/
|           +-- PatientCreatedIntegrationEvent.cs
|
+-- Core/Scheduling/
|   +-- Scheduling.Application/               # Uses IUnitOfWork.QueueIntegrationEvent()
|   |
|   +-- Scheduling.Infrastructure/
|       +-- Consumers/
|           +-- Wolverine/                    # Wolverine handlers
|               +-- PatientCreatedHandler.cs
|
+-- WebApplications/Scheduling.WebApi/
    +-- Program.cs                            # Wolverine registered here
```

### Clean Architecture Layer Dependencies

| Project | BuildingBlocks.Application | BuildingBlocks.Infrastructure.Wolverine | Shared/IntegrationEvents |
|---------|---------------------------|----------------------------------------|--------------------------|
| **{BC}.Domain** | No | No | No |
| **{BC}.Application** | Yes (IUnitOfWork, IEventBus) | No | Yes (event DTOs) |
| **{BC}.Infrastructure** | Yes | Optional (only if using Wolverine-specific types) | Yes (handlers) |
| **{BC}.WebApi** | Yes | Yes | No |

> **Note**: Unlike MassTransit, `{BC}.Infrastructure` does NOT need to reference `BuildingBlocks.Infrastructure.Wolverine` — Wolverine handlers are plain classes with no base class or interface dependency. The reference is only needed if you import Wolverine-specific types (like `Envelope`).

### Provider Swappability

The `IEventBus` abstraction allows swapping messaging providers without changing business logic. All application code depends on `IEventBus`, not on Wolverine directly.

**Current setup** (Wolverine + RabbitMQ):
```csharp
// In WebApplications/Scheduling.WebApi/Program.cs
builder.AddWolverineEventBus(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});
```

**If switching to MassTransit**:
1. Create `BuildingBlocks.Infrastructure.MassTransit` project
2. Implement `IEventBus` using MassTransit
3. Change host registration:
```csharp
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});
```

**No changes required in**:
- Application layer (still uses `IEventBus` abstraction)
- Business logic (command handlers)
- Integration event definitions

This is the power of Dependency Inversion: depend on abstractions, not concretions.

---

## 4. Implementation Steps

### Step 1: Create Projects

```bash
# BuildingBlocks.Application already exists with CQRS abstractions
# Add Messaging folder to it manually

# Create BuildingBlocks.Infrastructure.Wolverine project
dotnet new classlib -n BuildingBlocks.Infrastructure.Wolverine -o BuildingBlocks/BuildingBlocks.Infrastructure.Wolverine
dotnet sln add BuildingBlocks/BuildingBlocks.Infrastructure.Wolverine

# Create Shared/IntegrationEvents project
dotnet new classlib -n IntegrationEvents -o Shared/IntegrationEvents
dotnet sln add Shared/IntegrationEvents
```

### Step 2: Add NuGet Packages

**Note:** This project uses Central Package Management - see [Phase 1 documentation](../phase-1-ddd-fundamentals/03-building-patient-aggregate.md#understanding-central-package-management-cpm) for details on how CPM works.

```bash
# Add packages to BuildingBlocks.Infrastructure.Wolverine
cd BuildingBlocks/BuildingBlocks.Infrastructure.Wolverine
dotnet add package WolverineFx
dotnet add package WolverineFx.RabbitMQ
dotnet add reference ../BuildingBlocks.Application
```

Verify `Directory.Packages.props` contains:
```xml
<PackageVersion Include="WolverineFx" Version="4.12.2" />
<PackageVersion Include="WolverineFx.RabbitMQ" Version="4.12.2" />
```

### Step 3: Set Up Project References

```bash
# IntegrationEvents references BuildingBlocks.Application (for IntegrationEventBase)
cd Shared/IntegrationEvents
dotnet add reference ../../BuildingBlocks/BuildingBlocks.Application

# Scheduling.Application references BuildingBlocks.Application (for IEventBus)
cd ../../Core/Scheduling/Scheduling.Application
dotnet add reference ../../../BuildingBlocks/BuildingBlocks.Application

# Scheduling.Infrastructure references BuildingBlocks.Infrastructure.Wolverine and IntegrationEvents
cd ../Scheduling.Infrastructure
dotnet add reference ../../../BuildingBlocks/BuildingBlocks.Infrastructure.Wolverine
dotnet add reference ../../../Shared/IntegrationEvents

# Scheduling.WebApi references BuildingBlocks.Infrastructure.Wolverine (composition root)
cd ../../../WebApplications/Scheduling.WebApi
dotnet add reference ../../BuildingBlocks/BuildingBlocks.Infrastructure.Wolverine
```

### Step 4: Create Messaging Abstractions

Create the messaging abstractions in `BuildingBlocks.Application/Messaging/`:

**IEventBus.cs**:
```csharp
namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Abstraction for publishing integration events to a message broker.
/// Integration events are used for cross-bounded-context communication.
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class, IIntegrationEvent;
}
```

**IIntegrationEvent.cs**:
```csharp
namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Marker interface for integration events.
/// Integration events are published to RabbitMQ via Wolverine.
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>
    /// Unique identifier for this event instance.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// When this event occurred.
    /// </summary>
    DateTime OccurredOn { get; }
}
```

**IntegrationEventBase.cs**:
```csharp
namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Base class for integration events providing common properties.
/// </summary>
public abstract record IntegrationEventBase : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
```

**Update IUnitOfWork.cs** to include event queuing:
```csharp
namespace BuildingBlocks.Application.Interfaces;

public interface IUnitOfWork
{
    IRepository<T> RepositoryFor<T>() where T : class, IEntityBase;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues an integration event to be published after a successful save.
    /// Integration events are published to the message broker for cross-bounded-context communication.
    /// </summary>
    void QueueIntegrationEvent(IIntegrationEvent integrationEvent);

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CloseTransactionAsync(Exception? exception = null, CancellationToken cancellationToken = default);
}
```

### Step 5: Create Wolverine Implementation

Create `BuildingBlocks.Infrastructure.Wolverine/WolverineEventBus.cs`:

```csharp
using BuildingBlocks.Application.Messaging;
using Wolverine;

namespace BuildingBlocks.Infrastructure.Wolverine;

/// <summary>
/// Wolverine implementation of IEventBus.
/// Publishes integration events to RabbitMQ via Wolverine.
/// </summary>
internal sealed class WolverineEventBus : IEventBus
{
    private readonly IMessageBus _messageBus;

    public WolverineEventBus(IMessageBus messageBus)
    {
        _messageBus = messageBus;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent
    {
        await _messageBus.PublishAsync(@event);
    }
}
```

**Key points:**
- Wolverine uses `IMessageBus` for publishing
- `PublishAsync()` sends the event to RabbitMQ
- Wolverine handles routing conventions automatically

### Step 6: Create Example Integration Event

Create `Shared/IntegrationEvents/Scheduling/PatientCreatedIntegrationEvent.cs`:

```csharp
using BuildingBlocks.Application.Messaging;

namespace IntegrationEvents.Scheduling;

/// <summary>
/// Integration event published when a new patient is created.
/// This is the public contract for cross-bounded-context communication.
/// Other bounded contexts (e.g., Billing, MedicalRecords) consume this event.
/// </summary>
public record PatientCreatedIntegrationEvent(
    Guid PatientId,
    string FirstName,
    string LastName,
    string Email,
    DateTime DateOfBirth
) : IntegrationEventBase;
```

### Step 7: Set Up RabbitMQ in Docker

See [02a-rabbitmq-docker-setup.md](./02a-rabbitmq-docker-setup.md) for the complete RabbitMQ Docker setup (docker-compose.yml, starting, verifying, Management UI access).

### Step 8: Configure Wolverine

Create `BuildingBlocks.Infrastructure.Wolverine/WolverineExtensions.cs`:

```csharp
using BuildingBlocks.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using Wolverine;
using Wolverine.RabbitMQ;

namespace BuildingBlocks.Infrastructure.Wolverine;

public static class WolverineExtensions
{
    /// <summary>
    /// Registers Wolverine with RabbitMQ transport for event-driven messaging.
    /// </summary>
    /// <param name="builder">Host application builder (requires IHostApplicationBuilder, not just IServiceCollection)</param>
    /// <param name="configureWolverine">Optional callback to configure Wolverine options</param>
    public static IHostApplicationBuilder AddWolverineEventBus(
        this IHostApplicationBuilder builder,
        Action<WolverineOptions>? configureWolverine = null)
    {
        // Register IEventBus implementation
        builder.Services.AddScoped<IEventBus, WolverineEventBus>();

        // Configure Wolverine with RabbitMQ transport
        builder.UseWolverine(opts =>
        {
            var connectionString = builder.Configuration.GetConnectionString("messaging")
                ?? "amqp://guest:guest@localhost:5672";

            opts.UseRabbitMq(new Uri(connectionString))
                .AutoProvision(); // Automatically create queues/exchanges

            // Only discover handlers where the first parameter implements IIntegrationEvent.
            // This prevents accidental handler registration for non-event types.
            opts.Discovery.CustomizeHandlerDiscovery(q =>
            {
                q.Excludes.WithCondition("Non-IIntegrationEvent handlers", t =>
                    !HasIntegrationEventHandlerMethod(t));
            });

            // Allow host to configure additional options (e.g., add more assemblies)
            configureWolverine?.Invoke(opts);
        });

        return builder;
    }

    private static bool HasIntegrationEventHandlerMethod(Type type)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Any(m =>
                m.Name is "Handle" or "HandleAsync" &&
                m.GetParameters().FirstOrDefault()?.ParameterType
                    .IsAssignableTo(typeof(IIntegrationEvent)) == true);
    }
}
```

**Why filter by `IIntegrationEvent`?**

Without this filter, Wolverine would register a handler for ANY public class with a `Handle` method in the scanned assembly — even if the first parameter is `string` or another non-event type. By excluding types that don't have a `Handle`/`HandleAsync` method with an `IIntegrationEvent` first parameter, only genuine integration event handlers are discovered.

This uses `CustomizeHandlerDiscovery` with an exclude condition via `CompositeFilter<Type>.WithCondition()` this gives you convention-based discovery with a type-safety guardrail.

**Why `IHostApplicationBuilder` instead of `IServiceCollection`?**

Wolverine uses `IHostApplicationBuilder.UseWolverine()` because it registers a hosted service (`IHostedService`) for background message processing. The `UseWolverine()` extension method provides access to both `IServiceCollection` and `IConfiguration` for comprehensive setup.

Add connection string to `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "messaging": "amqp://guest:guest@localhost:5672"
  }
}
```

The `AutoProvision()` call in `WolverineExtensions.cs` tells Wolverine to automatically create RabbitMQ queues and exchanges based on handler conventions.

Register in `WebApplications/Scheduling.WebApi/Program.cs`:

```csharp
using BuildingBlocks.Infrastructure.Wolverine;

var builder = WebApplication.CreateBuilder(args);

// Add Wolverine for event-driven messaging
builder.AddWolverineEventBus(opts =>
{
    // Handler discovery is filtered by IIntegrationEvent in WolverineExtensions
    // Just specify which assemblies to scan
    opts.Discovery.IncludeAssembly(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});

// ... rest of app configuration ...
```

**Why register in the host?** The WebApi project is the composition root where all infrastructure gets wired up (DbContext, MediatR, messaging, etc.).

**Optional: Framework selection via configuration**

To support switching messaging frameworks at runtime, use a configuration setting:

```csharp
var messagingFramework = builder.Configuration.GetValue<string>("MessagingFramework") ?? "Wolverine";

if (messagingFramework == "Wolverine")
{
    builder.AddWolverineEventBus(opts =>
    {
        opts.Discovery.IncludeAssembly(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
    });
}
else
{
    // Alternative framework (e.g., MassTransit)
    // See 02-rabbitmq-masstransit-setup.md
    builder.Services.AddMassTransitEventBus(builder.Configuration, cfg =>
    {
        cfg.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
    });
}
```

Add to `appsettings.json`:
```json
{
  "MessagingFramework": "Wolverine"
}
```

### Step 9: Create Test Endpoint

Add a minimal endpoint to test message publishing in `Program.cs`:

```csharp
using BuildingBlocks.Application.Messaging;
using IntegrationEvents.Scheduling;

app.MapPost("/test-publish", async (IEventBus eventBus) =>
{
    await eventBus.PublishAsync(new PatientCreatedIntegrationEvent(
        PatientId: Guid.NewGuid(),
        FirstName: "Test",
        LastName: "Patient",
        Email: "test@example.com",
        DateOfBirth: new DateTime(1985, 6, 15)));

    return Results.Ok("Message published");
});
```

Notice we inject `IEventBus` (abstraction) instead of `IMessageBus` (Wolverine-specific). This keeps the endpoint decoupled from the messaging provider.

### Step 10: Create a Handler

Create `Scheduling.Infrastructure/Consumers/Wolverine/PatientCreatedHandler.cs`:

```csharp
using IntegrationEvents.Scheduling;
using Microsoft.Extensions.Logging;

namespace Scheduling.Infrastructure.Consumers.Wolverine;

/// <summary>
/// Wolverine handler for PatientCreatedIntegrationEvent.
/// Discovered automatically via convention (no explicit registration needed).
/// </summary>
public class PatientCreatedHandler
{
    public Task Handle(
        PatientCreatedIntegrationEvent message,
        ILogger<PatientCreatedHandler> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Received PatientCreatedIntegrationEvent for {PatientId} - {FirstName} {LastName}",
            message.PatientId,
            message.FirstName,
            message.LastName);

        // TODO: Add cross-bounded-context logic here
        // Example: notify Billing or MedicalRecords bounded contexts

        return Task.CompletedTask;
    }
}
```

**Key Wolverine characteristics:**
- **No interface required**: Plain class with a `Handle` method
- **Direct message injection**: Message is injected directly as the first parameter
- **Method injection**: Dependencies can be injected into the `Handle` method parameters (in addition to constructor injection)
- **Automatic discovery**: No explicit registration needed - handlers are discovered by scanning assemblies passed to `opts.Discovery.IncludeAssembly()`
- **Handler naming**: By convention, handler classes should end with `Handler` (e.g., `PatientCreatedHandler` for `PatientCreatedIntegrationEvent`)

**Handler method signature explained:**
- **First parameter** (`PatientCreatedIntegrationEvent message`): The message type this handler subscribes to
- **Remaining parameters** (`ILogger<T>`, `CancellationToken`, etc.): Resolved from DI via method injection
- **Return type**: `Task` or `Task<T>` for async handlers, `void` for synchronous handlers

### Step 11: Verify the Setup

1. **Start RabbitMQ** (if not already running):
   ```bash
   docker-compose up -d
   ```

2. **Run your application** and POST to `/test-publish`

3. **Check RabbitMQ Management UI** at http://localhost:15672:
   - **Queues tab**: Should show a queue named `patient-created-integration-event` (kebab-case from message type)
   - **Exchanges tab**: Should show the exchange
   - **Messages**: Can be viewed in queue details

4. **Check application logs**: Should show the handler processing the message

---

## 5. Handler Conventions

Wolverine discovers handlers by convention — no base class, no interface required. Just a public class with a public `Handle` method.

### What Wolverine Does Under the Hood

1. Scans assemblies registered via `opts.Discovery.IncludeAssembly()` or filtered via `opts.Discovery.CustomizeHandlerDiscovery()`
2. Looks for public classes with public `Handle`/`HandleAsync` methods
3. Inspects the first parameter of `Handle()` to determine the message type
4. Remaining parameters are resolved from DI (method injection)
5. Queue names are derived from message type (kebab-case)
6. Automatically wires up the handler to consume from that queue

### Recommended: Filter by IIntegrationEvent

```csharp
opts.Discovery.CustomizeHandlerDiscovery(q =>
{
    q.Excludes.WithCondition("Non-IIntegrationEvent handlers", t =>
        !t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Any(m =>
                m.Name is "Handle" or "HandleAsync" &&
                m.GetParameters().FirstOrDefault()?.ParameterType
                    .IsAssignableTo(typeof(IIntegrationEvent)) == true));
});
```

This prevents accidental handler registration for non-event types (e.g., a class with `Handle(string message)` would NOT be registered).

### Handler Discovery Table

| Convention | Wolverine |
|---|---|
| **Handler discovery** | Automatic scan via `Discovery.IncludeAssembly()` |
| **Handler method signature** | `Task Handle(T message, ...)` |
| **Handler base class/interface** | None — plain class |
| **Dependency injection style** | Method injection + constructor injection |
| **Queue naming** | `kebab-case` from message type |
| **Handler naming** | Class name should end with `Handler` (e.g., `PatientCreatedHandler`) |

### Method Injection Examples

**Constructor injection (standard)**:
```csharp
public class PatientCreatedHandler
{
    private readonly ILogger<PatientCreatedHandler> _logger;

    public PatientCreatedHandler(ILogger<PatientCreatedHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(PatientCreatedIntegrationEvent message, CancellationToken ct)
    {
        _logger.LogInformation("Processing {PatientId}", message.PatientId);
        return Task.CompletedTask;
    }
}
```

**Method injection (Wolverine-specific)**:
```csharp
public class PatientCreatedHandler
{
    public Task Handle(
        PatientCreatedIntegrationEvent message,
        ILogger<PatientCreatedHandler> logger,  // Injected from DI
        IMediator mediator,                      // Injected from DI
        CancellationToken cancellationToken)     // Injected from DI
    {
        logger.LogInformation("Processing {PatientId}", message.PatientId);
        return mediator.Send(new SomeCommand(message.PatientId), cancellationToken);
    }
}
```

**Accessing message metadata (Envelope)**:
```csharp
public class PatientCreatedHandler
{
    public Task Handle(
        PatientCreatedIntegrationEvent message,
        Envelope envelope,  // Wolverine's message metadata wrapper
        ILogger<PatientCreatedHandler> logger)
    {
        logger.LogInformation(
            "Received message {MessageId} at {ReceivedAt}",
            envelope.Id,
            envelope.ReceivedAt);

        return Task.CompletedTask;
    }
}
```

---

## 6. Error Handling and Retry

Wolverine has built-in retry and error handling policies.

### Global Retry Policy

Configure retry policies in `WolverineExtensions.cs`:

```csharp
builder.UseWolverine(opts =>
{
    opts.UseRabbitMq(new Uri(connectionString!))
        .AutoProvision();

    // Global retry policy for transient failures
    opts.OnException<SqlException>()
        .RetryTimes(3)
        .RetryWithCooldown(1.Seconds(), 5.Seconds(), 15.Seconds());

    // Don't retry validation failures
    opts.OnException<ValidationException>()
        .MoveToErrorQueue();

    opts.OnException<ArgumentException>()
        .MoveToErrorQueue();

    // Discovery
    opts.Discovery.CustomizeHandlerDiscovery(q =>
    {
        q.Excludes.WithCondition("Non-IIntegrationEvent handlers", t =>
            !t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Any(m =>
                    m.Name is "Handle" or "HandleAsync" &&
                    m.GetParameters().FirstOrDefault()?.ParameterType
                        .IsAssignableTo(typeof(IIntegrationEvent)) == true));
    });
});
```

### Per-Handler Retry Policy

You can also configure retry policies per handler using attributes:

```csharp
using Wolverine;

[RetryWithCooldown(typeof(SqlException), 1, 5, 15)]
public class PatientCreatedHandler
{
    public Task Handle(PatientCreatedIntegrationEvent message)
    {
        // If SqlException is thrown, retry with cooldowns: 1s, 5s, 15s
        return Task.CompletedTask;
    }
}
```

### Dead Letter Queue

Wolverine automatically moves failed messages to an error queue after retry exhaustion:

```csharp
opts.OnException<Exception>()
    .RetryTimes(3)
    .ThenMoveToErrorQueue(); // Default behavior
```

---

## 7. Outbox Support

Wolverine supports the transactional outbox pattern for guaranteed message delivery. We cover this in detail in [08-transactional-outbox.md](./08-transactional-outbox.md).

---

## 8. MassTransit Interoperability

When migrating bounded contexts independently between messaging frameworks, you may need Wolverine to consume messages published by MassTransit (or vice versa). Wolverine has built-in support for this via `.UseMassTransitInterop()`.

### The Problem

MassTransit and Wolverine use different conventions:

| Aspect | MassTransit | Wolverine |
|--------|-------------|-----------|
| **Exchange naming** | `Namespace:MessageType` (e.g., `IntegrationEvents.Scheduling:PatientCreatedIntegrationEvent`) | CLR type full name |
| **Message envelope** | Wraps payload in `{ "messageType": [...], "message": { ... }, "headers": { ... } }` | Own envelope format |
| **Queue naming** | Based on consumer type (kebab-case) | Based on handler endpoint |

This means a message published by MassTransit won't be consumed by Wolverine out of the box — they use different exchanges, queues, and serialization formats.

### The Solution: `UseMassTransitInterop()`

Wolverine can consume MassTransit messages by:
1. **Binding** a Wolverine queue to MassTransit's exchange (solves routing)
2. **Using MassTransit interop mode** on the listener (solves envelope deserialization)

```csharp
// In the consuming service's Program.cs (Wolverine consumer)
builder.AddWolverineEventBus(connectionString, opts =>
{
    opts.Discovery.IncludeAssembly(typeof(MyBoundedContext.Infrastructure.ServiceCollectionExtensions).Assembly);

    // One-liner: derives MassTransit exchange name from generic type, binds queue, and enables interop
    opts.ListenToMassTransitQueue<PatientCreatedIntegrationEvent>("consumer-patient-created");
});
```

> **Stale queue cleanup:** If you previously ran Wolverine with `IncludeAssembly` but **without** `ListenToMassTransitQueue`, Wolverine's `AutoProvision()` may have auto-created a queue named `PatientCreatedIntegrationEventHandler` bound to the MassTransit exchange. This queue lacks MassTransit interop, so messages pile up without being consumed. After adding the explicit listener above, delete the stale queue from RabbitMQ management:
>
> - `PatientCreatedIntegrationEventHandler`
> - `PatientCreatedIntegrationEventHandler_error`
>
> With `ListenToMassTransitQueue` configured, Wolverine routes the handler to `consumer-patient-created` instead and will not recreate the stale queue.

### What `ListenToMassTransitQueue<T>` Does Under the Hood

> **Important:** `.DefaultIncomingMessage<TMessage>()` is required because Wolverine cannot extract the message type from MassTransit's envelope metadata. MassTransit stores the message type inside the JSON body (`messageType` array), but Wolverine's handler pipeline checks for the message type *before* deserializing the body. Without this call, you'll get `ArgumentOutOfRangeException: "The envelope has no Message or MessageType name"`.

The helper in `WolverineExtensions.cs` derives the MassTransit exchange name from the CLR type using the convention `Namespace:TypeName`, then performs the bind and listen:

```csharp
public static WolverineOptions ListenToMassTransitQueue<TMessage>(
    this WolverineOptions opts,
    string queueName)
{
    var exchangeName = $"{typeof(TMessage).Namespace}:{typeof(TMessage).Name}";

    opts.UseRabbitMq()
        .BindExchange(exchangeName)
        .ToQueue(queueName);

    opts.ListenToRabbitQueue(queueName)
        .DefaultIncomingMessage<TMessage>()
        .UseMassTransitInterop();

    return opts;
}
```

This is equivalent to the manual approach:

```csharp
// Manual approach (what the helper does internally)
opts.UseRabbitMq()
    .BindExchange("IntegrationEvents.Scheduling:PatientCreatedIntegrationEvent")
    .ToQueue("consumer-patient-created");

opts.ListenToRabbitQueue("consumer-patient-created")
    .DefaultIncomingMessage<PatientCreatedIntegrationEvent>()
    .UseMassTransitInterop();
```

### How It Works

1. MassTransit publishes `PatientCreatedIntegrationEvent` to a fanout exchange named `IntegrationEvents.Scheduling:PatientCreatedIntegrationEvent`
2. RabbitMQ routes the message to the `consumer-patient-created` queue (via the exchange binding)
3. Wolverine picks up the message and uses `UseMassTransitInterop()` to:
   - Parse the outer MassTransit envelope
   - Extract the `message` property containing the actual payload
   - Map the `messageType` URN to the correct .NET type
   - Carry over correlation metadata (messageId, correlationId, etc.)
4. Wolverine's handler receives the deserialized `PatientCreatedIntegrationEvent` as normal

### When to Use This

This pattern is valuable for **incremental migration**:
- Migrate one bounded context at a time from MassTransit to Wolverine (or vice versa)
- No need to switch all services simultaneously
- The interop layer handles the envelope translation transparently

### Reverse Direction: Wolverine → MassTransit

The interop works both ways. If a Wolverine service needs to publish messages that a MassTransit consumer can understand, apply `.UseMassTransitInterop()` to the **publishing** side:

```csharp
// Wolverine publisher wrapping messages in MassTransit envelope format
builder.AddWolverineEventBus(connectionString, opts =>
{
    opts.Discovery.IncludeAssembly(typeof(MyBoundedContext.Infrastructure.ServiceCollectionExtensions).Assembly);

    // One-liner: derives MassTransit exchange name from generic type and enables interop
    opts.PublishToMassTransitExchange<PatientCreatedIntegrationEvent>();
});
```

This is equivalent to the manual approach:

```csharp
// Manual approach (what the helper does internally)
opts.PublishMessage<PatientCreatedIntegrationEvent>()
    .ToRabbitExchange("IntegrationEvents.Scheduling:PatientCreatedIntegrationEvent")
    .UseMassTransitInterop();
```

This wraps outgoing messages in MassTransit's envelope format (`messageType`, `message`, `headers`, etc.) so MassTransit consumers deserialize them natively — no changes needed on the MassTransit side.

> **Note:** In this example, the consuming service uses a generic `MyBoundedContext` placeholder. In a real scenario, this would be the actual bounded context name (e.g., in Phase 6, this becomes the Billing bounded context consuming events from Scheduling).

### Full Migration Matrix

| Direction | Wolverine Configuration | MassTransit Side |
|-----------|------------------------|------------------|
| **MassTransit → Wolverine** | `opts.ListenToMassTransitQueue<T>("queue-name")` | No changes needed |
| **Wolverine → MassTransit** | `opts.PublishToMassTransitExchange<T>()` | No changes needed |
| **Wolverine ↔ Wolverine** | Default (no interop) | N/A |
| **MassTransit ↔ MassTransit** | N/A | Default (no interop) |

This enables true **incremental migration** — migrate one bounded context at a time in either direction, with the interop layer handling envelope translation transparently.

### Important Caveat

> **Reserve interop endpoints for cross-framework traffic.** Don't mix MassTransit interop and Wolverine-to-Wolverine communication on the same queue. Use separate queues for each.

### MassTransit Exchange Naming Convention

MassTransit creates fanout exchanges using the pattern `Namespace:TypeName`:

```
CLR Type: IntegrationEvents.Scheduling.PatientCreatedIntegrationEvent
Exchange: IntegrationEvents.Scheduling:PatientCreatedIntegrationEvent
```

To find the exchange name for any integration event, check the RabbitMQ Management UI (Exchanges tab) after MassTransit has published at least one message.

---

## 9. Testing

Wolverine provides a built-in test harness for integration testing.

### Test Harness Example

```csharp
using Wolverine.Tracking;
using Microsoft.Extensions.Hosting;

[TestClass]
public class WolverineIntegrationTests
{
    [TestMethod]
    public async Task Should_Handle_PatientCreatedEvent()
    {
        // Arrange
        var host = await Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                opts.Discovery.IncludeAssembly(typeof(PatientCreatedHandler).Assembly);
            })
            .StartAsync();

        var @event = new PatientCreatedIntegrationEvent(
            PatientId: Guid.NewGuid(),
            FirstName: "Test",
            LastName: "Patient",
            Email: "test@test.com",
            DateOfBirth: new DateTime(1985, 6, 15));

        // Act - Track message execution
        var tracked = await host.InvokeMessageAndWaitAsync(@event);

        // Assert - Handler was invoked
        tracked.Executed.SingleMessage<PatientCreatedIntegrationEvent>().ShouldNotBeNull();
    }
}
```

### In-Memory Transport for Unit Tests

For fast unit tests, use the in-memory transport:

```csharp
builder.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(PatientCreatedHandler).Assembly);
    // No RabbitMQ configuration - defaults to in-memory transport
});
```

---

## 10. Optional: Auto-Start Docker on F5 in Visual Studio

See [02a-rabbitmq-docker-setup.md](./02a-rabbitmq-docker-setup.md#3-auto-start-docker-on-f5-in-visual-studio-optional) for the PowerShell script and MSBuild target setup.

---

## 11. Reference

### Docker Commands Cheat Sheet

See [02a-rabbitmq-docker-setup.md](./02a-rabbitmq-docker-setup.md#4-docker-commands-cheat-sheet).

### Queue Naming Convention

Wolverine uses kebab-case naming derived from message type:

```
Event: PatientCreatedIntegrationEvent
  +-- Queue: patient-created-integration-event
```

### Queue Topology: Per-Type vs Per-Service

Wolverine defaults to **per-message-type queues** — each event type gets its own queue (e.g., `patient-created-integration-event` for `PatientCreatedIntegrationEvent`). An alternative pattern is **per-service queues**, where a single queue per bounded context (e.g., `billing`) receives all event types, with internal dispatch to the right handler.

| | Per-message-type (our approach) | Per-service |
|---|---|---|
| **Queue count** | One per event type per consumer | One per service |
| **Monitoring** | Granular per event type | "How's billing doing?" at a glance |
| **Scaling** | Scale consumers per event type independently | Single scaling unit per service |
| **Retry/prefetch** | Configure per event type | Shared across all event types |

We use per-type queues in this project because it's the default and provides granular control over scaling and retry policies per event type.

Wolverine supports per-service queues via `UseConventionalRouting`:

```csharp
opts.UseRabbitMq()
    .UseConventionalRouting(x =>
    {
        // Route all message types to a single queue per service
        x.QueueNameForListener(type => "billing");
    });
```

This routes all incoming messages to the `billing` queue, regardless of message type. Wolverine dispatches to the correct handler based on the message type in its envelope.

### Handler Discovery Filter

```csharp
opts.Discovery.CustomizeHandlerDiscovery(q =>
{
    q.Excludes.WithCondition("Non-IIntegrationEvent handlers", t =>
        !t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Any(m =>
                m.Name is "Handle" or "HandleAsync" &&
                m.GetParameters().FirstOrDefault()?.ParameterType
                    .IsAssignableTo(typeof(IIntegrationEvent)) == true));
});
```

### Verification Checklist

- [ ] BuildingBlocks.Application/Messaging/ created with IEventBus, IntegrationEventBase
- [ ] BuildingBlocks.Infrastructure.Wolverine/ created with WolverineEventBus
- [ ] Shared/IntegrationEvents project created
- [ ] Project references configured correctly (abstractions in Application, implementation in Infrastructure)
- [ ] Wolverine NuGet packages installed in BuildingBlocks.Infrastructure.Wolverine
- [ ] Wolverine configured with RabbitMQ transport and AutoProvision
- [ ] IEventBus registered in DI container
- [ ] Connection string configured in appsettings.json
- [ ] Test publish endpoint working (uses IEventBus, not IMessageBus)
- [ ] Messages visible in RabbitMQ Management UI
- [ ] Handler discovered and processing messages
- [ ] Handler discovery filtered by IIntegrationEvent

---

## Summary

You've set up Wolverine for event-driven messaging following Clean Architecture:

1. **BuildingBlocks.Application/Messaging/** - Technology-agnostic abstractions (`IEventBus`, `IIntegrationEvent`, `IntegrationEventBase`)
2. **BuildingBlocks.Application/Interfaces/** - `IUnitOfWork` with `QueueIntegrationEvent()` for transactional event publishing
3. **BuildingBlocks.Infrastructure.Wolverine/** - Wolverine implementation (`WolverineEventBus`, `AddWolverineEventBus()`)
4. **Shared/IntegrationEvents** - Integration event definitions (public contracts between bounded contexts)
5. **[BC].Infrastructure/Consumers/Wolverine** - Wolverine handlers (plain classes with `Handle()` method)
6. **[BC].WebApi/Program.cs** - Wolverine registration (composition root)

### Key Architectural Decisions

**Event publishing:**
- All events go through RabbitMQ via Wolverine
- All events get durability, retry, and dead-letter queues
- MediatR is used for CQRS (commands/queries)

**Publishing flow:**
1. Command handler creates entity
2. Command handler queues integration event: `_uow.QueueIntegrationEvent(new PatientCreatedIntegrationEvent(...))`
3. Command handler saves: `await _uow.SaveChangesAsync()`
4. After successful database commit, queued events are published to RabbitMQ

**Framework registration:**
- Registered in the WebApi host (`Scheduling.WebApi/Program.cs`) — the composition root where all infrastructure gets wired up

**Wolverine advantages:**
- **MIT license**: Free forever for commercial and personal use
- **Convention-based**: Automatic handler discovery with type-safety filter
- **Method injection**: Inject dependencies into handler methods
- **No base classes**: Plain classes with `Handle()` methods
- **Auto-provisioning**: Automatically creates queues, exchanges, outbox tables

This architecture ensures:
- **Durability**: All events persisted to message broker
- **Dependency Inversion**: Application layer depends on `IEventBus`, not on Wolverine
- **Transactional Publishing**: Events only published after successful database commit
- **Provider Swappability**: Swap messaging frameworks by changing the registration in `Program.cs`

**See Also:**
- [02-rabbitmq-masstransit-setup.md](./02-rabbitmq-masstransit-setup.md) - MassTransit alternative implementation
- [04-integration-events.md](./04-integration-events.md) - Defining and publishing integration events

---

**Next:** [04-integration-events.md](./04-integration-events.md) - Defining and publishing integration events
