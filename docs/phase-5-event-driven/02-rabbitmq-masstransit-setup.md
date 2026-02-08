# RabbitMQ & MassTransit Setup

This document covers setting up the messaging infrastructure for event-driven communication between bounded contexts using RabbitMQ and MassTransit.

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

---

## 2. MassTransit Licensing

**MassTransit v9+ (2026 onwards) requires a commercial license** for production use:
- Small/Medium Business: $400/month or $4,000/year
- Large Organizations: $1,200/month or $12,000/year
- Local development/evaluation: Free (temporary license)

**MassTransit v8 remains open source (Apache 2.0)** and is maintained through end of 2026.

**For this learning project**: Use MassTransit v8.x (open source). The concepts you learn transfer to any messaging framework.

**Open source alternatives**:
- [Wolverine](https://wolverine.netlify.app/) - Modern .NET messaging (MIT)
- [Rebus](https://github.com/rebus-org/Rebus) - Mature service bus (MIT)
- [Brighter](https://github.com/BrighterCommand/Brighter) - CQRS/messaging (MIT)

---

## 3. Architecture: Project Structure for Messaging

This setup separates messaging concerns across multiple projects, following Clean Architecture and the Dependency Inversion Principle:

| Project | Purpose | Contains |
|---------|---------|----------|
| **BuildingBlocks.Application** | Application-layer abstractions | `IEventBus`, `IIntegrationEvent`, `IntegrationEventBase` |
| **BuildingBlocks.Infrastructure.MassTransit** | MassTransit provider | `MassTransitEventBus`, `AddMassTransitEventBus()` extension |
| **Shared/IntegrationEvents** | Integration event definitions | `PatientCreatedIntegrationEvent`, etc. |
| **[BC].Infrastructure** | BC-specific consumers | Message consumers for the bounded context |
| **WebApi** | Composition root | MassTransit registration with consumer discovery |

### Where MassTransit Gets Registered

MassTransit is registered in `WebApi/Program.cs` (the composition root), **not** in bounded context infrastructure layers:

```csharp
// WebApi/Program.cs
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    // Register consumers from bounded context assemblies
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});
```

### Why Register in the Host, Not Per-Bounded Context?

**In a modular monolith** (like this project):
- One WebApi hosts all bounded contexts
- One MassTransit registration handles all messaging
- Consumer assemblies are passed to `AddConsumers()` for discovery

**In a microservices architecture**:
- Each service has its own API/host project
- Each service registers MassTransit in its own `Program.cs`
- Each service only knows about its own consumers

**The key insight**: There is no benefit to abstracting MassTransit registration per-bounded context. The host project is already the composition root where infrastructure gets wired up. Adding a per-BC registration layer (like `AddSchedulingMessaging()`) only adds complexity without benefit.

### What Lives Where

**BuildingBlocks.Infrastructure.MassTransit** provides:
- `MassTransitEventBus` - implements `IEventBus` abstraction
- `AddMassTransitEventBus()` - extension method for host registration
- RabbitMQ transport configuration and retry policies

**[BC].Infrastructure** provides:
- Message consumers (e.g., `PatientCreatedConsumer`)
- These get discovered via `AddConsumers(assembly)`

**WebApi/Host** does:
- Calls `AddMassTransitEventBus()` once
- Passes in assemblies containing consumers

### Project Structure Diagram

```
src/
├── BuildingBlocks/
│   ├── BuildingBlocks.Application/           # Application-layer abstractions
│   │   └── Messaging/                        # Messaging abstractions
│   │       ├── IEventBus.cs                  # Publisher interface
│   │       ├── IIntegrationEvent.cs          # Marker interface
│   │       └── IntegrationEventBase.cs       # Base class with metadata
│   │
│   └── BuildingBlocks.Infrastructure.MassTransit/
│       ├── MassTransitEventBus.cs            # IEventBus implementation
│       └── MassTransitExtensions.cs          # AddMassTransitEventBus() extension
│
├── Shared/
│   └── IntegrationEvents/                    # Integration event definitions
│       ├── Scheduling/                       # PatientCreatedIntegrationEvent, etc.
│       ├── Billing/                          # PaymentProcessedIntegrationEvent
│       └── MedicalRecords/                   # RecordUpdatedIntegrationEvent
│
├── Core/Scheduling/
│   ├── Scheduling.Domain/
│   ├── Scheduling.Application/               # Uses IEventBus abstraction
│   └── Scheduling.Infrastructure/
│       └── Messaging/                        # Consumers live here
│
└── WebApi/
    └── Program.cs                            # MassTransit registered here
```

### Clean Architecture Layer Dependencies

| Project | BuildingBlocks.Application | BuildingBlocks.Infrastructure.MassTransit | Shared/IntegrationEvents |
|---------|---------------------------|------------------------------------------|--------------------------|
| **{BC}.Domain** | No | No | No |
| **{BC}.Application** | Yes (IEventBus) | No | Yes (event DTOs) |
| **{BC}.Infrastructure** | Yes | No | Yes (consumers) |
| **WebApi** | Yes | Yes (registration) | No |

**Why BC.Infrastructure does NOT reference BuildingBlocks.Infrastructure.MassTransit**:
- Consumers use MassTransit's `IConsumer<T>` interface (from MassTransit NuGet)
- They don't need the `AddMassTransitEventBus()` extension
- Only the host needs the registration extension

### Provider Swappability

This architecture allows swapping messaging providers without changing business logic:

**Current setup** (MassTransit + RabbitMQ):
```csharp
// In WebApi/Program.cs
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});
```

**If switching to Wolverine**:
1. Create `BuildingBlocks.Infrastructure.Wolverine` project
2. Implement `IEventBus` using Wolverine
3. Change host registration:
```csharp
// In WebApi/Program.cs
builder.Services.AddWolverineEventBus(builder.Configuration);
```

**No changes required in**:
- Domain layer (still pure)
- Application layer (still uses `IEventBus` abstraction)
- Business logic (command handlers)

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

# Scheduling.Infrastructure references both
cd ../Scheduling.Infrastructure
dotnet add reference ../../../BuildingBlocks/BuildingBlocks.Application
dotnet add reference ../../../Shared/IntegrationEvents

# WebApi references BuildingBlocks.Infrastructure.MassTransit (composition root)
cd ../../../Presentation/WebApi
dotnet add reference ../../BuildingBlocks/BuildingBlocks.Infrastructure.MassTransit
```

### Step 4: Create Messaging Abstractions

Create the messaging abstractions in `BuildingBlocks.Application/Messaging/`:

**IEventBus.cs**:
```csharp
namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Abstraction for publishing integration events to a message broker.
/// </summary>
/// <remarks>
/// This interface allows the Application layer to publish events without
/// coupling to a specific messaging implementation (MassTransit, Wolverine, etc.).
/// The Infrastructure layer provides the concrete implementation.
/// </remarks>
public interface IEventBus
{
    /// <summary>
    /// Publishes an integration event to the message broker.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent;
}
```

**IIntegrationEvent.cs**:
```csharp
namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Marker interface for integration events.
/// Integration events represent facts that have occurred in one bounded context
/// and may be of interest to other bounded contexts.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
}
```

**IntegrationEventBase.cs**:
```csharp
namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Base class for all integration events.
/// Integration events cross bounded context boundaries via message broker.
/// </summary>
/// <remarks>
/// This class provides technical infrastructure for distributed messaging:
/// - EventId enables idempotent message handling
/// - OccurredAt provides event ordering and auditing
/// - CorrelationId enables distributed tracing across bounded contexts
///
/// IMPORTANT: This class should contain NO domain-specific logic.
/// Actual event definitions belong in the Shared/IntegrationEvents project.
/// </remarks>
public abstract record IntegrationEventBase : IIntegrationEvent
{
    /// <summary>
    /// Unique identifier for this event instance.
    /// Used for idempotent message handling - consumers can track processed EventIds
    /// to avoid processing the same event multiple times.
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// When the event was created (UTC).
    /// Useful for event ordering, auditing, and troubleshooting.
    /// </summary>
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// Links this event to the original request/transaction that triggered it.
    /// Populated from HttpContext or command context.
    /// </summary>
    public string? CorrelationId { get; init; }
}
```

**Why this lives in BuildingBlocks.Application**: These are application-layer abstractions that define contracts without coupling to infrastructure. This allows the Application layer to publish events via `IEventBus` without knowing whether it's MassTransit, Wolverine, or any other provider.

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

Create an example event in `Shared/IntegrationEvents/Scheduling/PatientCreatedIntegrationEvent.cs`:

```csharp
using BuildingBlocks.Application.Messaging;

namespace IntegrationEvents.Scheduling;

/// <summary>
/// Published when a new patient is created in the Scheduling bounded context.
/// Consumed by Billing and MedicalRecords bounded contexts.
/// </summary>
public record PatientCreatedIntegrationEvent : IntegrationEventBase
{
    public Guid PatientId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public DateTime DateOfBirth { get; init; }
}
```

### Step 7: Set Up RabbitMQ in Docker

Create `docker-compose.yml` in your solution root:

```yaml
services:
  rabbitmq:
    image: rabbitmq:3-management
    container_name: ddd-rabbitmq
    restart: unless-stopped
    ports:
      - "0.0.0.0:5672:5672"    # AMQP port (messaging)
      - "0.0.0.0:15672:15672"  # Management UI
    environment:
      - RABBITMQ_DEFAULT_USER=guest
      - RABBITMQ_DEFAULT_PASS=guest
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "check_running"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  rabbitmq_data:
```

**Note**: The `0.0.0.0:5672:5672` format explicitly binds container ports to all network interfaces, ensuring the ports are accessible from your .NET application on Windows.

**Data persistence**: The `volumes` section ensures RabbitMQ data (including messages and DLQ) persists across container restarts and PC reboots. Data is only lost if you run `docker-compose down -v` (the `-v` flag deletes volumes).

Start RabbitMQ:
```bash
docker-compose up -d
```

Verify RabbitMQ is running:
```bash
docker-compose ps
# Expected: ddd-rabbitmq status "Up"
```

Access RabbitMQ Management UI:
- URL: http://localhost:15672
- Username: guest
- Password: guest

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

Register in `WebApi/Program.cs` (the composition root):

```csharp
using BuildingBlocks.Infrastructure.MassTransit.Configuration;

// Add MassTransit for event-driven messaging
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    // Register consumers from bounded context assemblies
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});
```

**Why register here?** The WebApi is the composition root - the single place where all infrastructure gets wired up. In a monolith, one registration handles all bounded contexts. In microservices, each service's API would have its own registration.

### Step 9: Create Test Endpoint

Add a minimal endpoint to test message publishing:

```csharp
using BuildingBlocks.Application.Messaging;
using IntegrationEvents.Scheduling;

app.MapPost("/test-publish", async (IEventBus eventBus) =>
{
    await eventBus.PublishAsync(new PatientCreatedIntegrationEvent
    {
        PatientId = Guid.NewGuid(),
        Email = "test@example.com",
        FullName = "Test Patient",
        DateOfBirth = new DateTime(1985, 6, 15)
    });

    return Results.Ok("Message published");
});
```

Notice we inject `IEventBus` (abstraction) instead of `IPublishEndpoint` (MassTransit-specific). This keeps the endpoint decoupled from the messaging provider.

### Step 10: Verify the Setup

1. Start RabbitMQ (if not already running): `docker-compose up -d`
2. Run your application and POST to `/test-publish`
3. Check RabbitMQ Management UI at http://localhost:15672:
   - Queues tab shows created queues
   - Exchanges tab shows the exchange
   - Messages can be viewed in queue details

---

## 5. Optional: Auto-Start Docker on F5 in Visual Studio

To automatically start RabbitMQ when you press F5 in Visual Studio, create an MSBuild target that runs before build.

### Create PowerShell Script

Create `scripts/ensure-docker.ps1` in your solution root:

```powershell
param(
    [int]$TimeoutSeconds = 60
)

Write-Host "Ensuring Docker containers are running..." -ForegroundColor Cyan

# Check if Docker Desktop is running
$dockerProcess = Get-Process "Docker Desktop" -ErrorAction SilentlyContinue
if (-not $dockerProcess) {
    Write-Host "ERROR: Docker Desktop is not running." -ForegroundColor Red
    Write-Host "Please start Docker Desktop and try again." -ForegroundColor Yellow
    exit 1
}

# Check if RabbitMQ container is running
$rabbitmqRunning = docker ps --filter "name=ddd-rabbitmq" --filter "status=running" --format "{{.Names}}"

if ($rabbitmqRunning) {
    Write-Host "RabbitMQ container already running." -ForegroundColor Green
    exit 0
}

# Start RabbitMQ container
Write-Host "Starting RabbitMQ container..." -ForegroundColor Yellow
Push-Location $PSScriptRoot\..
docker-compose up -d
Pop-Location

# Wait for RabbitMQ health check
Write-Host "Waiting for RabbitMQ to be ready..." -ForegroundColor Yellow
$elapsed = 0
while ($elapsed -lt $TimeoutSeconds) {
    $health = docker inspect --format="{{.State.Health.Status}}" ddd-rabbitmq 2>$null
    if ($health -eq "healthy") {
        Write-Host "RabbitMQ is ready!" -ForegroundColor Green
        exit 0
    }
    Start-Sleep -Seconds 2
    $elapsed += 2
}

Write-Host "ERROR: RabbitMQ health check timed out after $TimeoutSeconds seconds." -ForegroundColor Red
exit 1
```

### Create MSBuild Target

Create `Directory.Build.targets` in your solution root (same directory as .sln file):

```xml
<Project>

  <!--
    Docker startup targets for WebApi projects.
    These targets ensure Docker services (RabbitMQ) are running before build/run.
    Automatically inherited by all projects in the solution.

    Strategy: Use a SINGLE target that runs on EVERY Build, even cached builds.
    - The script itself is idempotent (fast exit if containers already running)
    - This ensures F5 ALWAYS checks and starts containers if needed
  -->

  <Target Name="EnsureDockerServices" BeforeTargets="Build">
    <Exec Command="powershell -ExecutionPolicy Bypass -File &quot;$(MSBuildThisFileDirectory)scripts\ensure-docker.ps1&quot;"
          IgnoreExitCode="false" />
  </Target>

</Project>
```

This approach:
- Automatically applies to ALL projects in the solution
- Runs before every build
- The PowerShell script exits quickly if containers are already running
- Ensures Docker is always checked before F5, even on cached builds

---

## 6. Reference

### Docker Commands Cheat Sheet

```bash
# Start containers in background
docker-compose up -d

# Check if containers are running
docker-compose ps

# View logs
docker-compose logs rabbitmq
docker-compose logs -f rabbitmq  # Follow/stream logs (real-time)

# Stop containers (keeps data)
docker-compose stop

# Start stopped containers
docker-compose start

# Stop and remove containers (keeps data in volumes)
docker-compose down

# Stop, remove containers AND delete data
docker-compose down -v

# Restart a specific service
docker-compose restart rabbitmq

# Execute commands inside running container
docker exec -it ddd-rabbitmq bash
```

### MassTransit Conventions

MassTransit uses conventions for queue/exchange naming:

```
Event: PatientCreatedIntegrationEvent
  └── Exchange: IntegrationEvents.Scheduling:PatientCreatedIntegrationEvent
      └── Queue: patient-created-integration-event (for each consumer)
```

Customize endpoint names with kebab-case:

```csharp
x.UsingRabbitMq((context, cfg) =>
{
    cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter(false));
});
```

### In-Memory Transport for Testing

For integration tests, use the in-memory transport:

```csharp
// In test setup
services.AddMassTransitTestHarness(x =>
{
    x.AddConsumer<PatientCreatedIntegrationEventConsumer>();
});

// In tests
[TestMethod]
public async Task Should_Consume_PatientCreated_Event()
{
    var harness = _serviceProvider.GetRequiredService<ITestHarness>();
    await harness.Start();

    var @event = new PatientCreatedIntegrationEvent
    {
        PatientId = Guid.NewGuid(),
        Email = "test@test.com",
        FullName = "Test Patient",
        DateOfBirth = new DateTime(1985, 6, 15)
    };

    await harness.Bus.Publish(@event);

    Assert.IsTrue(await harness.Consumed.Any<PatientCreatedIntegrationEvent>());
}
```

### Verification Checklist

- [ ] RabbitMQ running in Docker
- [ ] RabbitMQ Management UI accessible at http://localhost:15672
- [ ] Can log into Management UI with guest/guest credentials
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

1. **BuildingBlocks.Application/Messaging/** - Technology-agnostic abstractions (`IEventBus`, `IntegrationEventBase`)
2. **BuildingBlocks.Infrastructure.MassTransit/** - MassTransit implementation (`MassTransitEventBus`, `AddMassTransitEventBus()`)
3. **Shared/IntegrationEvents** - Domain-specific integration events
4. **[BC].Infrastructure** - BC-specific consumers
5. **WebApi/Program.cs** - Single MassTransit registration (composition root)

**Key architectural decision**: MassTransit is registered once in the host (`WebApi/Program.cs`), not per-bounded context. This keeps things simple:
- In a **monolith**: One API, one MassTransit registration, multiple consumer assemblies
- In **microservices**: Each service has its own API with its own MassTransit registration

There's no need for per-BC registration abstraction - it adds complexity without benefit. The host is already the composition root where infrastructure gets wired up.

This architecture ensures:
- **Dependency Inversion**: Application layer depends on `IEventBus`, not MassTransit
- **Provider Swappability**: Swap MassTransit for Wolverine/Rebus by changing one line in `Program.cs`
- **Simplicity**: One registration point, no unnecessary abstraction layers
- **No Coupling**: Bounded contexts only reference Shared/IntegrationEvents

Next: [03-integration-events.md](./03-integration-events.md) - Defining and publishing integration events
