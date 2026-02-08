# Integration Events - Publishing and Consuming

## Overview

Integration events enable asynchronous communication between bounded contexts via RabbitMQ/MassTransit.

This document covers:
- Defining integration events
- Publishing from command handlers via `_uow.QueueIntegrationEvent()`
- Consuming in bounded contexts
- The publish-subscribe pattern

---

## Architecture

```
+-----------------------------------------------------------------------------+
|                         EVENT ARCHITECTURE                                   |
+-----------------------------------------------------------------------------+

Integration Events
+------------------------------------------------------------------------+
| - Published to RabbitMQ via MassTransit                                 |
| - Cross bounded context communication                                   |
| - Public contract between services                                      |
| - Queued in command handler via _uow.QueueIntegrationEvent()            |
| - Published after SaveChangesAsync() succeeds                           |
| - Get durability, retry, and dead-letter queues                         |
+------------------------------------------------------------------------+
```

### Project Location

Integration events live in a shared location accessible by all bounded contexts:

```
Shared/
+-- IntegrationEvents/
    +-- Scheduling/
    |   +-- PatientCreatedIntegrationEvent.cs
    |   +-- AppointmentScheduledIntegrationEvent.cs
    |   +-- AppointmentCancelledIntegrationEvent.cs
    +-- Billing/
        +-- InvoiceCreatedIntegrationEvent.cs
```

---

## Integration Event Design

### Principles

1. **Self-contained** - Include all data consumers need (avoid callbacks)
2. **Immutable** - Events are facts, they don't change
3. **Past tense** - Name describes what happened: `PatientCreated`, not `CreatePatient`
4. **Versioned** - Plan for schema evolution from the start

### Anatomy of an Integration Event

```csharp
// Shared/IntegrationEvents/Scheduling/PatientCreatedIntegrationEvent.cs
namespace Shared.IntegrationEvents.Scheduling;

public record PatientCreatedIntegrationEvent : IntegrationEventBase
{
    /// <summary>
    /// The patient's ID in the Scheduling context
    /// </summary>
    public required Guid PatientId { get; init; }

    /// <summary>
    /// Patient's email (for notifications, billing contact)
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Full name for display purposes
    /// </summary>
    public required string FullName { get; init; }

    /// <summary>
    /// When the patient was created
    /// </summary>
    public required DateTime CreatedAt { get; init; }
}
```

### What Data to Include?

```
+---------------------------------------------------------------------+
|                    INCLUDE IN EVENT                                  |
+---------------------------------------------------------------------+
|  [x] IDs that other contexts need to correlate                      |
|  [x] Data that consumers need to do their job                       |
|  [x] Timestamps for ordering and debugging                          |
|  [x] Correlation IDs for distributed tracing                        |
+---------------------------------------------------------------------+

+---------------------------------------------------------------------+
|                    AVOID IN EVENT                                    |
+---------------------------------------------------------------------+
|  [ ] Internal implementation details                                 |
|  [ ] Sensitive data (passwords, tokens)                             |
|  [ ] Large blobs (files, images)                                    |
|  [ ] Data that changes frequently (cache it instead)                |
|  [ ] Circular references                                             |
+---------------------------------------------------------------------+
```

---

## Publishing Integration Events

### The Pattern: Command Handler Queues, UnitOfWork Publishes

The command handler queues integration events via `_uow.QueueIntegrationEvent()`. After `SaveChangesAsync()` succeeds, the UnitOfWork publishes all queued events to RabbitMQ.

```
Command Handler                       UnitOfWork
+------------------------+           +----------------------------+
| 1. Execute domain logic|           | 4. SaveChangesAsync()      |
| 2. Add to repository   |           | 5. If success, publish     |
| 3. Queue integration   |---------->|    all queued events       |
|    event               |           |    to RabbitMQ             |
+------------------------+           +----------------------------+
```

### Publishing from Command Handler

```csharp
// Scheduling.Application/Patients/Commands/CreatePatientCommandHandler.cs
using MediatR;
using Scheduling.Domain.Patients;
using Shared.IntegrationEvents.Scheduling;

namespace Scheduling.Application.Patients.Commands;

public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CreatePatientCommandHandler> _logger;

    public CreatePatientCommandHandler(
        IUnitOfWork uow,
        ILogger<CreatePatientCommandHandler> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
    {
        // 1. Create domain entity
        var patient = Patient.Create(
            cmd.FirstName,
            cmd.LastName,
            cmd.Email,
            cmd.DateOfBirth);

        // 2. Add to repository
        _uow.RepositoryFor<Patient>().Add(patient);

        // 3. Queue integration event for cross-BC communication
        _uow.QueueIntegrationEvent(new PatientCreatedIntegrationEvent
        {
            PatientId = patient.Id,
            Email = patient.Email,
            FullName = $"{patient.FirstName} {patient.LastName}",
            CreatedAt = DateTime.UtcNow
        });

        // 4. Save changes - publishes to RabbitMQ after successful save
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created patient {PatientId} and queued integration event",
            patient.Id);

        return patient.Id;
    }
}
```

### Flow Diagram

```
+-----------------------------------------------------------------------------+
|                         CREATE PATIENT FLOW                                  |
+-----------------------------------------------------------------------------+

     API Request
          |
          v
+------------------+
|   Controller     |
|   POST /patients |
+--------+---------+
         | MediatR.Send()
         v
+------------------+
| CreatePatient    |
| CommandHandler   |
+--------+---------+
         |
         v
+--------------------------------------------------+
| 1. Patient.Create()                              |
| 2. _uow.RepositoryFor<Patient>().Add(patient)    |
| 3. _uow.QueueIntegrationEvent(event)             |
+---------+----------------------------------------+
         |
         v
+------------------+
| UnitOfWork       |
| SaveChangesAsync |
+--------+---------+
         |
         +-- 1. Save to SQL Server
         |
         +-- 2. Publish queued events to RabbitMQ
                    |
                    v
         +---------------------------+
         |       RabbitMQ            |
         | PatientCreatedIntegration |
         |         Event             |
         +---------------------------+
                    |
           +--------+--------+
           v        v        v
        Billing   Notif.   Analytics
        Consumer  Consumer  Consumer
```

### Why This Pattern?

| Benefit | Description |
|---------|-------------|
| **Transactional safety** | Events only published if save succeeds |
| **Explicit intent** | Command handler decides what to publish |
| **Simple flow** | No intermediate event handlers |
| **Testable** | Easy to verify queued events in tests |
| **Single responsibility** | UnitOfWork handles the publishing mechanics |

---

## Consuming Integration Events

### Creating a Consumer

Consumers handle incoming messages from RabbitMQ:

```csharp
// Billing.Infrastructure/Consumers/PatientCreatedEventConsumer.cs
using MassTransit;
using Shared.IntegrationEvents.Scheduling;

namespace Billing.Infrastructure.Consumers;

public class PatientCreatedEventConsumer : IConsumer<PatientCreatedIntegrationEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<PatientCreatedEventConsumer> _logger;

    public PatientCreatedEventConsumer(
        IMediator mediator,
        ILogger<PatientCreatedEventConsumer> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
    {
        var evt = context.Message;

        _logger.LogInformation(
            "Received PatientCreatedIntegrationEvent {EventId} for patient {PatientId}",
            evt.EventId,
            evt.PatientId);

        // Handle in Billing BC - create a billing profile
        var command = new CreateBillingProfileCommand
        {
            ExternalPatientId = evt.PatientId,
            Email = evt.Email,
            FullName = evt.FullName
        };

        await _mediator.Send(command, context.CancellationToken);

        _logger.LogInformation(
            "Created billing profile for patient {PatientId}",
            evt.PatientId);
    }
}
```

### Registering Consumers

Consumers are registered in MassTransit configuration:

```csharp
// WebApi/Program.cs
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    // Auto-register all consumers from assembly
    configure.AddConsumers(typeof(Billing.Infrastructure.ServiceCollectionExtensions).Assembly);

    // Or register explicitly
    configure.AddConsumer<PatientCreatedEventConsumer>();
});
```

---

## Multiple Consumers

Multiple bounded contexts can consume the same event:

```csharp
// In Billing context
public class PatientCreatedConsumer_Billing : IConsumer<PatientCreatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
    {
        // Create billing profile
    }
}

// In Notification context
public class PatientCreatedConsumer_Notification : IConsumer<PatientCreatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
    {
        // Send welcome email
    }
}

// In Analytics context
public class PatientCreatedConsumer_Analytics : IConsumer<PatientCreatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
    {
        // Track signup metric
    }
}
```

Each consumer gets its own queue:

```
Exchange: PatientCreatedIntegrationEvent
    |
    +-- Queue: billing-patient-created ------> Billing Consumer
    |
    +-- Queue: notification-patient-created -> Notification Consumer
    |
    +-- Queue: analytics-patient-created ----> Analytics Consumer
```

---

## Consumer Definition (Advanced Configuration)

For fine-grained control, use consumer definitions:

```csharp
public class PatientCreatedConsumerDefinition
    : ConsumerDefinition<PatientCreatedEventConsumer>
{
    public PatientCreatedConsumerDefinition()
    {
        // Custom endpoint name
        EndpointName = "billing-patient-created";

        // Concurrent message limit
        ConcurrentMessageLimit = 10;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PatientCreatedEventConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Retry policy for this consumer
        endpointConfigurator.UseMessageRetry(r => r.Intervals(100, 500, 1000));

        // Circuit breaker
        endpointConfigurator.UseCircuitBreaker(cb =>
        {
            cb.TrackingPeriod = TimeSpan.FromMinutes(1);
            cb.TripThreshold = 15;
            cb.ActiveThreshold = 10;
            cb.ResetInterval = TimeSpan.FromMinutes(5);
        });
    }
}
```

---

## Request-Response (Optional)

Sometimes you need a response. Use `IRequestClient`:

```csharp
// Define request and response
public record GetPatientRequest
{
    public Guid PatientId { get; init; }
}

public record GetPatientResponse
{
    public Guid PatientId { get; init; }
    public string FullName { get; init; }
    public string Email { get; init; }
}

// Consumer responds
public class GetPatientRequestConsumer : IConsumer<GetPatientRequest>
{
    public async Task Consume(ConsumeContext<GetPatientRequest> context)
    {
        var patient = await _repository.GetById(context.Message.PatientId);

        await context.RespondAsync(new GetPatientResponse
        {
            PatientId = patient.Id,
            FullName = patient.FullName,
            Email = patient.Email
        });
    }
}

// Client makes request
public class SomeService
{
    private readonly IRequestClient<GetPatientRequest> _client;

    public async Task<GetPatientResponse> GetPatient(Guid patientId)
    {
        var response = await _client.GetResponse<GetPatientResponse>(
            new GetPatientRequest { PatientId = patientId });

        return response.Message;
    }
}
```

> Note: Prefer events over request-response. Request-response creates coupling.

---

## Common Integration Events for Healthcare Domain

```csharp
// Shared/IntegrationEvents/Scheduling/

// Patient events
public record PatientCreatedIntegrationEvent : IntegrationEventBase { ... }
public record PatientSuspendedIntegrationEvent : IntegrationEventBase { ... }
public record PatientReactivatedIntegrationEvent : IntegrationEventBase { ... }

// Appointment events
public record AppointmentScheduledIntegrationEvent : IntegrationEventBase
{
    public required Guid AppointmentId { get; init; }
    public required Guid PatientId { get; init; }
    public required Guid DoctorId { get; init; }
    public required DateTime ScheduledAt { get; init; }
    public required TimeSpan Duration { get; init; }
}

public record AppointmentCancelledIntegrationEvent : IntegrationEventBase
{
    public required Guid AppointmentId { get; init; }
    public required string CancellationReason { get; init; }
}

public record AppointmentCompletedIntegrationEvent : IntegrationEventBase
{
    public required Guid AppointmentId { get; init; }
    public required Guid PatientId { get; init; }
    public required DateTime CompletedAt { get; init; }
}

// Shared/IntegrationEvents/Billing/

// Billing events (from Billing context)
public record InvoiceCreatedIntegrationEvent : IntegrationEventBase
{
    public required Guid InvoiceId { get; init; }
    public required Guid PatientId { get; init; }
    public required decimal Amount { get; init; }
}

public record PaymentReceivedIntegrationEvent : IntegrationEventBase
{
    public required Guid PaymentId { get; init; }
    public required Guid InvoiceId { get; init; }
    public required decimal Amount { get; init; }
}
```

---

## Verification Checklist

- [ ] Integration events defined with `required` properties
- [ ] Events inherit from `IntegrationEventBase`
- [ ] Events located in `Shared/IntegrationEvents/{BoundedContext}/`
- [ ] Command handlers queue events via `_uow.QueueIntegrationEvent()`
- [ ] Consumers registered in MassTransit
- [ ] Each consumer has its own queue (verified in RabbitMQ UI)
- [ ] Consumer logs message receipt and processing
- [ ] Integration events visible in RabbitMQ Management UI

---

## Common Mistakes

| Mistake | Problem | Solution |
|---------|---------|----------|
| Publishing before save | Data might not persist | UnitOfWork publishes after SaveChangesAsync |
| Huge event payloads | Slow, memory issues | Include only necessary data |
| Synchronous request-response | Defeats async benefits | Use events, cache data locally |
| No event ID | Can't ensure idempotency | Always include unique EventId (IntegrationEventBase provides this) |
| Forgetting to queue event | Other BCs don't get notified | Review command handlers for cross-BC impact |

---

> Next: [04-idempotency-error-handling.md](./04-idempotency-error-handling.md) - Making handlers safe and resilient
