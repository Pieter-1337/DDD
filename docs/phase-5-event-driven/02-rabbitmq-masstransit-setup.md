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

This setup uses **three distinct projects** for messaging infrastructure, following DDD principles and Clean Architecture:

| Project | Purpose | Contains |
|---------|---------|----------|
| **BuildingBlocks.Messaging** | Reusable abstractions | `IntegrationEventBase`, `IIntegrationEvent`, MassTransit helpers |
| **Shared/IntegrationEvents** | Integration event definitions | `AppointmentScheduledIntegrationEvent`, `PatientCreatedIntegrationEvent` |
| **[BC].Infrastructure** | BC-specific implementation | MassTransit configuration, publishers, consumers |

### Why Three Separate Projects?

**BuildingBlocks.Messaging** = Generic envelope system (tracking numbers, timestamps)
- Reusable infrastructure that could work in ANY domain
- Contains ONLY technical abstractions, NO domain content
- Example: If you start an e-commerce project, you'd reuse this project

**Shared/IntegrationEvents** = The actual letters (healthcare events)
- Your healthcare domain's public API between bounded contexts
- Contains domain-specific integration events
- Organized by bounded context: `Scheduling/`, `Billing/`, `MedicalRecords/`

**Scheduling/Billing/Infrastructure** = The people writing and reading letters
- BC-specific message handling (publishers and consumers)
- MassTransit configuration for each bounded context
- Each BC references BuildingBlocks.Messaging and Shared/IntegrationEvents

**Key Principle**: Bounded contexts never reference each other directly, only the shared IntegrationEvents project. This preserves autonomy and prevents tight coupling.

### Project Structure Diagram

```
src/
├── BuildingBlocks/
│   ├── BuildingBlocks.Messaging/          # Reusable messaging abstractions
│   │   ├── Abstractions/                  # IntegrationEventBase, IIntegrationEvent
│   │   └── Configuration/                 # MassTransit setup helpers
│   │
│   └── BuildingBlocks.Infrastructure/     # Persistence infrastructure (separate concern)
│       ├── Persistence/                   # EF Core base classes
│       └── Repositories/                  # Repository patterns
│
├── Shared/
│   └── IntegrationEvents/                 # Integration event definitions
│       ├── Scheduling/                    # AppointmentScheduledIntegrationEvent
│       ├── Billing/                       # PaymentProcessedIntegrationEvent
│       └── MedicalRecords/                # RecordUpdatedIntegrationEvent
│
├── Scheduling/
│   ├── Scheduling.Domain/
│   ├── Scheduling.Application/
│   └── Scheduling.Infrastructure/         # References: BuildingBlocks.Messaging, Shared/IntegrationEvents
│       ├── Persistence/                   # EF Core, repositories (internal)
│       └── Messaging/                     # Event publishers and consumers
│
└── Billing/
    ├── Billing.Domain/
    ├── Billing.Application/
    └── Billing.Infrastructure/            # References: BuildingBlocks.Messaging, Shared/IntegrationEvents
        ├── Persistence/
        └── Messaging/
```

### Clean Architecture Layer Dependencies

| Project | BuildingBlocks.Infrastructure | BuildingBlocks.Messaging | Shared/IntegrationEvents |
|---------|------------------------------|-------------------------|--------------------------|
| **{BC}.Domain** | No | No | No |
| **{BC}.Application** | No | Yes (abstractions only) | Yes (event DTOs) |
| **{BC}.Infrastructure** | Yes (EF Core, repositories) | Yes (MassTransit config) | Yes (publishers/consumers) |

**Why Domain references nothing**: Domain is the innermost layer with zero external dependencies. It remains pure and framework-agnostic.

**Why Application references Messaging**: Application layer orchestrates use cases and may need to publish integration events via `IEventBus` abstraction (without knowing the implementation details).

**Why Infrastructure references both**: Infrastructure provides concrete implementations for abstractions defined in inner layers. It bridges persistence (BuildingBlocks.Infrastructure) and messaging (BuildingBlocks.Messaging) concerns.

### Why This Separation?

The three-project split enforces a critical architectural principle: **reusability vs. domain specificity**.

**BuildingBlocks.Messaging** = Reusable in ANY domain
- Contains ONLY technical infrastructure (base classes, MassTransit helpers)
- Zero domain knowledge
- If you start a new e-commerce project tomorrow, you copy this project as-is

**Shared/IntegrationEvents** = THIS domain's public API
- Healthcare-specific events (appointments, patients, billing)
- Defines the contract between YOUR bounded contexts
- Each new domain gets its own IntegrationEvents project with domain-specific events

**Bounded Context Independence**
- Scheduling and Billing bounded contexts never reference each other
- They only reference the shared IntegrationEvents project
- This prevents tight coupling while enabling communication through events

This separation ensures you're not mixing technical abstractions (the envelope system) with domain semantics (the letters inside the envelopes).

---

## 4. Implementation Steps

### Step 1: Create Projects

```bash
# Create BuildingBlocks.Messaging project
dotnet new classlib -n BuildingBlocks.Messaging -o BuildingBlocks/BuildingBlocks.Messaging
dotnet sln add BuildingBlocks/BuildingBlocks.Messaging

# Create Shared/IntegrationEvents project
dotnet new classlib -n IntegrationEvents -o Shared/IntegrationEvents
dotnet sln add Shared/IntegrationEvents
```

### Step 2: Add NuGet Packages

**Note:** This project uses Central Package Management - see [Phase 1 documentation](../phase-1-ddd-fundamentals/03-building-patient-aggregate.md#understanding-central-package-management-cpm) for details on how CPM works.

```bash
# Add packages to BuildingBlocks.Messaging (versions come from Directory.Packages.props)
cd BuildingBlocks/BuildingBlocks.Messaging
dotnet add package MassTransit
dotnet add package MassTransit.RabbitMQ
```

Verify `Directory.Packages.props` contains:
```xml
<PackageVersion Include="MassTransit" Version="8.*" />
<PackageVersion Include="MassTransit.RabbitMQ" Version="8.*" />
```

### Step 3: Set Up Project References

```bash
# IntegrationEvents references BuildingBlocks.Messaging (for IntegrationEventBase)
cd Shared/IntegrationEvents
dotnet add reference ../../BuildingBlocks/BuildingBlocks.Messaging

# Scheduling.Infrastructure references both
cd ../../Core/Scheduling/Scheduling.Infrastructure
dotnet add reference ../../../BuildingBlocks/BuildingBlocks.Messaging
dotnet add reference ../../../Shared/IntegrationEvents
```

### Step 4: Create IntegrationEventBase

Create the base class for all integration events in `BuildingBlocks.Messaging/Abstractions/IntegrationEventBase.cs`:

```csharp
namespace BuildingBlocks.Messaging.Abstractions;

/// <summary>
/// Base class for all integration events.
/// Integration events cross bounded context boundaries via message broker.
/// </summary>
/// <remarks>
/// This class is part of the Shared Kernel (abstractions only).
/// It provides technical infrastructure for distributed messaging:
/// - EventId enables idempotent message handling
/// - OccurredAt provides event ordering and auditing
/// - CorrelationId enables distributed tracing across bounded contexts
///
/// IMPORTANT: This class should contain NO domain-specific logic.
/// Actual event definitions belong in the Shared/IntegrationEvents project.
/// </remarks>
public abstract record IntegrationEventBase
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

**Why this lives in BuildingBlocks.Messaging**: Provides reusable technical infrastructure for ALL integration events. Contains NO domain-specific content and could work in any domain (e-commerce, banking, healthcare).

### Step 5: Create Example Integration Event

Create an example event in `Shared/IntegrationEvents/Scheduling/PatientCreatedIntegrationEvent.cs`:

```csharp
using BuildingBlocks.Messaging.Abstractions;

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

### Step 6: Set Up RabbitMQ in Docker

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

### Step 7: Configure MassTransit

Create `Scheduling.Infrastructure/Messaging/MassTransitConfiguration.cs`:

```csharp
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Infrastructure.Messaging;

public static class MassTransitConfiguration
{
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMassTransit(x =>
        {
            // Register consumers from this assembly
            x.AddConsumers(typeof(MassTransitConfiguration).Assembly);

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

Register in `Program.cs`:

```csharp
builder.Services.AddMessaging(builder.Configuration);
```

### Step 8: Create Test Endpoint

Add a minimal endpoint to test message publishing:

```csharp
using IntegrationEvents.Scheduling;
using MassTransit;

app.MapPost("/test-publish", async (IPublishEndpoint publishEndpoint) =>
{
    await publishEndpoint.Publish(new PatientCreatedIntegrationEvent
    {
        PatientId = Guid.NewGuid(),
        Email = "test@example.com",
        FullName = "Test Patient",
        DateOfBirth = new DateTime(1985, 6, 15)
    });

    return Results.Ok("Message published");
});
```

### Step 9: Verify the Setup

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
- [ ] BuildingBlocks.Messaging project created with IntegrationEventBase
- [ ] Shared/IntegrationEvents project created
- [ ] Project references configured correctly
- [ ] MassTransit NuGet packages installed
- [ ] MassTransit configured with RabbitMQ transport
- [ ] appsettings.json has correct RabbitMQ connection details
- [ ] Test publish endpoint working
- [ ] Messages visible in RabbitMQ Management UI

---

## Summary

You've set up the messaging infrastructure with three distinct projects:

1. **BuildingBlocks.Messaging** - Reusable technical abstractions (IntegrationEventBase, MassTransit helpers)
2. **Shared/IntegrationEvents** - Your healthcare domain's integration events
3. **[BC].Infrastructure** - BC-specific message handling (publishers, consumers)

This architecture ensures:
- Clear separation between technical abstractions and domain events
- No coupling between bounded contexts (they only reference Shared/IntegrationEvents)
- Reusability of BuildingBlocks.Messaging across different domains
- Clean Architecture principles with proper dependency inversion

Next: [03-integration-events.md](./03-integration-events.md) - Defining and publishing integration events
