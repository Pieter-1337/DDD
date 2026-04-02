# RabbitMQ & MassTransit Setup

This document provides a complete guide for setting up MassTransit as a messaging framework for event-driven communication between bounded contexts. MassTransit is a mature .NET service bus abstraction that provides enterprise messaging patterns out of the box.

---

## 1. Overview: Why MassTransit and RabbitMQ?

### Why MassTransit?

MassTransit is a .NET abstraction layer over message brokers that eliminates low-level boilerplate and provides enterprise messaging patterns out of the box.

**Without MassTransit** (raw RabbitMQ):
```csharp
var factory = new ConnectionFactory { HostName = "localhost" };
using var connection = factory.CreateConnection();
using var channel = connection.CreateModel();

channel.QueueDeclare("patient-created", durable: true, exclusive: false, autoDelete: false);
var body = JsonSerializer.SerializeToUtf8Bytes(message);
channel.BasicPublish(exchange: "", routingKey: "patient-created", body: body);
```

**With MassTransit**:
```csharp
await _publishEndpoint.Publish(new PatientCreatedIntegrationEvent { ... });
```

### MassTransit Features

| Feature | Description |
|---------|-------------|
| **Broker abstraction** | Switch between RabbitMQ, Azure Service Bus, Amazon SQS without code changes |
| **Message serialization** | JSON, XML, BSON out of the box |
| **Retry policies** | Configurable retry with exponential backoff |
| **Error handling** | Dead letter queues, fault handling |
| **Saga support** | State machine-based orchestration for distributed transactions |
| **Testing** | In-memory transport for integration tests |

### Why RabbitMQ?

RabbitMQ is a proven message broker that provides reliable message delivery between distributed services. We'll run it in Docker for easy setup and teardown during development.

### When to Use MassTransit

MassTransit is a good choice when:
- You need mature, battle-tested enterprise messaging patterns
- You want built-in saga/state machine support for distributed transactions
- You need advanced features like circuit breakers, rate limiting, and scheduling
- Your team is familiar with the `IConsumer<T>` pattern
- You prefer explicit handler registration over convention-based discovery

MassTransit provides:
- RabbitMQ, Azure Service Bus, Amazon SQS support
- Retry policies and dead letter queues
- Message serialization
- In-memory transport for testing
- Saga state machines

---

## 2. MassTransit Licensing

**MassTransit v9+ (2026 onwards) requires a commercial license** for production use:
- Small/Medium Business: $400/month or $4,000/year
- Large Organizations: $1,200/month or $12,000/year
- Local development/evaluation: Free (temporary license)

**MassTransit v8 remains open source (Apache 2.0)** and is maintained through end of 2026.

**For this learning project**: Use MassTransit v8.x (open source). The concepts you learn transfer to any messaging framework.

**Open source alternatives**:
- [Wolverine](https://wolverine.netlify.app/) - Modern .NET messaging (MIT) — see [03-rabbitmq-wolverine-setup.md](./03-rabbitmq-wolverine-setup.md)
- [Rebus](https://github.com/rebus-org/Rebus) - Mature service bus (MIT)
- [Brighter](https://github.com/BrighterCommand/Brighter) - CQRS/messaging (MIT)

> **Note**: For an alternative MIT-licensed messaging framework with convention-based handler discovery, see [03-rabbitmq-wolverine-setup.md](./03-rabbitmq-wolverine-setup.md).

---

## 3. Architecture: Project Structure for Messaging

This setup separates messaging concerns across multiple projects, following Clean Architecture and the Dependency Inversion Principle.

### Core Principle

All events are:
- Published to RabbitMQ via MassTransit
- Defined in `Shared/IntegrationEvents/`
- Queued in command handlers via `_uow.QueueIntegrationEvent()`
- Published after `SaveChangesAsync()` succeeds

| Project | Purpose | Contains |
|---------|---------|----------|
| **BuildingBlocks.Application/Messaging** | Application-layer abstractions | `IEventBus`, `IIntegrationEvent`, `IntegrationEventBase` |
| **BuildingBlocks.Application/Interfaces** | Unit of work with event queuing | `IUnitOfWork.QueueIntegrationEvent()` |
| **BuildingBlocks.Infrastructure.MassTransit** | MassTransit provider | `MassTransitEventBus`, `AddMassTransitEventBus()` extension |
| **Shared/IntegrationEvents** | Integration event definitions | `PatientCreatedIntegrationEvent`, etc. |
| **[BC].Infrastructure/Consumers/MassTransit** | MassTransit consumers | Handles incoming integration events |
| **[BC].WebApi** | Composition root | MassTransit registration with consumer discovery |

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

### Where MassTransit Gets Registered

MassTransit is registered in `WebApplications/Scheduling.WebApi/Program.cs`

```csharp
// WebApplications/Scheduling.WebApi/Program.cs
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});
```

### What Lives Where

**BuildingBlocks.Application/Messaging** provides:
- `IEventBus` - abstraction for publishing integration events
- `IIntegrationEvent` - marker interface
- `IntegrationEventBase` - base class with `EventId` and `OccurredOn`

**BuildingBlocks.Infrastructure.MassTransit** provides:
- `MassTransitEventBus` - implements `IEventBus` abstraction
- `IntegrationEventHandler<T>` - base class for handlers with automatic logging
- `AddMassTransitEventBus()` - extension method for host registration
- RabbitMQ transport configuration and retry policies

**Shared/IntegrationEvents** provides:
- Integration event definitions (e.g., `PatientCreatedIntegrationEvent`)
- These are the public contracts between bounded contexts

**[BC].Infrastructure/Consumers/MassTransit** provides:
- MassTransit handlers (e.g., `PatientCreatedIntegrationEventHandler`)
- These get discovered via `AddConsumers(assembly)`

**Scheduling.WebApi/Host** does:
- Calls `AddMassTransitEventBus()` once
- Passes in assemblies containing consumers

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
|   +-- BuildingBlocks.Infrastructure.MassTransit/
|       +-- MassTransitEventBus.cs            # IEventBus implementation
|       +-- IntegrationEventHandler.cs        # Base class with automatic logging
|       +-- MassTransitExtensions.cs          # AddMassTransitEventBus() extension
|
+-- Shared/
|   +-- IntegrationEvents/                    # Integration event definitions
|       +-- Scheduling/
|       |   +-- PatientCreatedIntegrationEvent.cs
|       +-- Billing/
|       |   +-- PaymentProcessedIntegrationEvent.cs
|       +-- MedicalRecords/
|           +-- RecordUpdatedIntegrationEvent.cs
|
+-- Core/Scheduling/
|   +-- Scheduling.Application/               # Uses IUnitOfWork.QueueIntegrationEvent()
|   |
|   +-- Scheduling.Infrastructure/
|       +-- Consumers/
|           +-- MassTransit/                  # MassTransit handlers
|               +-- PatientCreatedIntegrationEventHandler.cs
|
+-- WebApplications/Scheduling.WebApi/
    +-- Program.cs                            # MassTransit registered here
```

### Clean Architecture Layer Dependencies

| Project | BuildingBlocks.Application | BuildingBlocks.Infrastructure.MassTransit | Shared/IntegrationEvents |
|---------|---------------------------|------------------------------------------|--------------------------|
| **{BC}.Domain** | No | No | No |
| **{BC}.Application** | Yes (IUnitOfWork, IEventBus) | No | Yes (event DTOs) |
| **{BC}.Infrastructure** | Yes | Yes | Yes (handlers) |
| **{BC}.WebApi** | Yes | Yes | No |

> **Note**: `{BC}.Infrastructure` references `BuildingBlocks.Infrastructure.MassTransit` for the `IntegrationEventHandler<T>` base class. MassTransit packages flow transitively.

### Provider Swappability

The `IEventBus` abstraction allows swapping messaging providers without changing business logic. All application code depends on `IEventBus`, not on MassTransit directly.

**Current setup** (MassTransit + RabbitMQ):
```csharp
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});
```

**If switching to Wolverine**:
1. Create `BuildingBlocks.Infrastructure.Wolverine` project
2. Implement `IEventBus` using Wolverine
3. Change host registration

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

# Create BuildingBlocks.Infrastructure.MassTransit project
dotnet new classlib -n BuildingBlocks.Infrastructure.MassTransit -o BuildingBlocks/BuildingBlocks.Infrastructure.MassTransit
dotnet sln add BuildingBlocks/BuildingBlocks.Infrastructure.MassTransit

# Create Shared/IntegrationEvents project
dotnet new classlib -n IntegrationEvents -o Shared/IntegrationEvents
dotnet sln add Shared/IntegrationEvents
```

### Step 2: Add NuGet Packages

**Note:** This project uses Central Package Management - see [Phase 1 documentation](../phase-1-ddd-fundamentals/03-building-patient-aggregate.md#understanding-central-package-management-cpm) for details on how CPM works.

```bash
# Add packages to BuildingBlocks.Infrastructure.MassTransit
cd BuildingBlocks/BuildingBlocks.Infrastructure.MassTransit
dotnet add package MassTransit
dotnet add package MassTransit.RabbitMQ
dotnet add reference ../BuildingBlocks.Application
```

Verify `Directory.Packages.props` contains:
```xml
<PackageVersion Include="MassTransit" Version="8.*" />
<PackageVersion Include="MassTransit.RabbitMQ" Version="8.*" />
```

### Step 3: Set Up Project References

```bash
# IntegrationEvents references BuildingBlocks.Application (for IntegrationEventBase)
cd Shared/IntegrationEvents
dotnet add reference ../../BuildingBlocks/BuildingBlocks.Application

# Scheduling.Application references BuildingBlocks.Application (for IEventBus)
cd ../../Core/Scheduling/Scheduling.Application
dotnet add reference ../../../BuildingBlocks/BuildingBlocks.Application

# Scheduling.Infrastructure references BuildingBlocks.Infrastructure.MassTransit and IntegrationEvents
# (MassTransit packages flow transitively - no direct package reference needed)
cd ../Scheduling.Infrastructure
dotnet add reference ../../../BuildingBlocks/BuildingBlocks.Infrastructure.MassTransit
dotnet add reference ../../../Shared/IntegrationEvents

# Scheduling.WebApi references BuildingBlocks.Infrastructure.MassTransit (composition root)
cd ../../../WebApplications/Scheduling.WebApi
dotnet add reference ../../BuildingBlocks/BuildingBlocks.Infrastructure.MassTransit
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
/// Integration events are published to RabbitMQ via MassTransit.
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

### Step 5: Create MassTransit Implementation

Create the MassTransit implementation of `IEventBus` in `BuildingBlocks.Infrastructure.MassTransit/MassTransitEventBus.cs`:

```csharp
using BuildingBlocks.Application.Messaging;
using MassTransit;

namespace BuildingBlocks.Infrastructure.MassTransit;

/// <summary>
/// MassTransit implementation of IEventBus.
/// Publishes integration events to RabbitMQ via MassTransit.
/// </summary>
internal sealed class MassTransitEventBus : IEventBus
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitEventBus(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent
    {
        await _publishEndpoint.Publish(@event, cancellationToken);
    }
}
```

### Step 6: Create Example Integration Event

Create an example integration event in `Shared/IntegrationEvents/Scheduling/PatientCreatedIntegrationEvent.cs`:

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

### Step 8: Configure MassTransit

Create `BuildingBlocks.Infrastructure.MassTransit/MassTransitExtensions.cs`:

```csharp
using BuildingBlocks.Application.Messaging;
using FluentValidation;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.MassTransit.Configuration;

public static class MassTransitExtensions
{
    public static IServiceCollection AddMassTransitEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IRegistrationConfigurator>? configureConsumers = null)
    {
        services.AddMassTransit(x =>
        {
            // Allow host to register consumers from specific assemblies
            configureConsumers?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                var rabbitMqSettings = configuration.GetSection("RabbitMQ");

                cfg.Host(
                    rabbitMqSettings["Host"] ?? "localhost",
                    rabbitMqSettings["VirtualHost"] ?? "/",
                    h =>
                    {
                        h.Username(rabbitMqSettings["Username"] ?? "guest");
                        h.Password(rabbitMqSettings["Password"] ?? "guest");
                    });

                // Configure retry policy
                cfg.UseMessageRetry(r =>
                {
                    r.Intervals(
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(15),
                        TimeSpan.FromSeconds(30)
                    );

                    // Don't retry validation failures
                    r.Ignore<ValidationException>();
                    r.Ignore<ArgumentException>();
                });

                // Configure endpoints for all registered consumers
                cfg.ConfigureEndpoints(context);
            });
        });

        // Register IEventBus implementation
        services.AddScoped<IEventBus, MassTransitEventBus>();

        return services;
    }
}
```

Add RabbitMQ settings to `appsettings.json`:

```json
{
  "RabbitMQ": {
    "Host": "localhost",
    "VirtualHost": "/",
    "Username": "guest",
    "Password": "guest"
  }
}
```

Register in `WebApplications/Scheduling.WebApi/Program.cs` (the composition root):

```csharp
using BuildingBlocks.Infrastructure.MassTransit.Configuration;

// Add MassTransit for event-driven messaging
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    // Register consumers from bounded context assemblies
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});
```

**Optional: Framework selection via configuration**

To support switching messaging frameworks at runtime, use a configuration setting:

```csharp
var messagingFramework = builder.Configuration.GetValue<string>("MessagingFramework") ?? "MassTransit";

if (messagingFramework == "MassTransit")
{
    builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
    {
        configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
    });
}
else
{
    // Alternative framework (e.g., Wolverine)
    // See 03-rabbitmq-wolverine-setup.md
    builder.AddWolverineEventBus(opts =>
    {
        opts.Discovery.IncludeAssembly(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
    });
}
```

Add to `appsettings.json`:
```json
{
  "MessagingFramework": "MassTransit"
}
```

### Step 9: Create Test Endpoint

Add a minimal endpoint to test message publishing:

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

Notice we inject `IEventBus` (abstraction) instead of `IPublishEndpoint` (MassTransit-specific). This keeps the endpoint decoupled from the messaging provider.

### Step 10: Create a Handler

Handlers inherit from `IntegrationEventHandler<TEvent>` which provides automatic logging (start, complete, error).

Create a handler in `Scheduling.Infrastructure/Consumers/MassTransit/PatientCreatedIntegrationEventHandler.cs`:

```csharp
using BuildingBlocks.Infrastructure.MassTransit;
using IntegrationEvents.Scheduling;
using Microsoft.Extensions.Logging;

namespace Scheduling.Infrastructure.Consumers.MassTransit;

/// <summary>
/// Handler for PatientCreatedIntegrationEvent.
/// Handles cross-bounded-context processing when a new patient is created.
/// </summary>
public class PatientCreatedIntegrationEventHandler
    : IntegrationEventHandler<PatientCreatedIntegrationEvent>
{
    public PatientCreatedIntegrationEventHandler(
        ILogger<PatientCreatedIntegrationEventHandler> logger) : base(logger)
    {
    }

    protected override Task HandleAsync(
        PatientCreatedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        // Business logic only - logging is automatic via base class
        Logger.LogInformation(
            "Processing patient {PatientId} - {FirstName} {LastName} ({Email})",
            message.PatientId,
            message.FirstName,
            message.LastName,
            message.Email);

        // TODO: Add cross-bounded-context logic here
        // Example: notify Billing or MedicalRecords bounded contexts

        return Task.CompletedTask;
    }
}
```

### Step 11: Verify the Setup

1. Start RabbitMQ (if not already running): `docker-compose up -d`
2. Run your application and POST to `/test-publish`
3. Check RabbitMQ Management UI at http://localhost:15672:
   - Queues tab shows created queues
   - Exchanges tab shows the exchange
   - Messages can be viewed in queue details

---

## 5. Handler Conventions

MassTransit uses the `IConsumer<T>` interface pattern for message handling. In this project, handlers inherit from `IntegrationEventHandler<TEvent>` — a base class that wraps `IConsumer<T>` with automatic logging.

### How MassTransit Discovers Handlers

1. Host calls `AddConsumers(assembly)` to register consumer classes from specific assemblies
2. MassTransit scans the assembly for classes implementing `IConsumer<T>`
3. Each consumer gets its own receive endpoint (queue)
4. Queue names are derived from the consumer class name (kebab-case)
5. MassTransit wires up deserialization, retry, and error handling automatically

### Handler Discovery Table

| Convention | MassTransit |
|---|---|
| **Handler discovery** | Explicit via `AddConsumers(assembly)` |
| **Handler interface** | `IConsumer<T>` (or `IntegrationEventHandler<T>` base class) |
| **Handler base class** | `IntegrationEventHandler<TEvent>` (project-specific, provides logging) |
| **Dependency injection style** | Constructor injection only |
| **Queue naming** | `kebab-case` from consumer class name |
| **Handler naming** | Class name should match event name with `Handler` suffix |

### IntegrationEventHandler<T> Base Class

The project provides `IntegrationEventHandler<TEvent>` in `BuildingBlocks.Infrastructure.MassTransit/` which wraps `IConsumer<T>` with automatic logging:

```csharp
// BuildingBlocks.Infrastructure.MassTransit/IntegrationEventHandler.cs
public abstract class IntegrationEventHandler<TEvent> : IConsumer<TEvent>
    where TEvent : class, IIntegrationEvent
{
    protected readonly ILogger Logger;

    protected IntegrationEventHandler(ILogger logger) => Logger = logger;

    public async Task Consume(ConsumeContext<TEvent> context)
    {
        Logger.LogInformation("Handling {EventType} with EventId {EventId}",
            typeof(TEvent).Name, context.Message.EventId);

        try
        {
            await HandleAsync(context.Message, context.CancellationToken);
            Logger.LogInformation("Handled {EventType} with EventId {EventId}",
                typeof(TEvent).Name, context.Message.EventId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling {EventType} with EventId {EventId}",
                typeof(TEvent).Name, context.Message.EventId);
            throw;
        }
    }

    protected abstract Task HandleAsync(TEvent message, CancellationToken cancellationToken);
}
```

Handlers only need to implement `HandleAsync()` — logging is automatic:

```csharp
public class PatientCreatedIntegrationEventHandler
    : IntegrationEventHandler<PatientCreatedIntegrationEvent>
{
    public PatientCreatedIntegrationEventHandler(
        ILogger<PatientCreatedIntegrationEventHandler> logger) : base(logger) { }

    protected override Task HandleAsync(
        PatientCreatedIntegrationEvent message, CancellationToken ct)
    {
        // Business logic only - logging is automatic
        return Task.CompletedTask;
    }
}
```

### Using Raw IConsumer<T> (Without Base Class)

For request-response handlers or when you need `ConsumeContext` access, use `IConsumer<T>` directly:

```csharp
public class GetPatientRequestHandler : IConsumer<GetPatientRequest>
{
    private readonly ILogger<GetPatientRequestHandler> _logger;

    public GetPatientRequestHandler(ILogger<GetPatientRequestHandler> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<GetPatientRequest> context)
    {
        // Access to ConsumeContext for RespondAsync, Headers, etc.
        await context.RespondAsync(new GetPatientResponse { ... });
    }
}
```

### ConsumerDefinition for Advanced Configuration

Use `ConsumerDefinition<T>` for fine-grained endpoint configuration:

```csharp
public class PatientCreatedIntegrationEventHandlerDefinition
    : ConsumerDefinition<PatientCreatedIntegrationEventHandler>
{
    public PatientCreatedIntegrationEventHandlerDefinition()
    {
        EndpointName = "billing-patient-created";
        ConcurrentMessageLimit = 10;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PatientCreatedIntegrationEventHandler> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(100, 500, 1000));
    }
}
```

---

## 6. Error Handling and Retry

MassTransit has built-in retry and error handling policies configured via the transport.

### Global Retry Policy

Retry policies are configured in `MassTransitExtensions.cs`:

```csharp
cfg.UseMessageRetry(r =>
{
    r.Intervals(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30)
    );

    // Don't retry validation failures
    r.Ignore<ValidationException>();
    r.Ignore<ArgumentException>();
});
```

### Per-Consumer Retry Policy

Use `ConsumerDefinition<T>` for consumer-specific retry:

```csharp
protected override void ConfigureConsumer(
    IReceiveEndpointConfigurator endpointConfigurator,
    IConsumerConfigurator<PatientCreatedIntegrationEventHandler> consumerConfigurator,
    IRegistrationContext context)
{
    endpointConfigurator.UseMessageRetry(r => r.Intervals(100, 500, 1000));

    endpointConfigurator.UseCircuitBreaker(cb =>
    {
        cb.TrackingPeriod = TimeSpan.FromMinutes(1);
        cb.TripThreshold = 15;
        cb.ActiveThreshold = 10;
        cb.ResetInterval = TimeSpan.FromMinutes(5);
    });
}
```

### Dead Letter Queue

MassTransit automatically moves failed messages (after retry exhaustion) to an error queue:

- Error queue name: `{queue-name}_error`
- Skipped messages go to: `{queue-name}_skipped`
- Messages in error queues can be inspected via RabbitMQ Management UI
- You can requeue messages for reprocessing

---

## 7. Outbox Support

MassTransit supports the transactional outbox pattern for guaranteed message delivery. We cover this in detail in [08-transactional-outbox.md](./08-transactional-outbox.md).

---

## 8. Testing

MassTransit provides a built-in test harness for integration testing.

### Test Harness Example

```csharp
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

[TestClass]
public class MassTransitIntegrationTests
{
    [TestMethod]
    public async Task Should_Consume_PatientCreated_Event()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<PatientCreatedIntegrationEventHandler>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var @event = new PatientCreatedIntegrationEvent(
            PatientId: Guid.NewGuid(),
            FirstName: "Test",
            LastName: "Patient",
            Email: "test@test.com",
            DateOfBirth: new DateTime(1985, 6, 15));

        // Act
        await harness.Bus.Publish(@event);

        // Assert
        Assert.IsTrue(await harness.Consumed.Any<PatientCreatedIntegrationEvent>());

        var consumerHarness = harness.GetConsumerHarness<PatientCreatedIntegrationEventHandler>();
        Assert.IsTrue(await consumerHarness.Consumed.Any<PatientCreatedIntegrationEvent>());
    }
}
```

### In-Memory Transport for Unit Tests

For fast unit tests without RabbitMQ, use the in-memory transport:

```csharp
services.AddMassTransitTestHarness(x =>
{
    x.AddConsumer<PatientCreatedIntegrationEventHandler>();
    // Uses in-memory transport automatically — no RabbitMQ needed
});
```

The test harness:
- Automatically uses in-memory transport
- Provides message tracking and assertions
- Supports consumer, saga, and request-response testing
- No RabbitMQ dependency for tests

---

## 9. Optional: Auto-Start Docker on F5 in Visual Studio

See [02a-rabbitmq-docker-setup.md](./02a-rabbitmq-docker-setup.md#3-auto-start-docker-on-f5-in-visual-studio-optional) for the PowerShell script and MSBuild target setup.

---

## 10. Reference

### Docker Commands Cheat Sheet

See [02a-rabbitmq-docker-setup.md](./02a-rabbitmq-docker-setup.md#4-docker-commands-cheat-sheet).

### MassTransit Conventions

MassTransit uses conventions for queue/exchange naming:

```
Event: PatientCreatedIntegrationEvent
  +-- Exchange: IntegrationEvents.Scheduling:PatientCreatedIntegrationEvent
      +-- Queue: patient-created-integration-event (for each consumer)
```

Customize endpoint names with kebab-case:

```csharp
x.UsingRabbitMq((context, cfg) =>
{
    cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter(false));
});
```

### Handler Naming Convention

Handler classes should match the event class name with `Handler` suffix:
- Event: `PatientCreatedIntegrationEvent`
- Handler: `PatientCreatedIntegrationEventHandler`

This makes it easy to find handlers using Ctrl+T by typing the event name.

### Verification Checklist

- [ ] BuildingBlocks.Application/Messaging/ created with IEventBus, IntegrationEventBase
- [ ] BuildingBlocks.Infrastructure.MassTransit/ created with MassTransitEventBus
- [ ] Shared/IntegrationEvents project created
- [ ] Project references configured correctly (abstractions in Application, implementation in Infrastructure)
- [ ] MassTransit NuGet packages installed in BuildingBlocks.Infrastructure.MassTransit
- [ ] MassTransit configured with RabbitMQ transport
- [ ] IEventBus registered in DI container
- [ ] appsettings.json has correct RabbitMQ connection details
- [ ] Test publish endpoint working (uses IEventBus, not IPublishEndpoint)
- [ ] Messages visible in RabbitMQ Management UI

---

## Summary

You've set up the messaging infrastructure following Clean Architecture and Dependency Inversion:

1. **BuildingBlocks.Application/Messaging/** - Technology-agnostic abstractions (`IEventBus`, `IIntegrationEvent`, `IntegrationEventBase`)
2. **BuildingBlocks.Application/Interfaces/** - `IUnitOfWork` with `QueueIntegrationEvent()` for transactional event publishing
3. **BuildingBlocks.Infrastructure.MassTransit/** - MassTransit implementation (`MassTransitEventBus`, `AddMassTransitEventBus()`)
4. **Shared/IntegrationEvents** - Integration event definitions (public contracts between bounded contexts)
5. **[BC].Infrastructure/Consumers/MassTransit** - MassTransit handlers for incoming integration events
6. **Scheduling.WebApi/Program.cs** - MassTransit registration (composition root)

### Key Architectural Decisions

**Event publishing:**
- All events go through RabbitMQ via MassTransit
- All events get durability, retry, and dead-letter queues
- MediatR is used for CQRS (commands/queries)

**Publishing flow:**
1. Command handler creates entity
2. Command handler queues integration event: `_uow.QueueIntegrationEvent(new PatientCreatedIntegrationEvent(...))`
3. Command handler saves: `await _uow.SaveChangesAsync()`
4. After successful database commit, queued events are published to RabbitMQ

**Framework registration:**
- Registered in the WebApi host (`Scheduling.WebApi/Program.cs`) — the composition root where all infrastructure gets wired up

**MassTransit advantages:**
- **Battle-tested**: Widely used in production across thousands of .NET applications
- **Saga support**: Built-in state machine sagas for distributed transactions
- **Explicit registration**: `AddConsumers(assembly)` gives clear control over what's registered
- **Rich ecosystem**: Scheduling, circuit breakers, rate limiting out of the box
- **IntegrationEventHandler<T>**: Project-specific base class with automatic logging

This architecture ensures:
- **Durability**: All events persisted to message broker
- **Dependency Inversion**: Application layer depends on `IEventBus`, not on MassTransit
- **Transactional Publishing**: Events only published after successful database commit
- **Provider Swappability**: Swap messaging providers by implementing `IEventBus` with an alternative provider

**See Also:**
- [03-rabbitmq-wolverine-setup.md](./03-rabbitmq-wolverine-setup.md) - Wolverine alternative implementation (MIT-licensed)
- [04-integration-events.md](./04-integration-events.md) - Defining and publishing integration events

---

**Next:** [04-integration-events.md](./04-integration-events.md) - Defining and publishing integration events
