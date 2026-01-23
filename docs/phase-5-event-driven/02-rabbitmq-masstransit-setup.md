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

## Docker Setup

### docker-compose.yml

Add RabbitMQ to your docker-compose:

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

- [ ] Docker Compose file with RabbitMQ service
- [ ] RabbitMQ container running (`docker-compose ps`)
- [ ] RabbitMQ Management UI accessible at `http://localhost:15672`
- [ ] MassTransit NuGet packages installed
- [ ] `IntegrationEvent` base class created
- [ ] MassTransit configured with RabbitMQ transport
- [ ] Health check for RabbitMQ (optional but recommended)
- [ ] Test publish endpoint working
- [ ] Messages visible in RabbitMQ Management UI

---

> Next: [03-integration-events.md](./03-integration-events.md) - Defining and publishing integration events
