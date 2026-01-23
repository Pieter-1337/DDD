# RabbitMQ & MassTransit Setup

## Overview

This document covers setting up the messaging infrastructure:
- **RabbitMQ** - The message broker (runs in Docker)
- **MassTransit** - .NET abstraction over message brokers

---

## Why MassTransit?

MassTransit is a .NET library that provides:

| Feature | Description |
|---------|-------------|
| **Broker abstraction** | Switch between RabbitMQ, Azure Service Bus, Amazon SQS |
| **Message serialization** | JSON, XML, BSON out of the box |
| **Retry policies** | Configurable retry with exponential backoff |
| **Error handling** | Dead letter queues, fault handling |
| **Saga support** | State machine-based orchestration |
| **Testing** | In-memory transport for tests |

Without MassTransit, you'd write low-level RabbitMQ code:

```csharp
// Raw RabbitMQ - lots of boilerplate
var factory = new ConnectionFactory { HostName = "localhost" };
using var connection = factory.CreateConnection();
using var channel = connection.CreateModel();

channel.QueueDeclare("patient-created", durable: true, exclusive: false, autoDelete: false);
var body = JsonSerializer.SerializeToUtf8Bytes(message);
channel.BasicPublish(exchange: "", routingKey: "patient-created", body: body);
```

With MassTransit:

```csharp
// MassTransit - clean and simple
await _publishEndpoint.Publish(new PatientCreatedIntegrationEvent { ... });
```

---

## Running RabbitMQ - Three Options

You have three options for running RabbitMQ:

| Option | Pros | Cons |
|--------|------|------|
| **Docker** | Isolated, easy cleanup, matches prod | Need to learn Docker basics |
| **Windows Installer** | Native, no Docker needed | Installs Erlang system-wide, harder cleanup |
| **Cloud (CloudAMQP)** | No local install, free tier | Requires internet, slight latency |

Choose what works for you. All three work with MassTransit.

---

## Option 1: Docker (Recommended)

### What is Docker?

Docker runs applications in isolated "containers" - like lightweight virtual machines.

```
Without Docker:                    With Docker:
───────────────                    ────────────
Install Erlang on Windows          docker-compose up
Install RabbitMQ on Windows        (Downloads and runs everything)
Configure environment variables
Start RabbitMQ service             docker-compose down
Uninstall when done (messy)        (Clean removal, nothing left behind)
```

**Key concepts:**
- **Image** - A template (like `rabbitmq:3-management`)
- **Container** - A running instance of an image
- **docker-compose** - Tool to run multiple containers together

### Install Docker Desktop

1. Download from https://www.docker.com/products/docker-desktop/
2. Install and restart Windows
3. Open Docker Desktop (it runs in the background)
4. Verify in terminal:
   ```bash
   docker --version
   # Docker version 24.x.x
   ```

### docker-compose.yml

Create a `docker-compose.yml` file in your solution root:

```yaml
services:
  rabbitmq:
    image: rabbitmq:3-management
    container_name: ddd-rabbitmq
    ports:
      - "5672:5672"    # AMQP port (messaging)
      - "15672:15672"  # Management UI
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

  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: ddd-sqlserver
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourStrong@Password
    ports:
      - "1433:1433"
    volumes:
      - sqlserver_data:/var/opt/mssql

volumes:
  rabbitmq_data:
  sqlserver_data:
```

### What About Data Persistence?

Will you lose your messages (including DLQ) if Docker stops or you restart your PC?

**No** - the `volumes` in docker-compose.yml persist data to disk:

```
WITHOUT volumes:                    WITH volumes (our setup):
────────────────                    ─────────────────────────

┌─────────────┐                     ┌─────────────┐
│  Container  │                     │  Container  │
│  (RabbitMQ) │                     │  (RabbitMQ) │
│             │                     │      │      │
│  [messages] │ ← Lost on stop      │      │      │
└─────────────┘                     └──────┼──────┘
                                           │
                                           ▼
                                    ┌─────────────┐
                                    │   Volume    │ ← Persists on disk
                                    │(rabbitmq_   │
                                    │   data)     │
                                    └─────────────┘
```

This line in docker-compose.yml saves RabbitMQ data:
```yaml
volumes:
  - rabbitmq_data:/var/lib/rabbitmq
```

**When is data preserved vs lost?**

| Action | Messages Preserved? |
|--------|---------------------|
| Restart PC | Yes |
| `docker-compose stop` | Yes |
| `docker-compose down` | Yes |
| `docker-compose down -v` | **No** (the `-v` deletes volumes) |

**Verify your volumes exist:**

```bash
# List volumes
docker volume ls
# DRIVER    VOLUME NAME
# local     ddd_rabbitmq_data

# See where it's stored on disk
docker volume inspect ddd_rabbitmq_data
```

### Start Services

```bash
# Start all services
docker-compose up -d

# Check status
docker-compose ps

# View RabbitMQ logs
docker-compose logs rabbitmq

# Access RabbitMQ Management UI
# http://localhost:15672
# Username: guest
# Password: guest
```

### Docker Commands Cheat Sheet

```bash
# Start containers (runs in background)
docker-compose up -d

# Stop containers (keeps data)
docker-compose stop

# Stop and remove containers (keeps data in volumes)
docker-compose down

# Stop, remove containers AND delete data
docker-compose down -v

# View running containers
docker-compose ps

# View logs
docker-compose logs rabbitmq
docker-compose logs -f rabbitmq  # Follow/stream logs

# Restart a specific service
docker-compose restart rabbitmq
```

---

## Option 2: Windows Installer (No Docker)

If you prefer not to use Docker, install RabbitMQ directly on Windows.

### Step 1: Install Erlang

RabbitMQ requires Erlang (the language it's built with).

1. Download from https://www.erlang.org/downloads
2. Run installer, accept defaults
3. Verify in new terminal:
   ```bash
   erl -version
   # Erlang (SMP,ASYNC_THREADS) (BEAM) emulator version 13.x
   ```

### Step 2: Install RabbitMQ

1. Download from https://www.rabbitmq.com/install-windows.html
2. Run installer
3. RabbitMQ runs as a Windows service (starts automatically)

### Step 3: Enable Management UI

Open RabbitMQ Command Prompt (from Start menu) and run:

```bash
rabbitmq-plugins enable rabbitmq_management
```

### Step 4: Access Management UI

- URL: http://localhost:15672
- Username: `guest`
- Password: `guest`

### Uninstalling Later

1. Uninstall RabbitMQ from Windows Settings > Apps
2. Uninstall Erlang from Windows Settings > Apps
3. Delete `C:\Users\{you}\AppData\Roaming\RabbitMQ` (data folder)

---

## Option 3: Cloud Service (CloudAMQP)

No local installation needed. CloudAMQP offers a free tier.

### Step 1: Create Account

1. Go to https://www.cloudamqp.com/
2. Sign up (free tier: "Little Lemur")
3. Create a new instance (choose region close to you)

### Step 2: Get Connection Details

From your instance dashboard, copy:
- **Host**: `something.rmq.cloudamqp.com`
- **Username**: (shown in dashboard)
- **Password**: (shown in dashboard)
- **Virtual Host**: (usually same as username)

### Step 3: Configure appsettings.json

```json
{
  "RabbitMQ": {
    "Host": "sparrow.rmq.cloudamqp.com",
    "VirtualHost": "your-vhost",
    "Username": "your-username",
    "Password": "your-password"
  }
}
```

### Free Tier Limits

- 1 million messages/month
- 20 concurrent connections
- Limited queues

Fine for learning, but consider Docker for unlimited local development.

---

## NuGet Packages

Add these packages to your Infrastructure project:

```bash
# Core MassTransit
dotnet add package MassTransit

# RabbitMQ transport
dotnet add package MassTransit.RabbitMQ
```

For the Application project (if you define contracts there):

```bash
# Only if you need IBus/IPublishEndpoint interfaces
dotnet add package MassTransit.Abstractions
```

---

## Project Structure

```
src/
├── BuildingBlocks/
│   └── BuildingBlocks.Messaging/           <- NEW: Shared messaging infrastructure
│       ├── IntegrationEvent.cs             <- Base class for integration events
│       ├── IMessageBus.cs                  <- Abstraction (optional)
│       └── BuildingBlocks.Messaging.csproj
│
├── Core/Scheduling/
│   ├── Scheduling.Application/
│   │   └── IntegrationEvents/              <- NEW: Integration event definitions
│   │       └── PatientCreatedIntegrationEvent.cs
│   │
│   └── Scheduling.Infrastructure/
│       └── Messaging/                      <- NEW: MassTransit configuration
│           ├── MassTransitConfiguration.cs
│           └── Consumers/                  <- Event consumers
│               └── ...
│
└── Contracts/                              <- ALTERNATIVE: Shared contracts project
    └── Scheduling.Contracts/
        └── PatientCreatedIntegrationEvent.cs
```

### Where to Put Integration Events?

Two common approaches:

**Option A: Per-context Contracts project (Recommended for learning)**
```
Each context publishes a Contracts package that other contexts reference
+ Clear ownership
+ Explicit dependencies
- More packages to manage
```

**Option B: Shared Contracts project**
```
All integration events in one shared project
+ Simple
+ Easy to find all events
- Can lead to coupling
```

For this project, we'll start with events in `Scheduling.Application/IntegrationEvents/`.

---

## Base Integration Event

Create a base class for all integration events:

```csharp
// BuildingBlocks.Messaging/IntegrationEvent.cs
namespace BuildingBlocks.Messaging;

/// <summary>
/// Base class for all integration events.
/// Integration events cross bounded context boundaries via message broker.
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>
    /// Unique identifier for this event instance (for idempotency)
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// When the event was created
    /// </summary>
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Correlation ID for distributed tracing
    /// </summary>
    public string? CorrelationId { get; init; }
}
```

---

## MassTransit Configuration

### Basic Setup

```csharp
// Scheduling.Infrastructure/Messaging/MassTransitConfiguration.cs
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

                // Configure endpoints for all registered consumers
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
```

### Configuration in appsettings.json

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

### Register in Program.cs

```csharp
// Program.cs
builder.Services.AddMessaging(builder.Configuration);
```

---

## Testing the Connection

### Health Check

```csharp
// Add health check for RabbitMQ
builder.Services.AddHealthChecks()
    .AddRabbitMQ(
        rabbitConnectionString: "amqp://guest:guest@localhost:5672",
        name: "rabbitmq",
        tags: new[] { "messaging", "rabbitmq" });
```

### Simple Publisher Test

Create a minimal endpoint to test publishing:

```csharp
// In a controller or minimal API
app.MapPost("/test-publish", async (IPublishEndpoint publishEndpoint) =>
{
    await publishEndpoint.Publish(new PatientCreatedIntegrationEvent
    {
        PatientId = Guid.NewGuid(),
        Email = "test@example.com",
        FullName = "Test Patient",
        CreatedAt = DateTime.UtcNow
    });

    return Results.Ok("Message published");
});
```

Check RabbitMQ Management UI at `http://localhost:15672`:
- Queues tab shows created queues
- Exchanges tab shows the exchange
- Messages can be viewed in queue details

---

## MassTransit Conventions

MassTransit uses conventions for queue/exchange naming:

```
Event: PatientCreatedIntegrationEvent
  └── Exchange: Scheduling.Application.IntegrationEvents:PatientCreatedIntegrationEvent
      └── Queue: patient-created-integration-event (for each consumer)
```

### Customizing Endpoint Names

```csharp
x.UsingRabbitMq((context, cfg) =>
{
    // Use kebab-case for queue names
    cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter(false));
});
```

---

## Retry Configuration

Configure retry policies for resilience:

```csharp
x.UsingRabbitMq((context, cfg) =>
{
    cfg.Host(...);

    // Global retry policy
    cfg.UseMessageRetry(r =>
    {
        r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30)
        );

        // Don't retry certain exceptions
        r.Ignore<ValidationException>();
        r.Ignore<ArgumentException>();
    });

    cfg.ConfigureEndpoints(context);
});
```

---

## In-Memory Transport for Testing

For integration tests, use the in-memory transport:

```csharp
// In test setup
services.AddMassTransitTestHarness(x =>
{
    x.AddConsumer<PatientCreatedIntegrationEventConsumer>();
});
```

Usage in tests:

```csharp
[TestMethod]
public async Task Should_Consume_PatientCreated_Event()
{
    // Arrange
    var harness = _serviceProvider.GetRequiredService<ITestHarness>();
    await harness.Start();

    var @event = new PatientCreatedIntegrationEvent
    {
        PatientId = Guid.NewGuid(),
        Email = "test@test.com"
    };

    // Act
    await harness.Bus.Publish(@event);

    // Assert
    Assert.IsTrue(await harness.Consumed.Any<PatientCreatedIntegrationEvent>());
}
```

---

## Verification Checklist

- [ ] RabbitMQ running (Docker, Windows service, or CloudAMQP)
- [ ] RabbitMQ Management UI accessible (http://localhost:15672 or cloud dashboard)
- [ ] Can log into Management UI with credentials
- [ ] MassTransit NuGet packages installed
- [ ] `IntegrationEvent` base class created
- [ ] MassTransit configured with RabbitMQ transport
- [ ] appsettings.json has correct RabbitMQ connection details
- [ ] Health check for RabbitMQ (optional but recommended)
- [ ] Test publish endpoint working
- [ ] Messages visible in RabbitMQ Management UI

---

> Next: [03-integration-events.md](./03-integration-events.md) - Defining and publishing integration events
