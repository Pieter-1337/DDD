# Integration Events - Publishing and Consuming

## Overview

Integration events are how bounded contexts communicate asynchronously. This document covers:
- Defining integration events
- Publishing from domain event handlers
- Consuming in other contexts
- The publish-subscribe pattern

---

## Integration Event Design

### Principles

1. **Self-contained** - Include all data consumers need (avoid callbacks)
2. **Immutable** - Events are facts, they don't change
3. **Past tense** - Name describes what happened: `PatientCreated`, not `CreatePatient`
4. **Versioned** - Plan for schema evolution from the start

### Anatomy of an Integration Event

```csharp
// Scheduling.Application/IntegrationEvents/PatientCreatedIntegrationEvent.cs
namespace Scheduling.Application.IntegrationEvents;

public record PatientCreatedIntegrationEvent : IntegrationEvent
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
┌─────────────────────────────────────────────────────────────────────┐
│                    INCLUDE IN EVENT                                  │
├─────────────────────────────────────────────────────────────────────┤
│  ✅ IDs that other contexts need to correlate                       │
│  ✅ Data that consumers need to do their job                        │
│  ✅ Timestamps for ordering and debugging                           │
│  ✅ Correlation IDs for distributed tracing                         │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                    AVOID IN EVENT                                    │
├─────────────────────────────────────────────────────────────────────┤
│  ❌ Internal implementation details                                  │
│  ❌ Sensitive data (passwords, tokens)                              │
│  ❌ Large blobs (files, images)                                     │
│  ❌ Data that changes frequently (cache it instead)                 │
│  ❌ Circular references                                              │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Publishing Integration Events

### The Pattern: Domain Event → Integration Event

```
Domain Event (in-memory)          Integration Event (broker)
┌────────────────────┐            ┌────────────────────────────┐
│ PatientCreatedEvent│──handler──>│ PatientCreatedIntegration  │
│ (internal detail)  │            │ Event (public contract)    │
└────────────────────┘            └────────────────────────────┘
```

### Domain Event Handler Publishes Integration Event

```csharp
// Scheduling.Application/Patients/EventHandlers/PatientCreatedEventHandler.cs
using MassTransit;
using MediatR;
using Scheduling.Domain.Patients.Events;
using Scheduling.Application.IntegrationEvents;

namespace Scheduling.Application.Patients.EventHandlers;

public class PatientCreatedDomainEventHandler : INotificationHandler<PatientCreatedEvent>
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<PatientCreatedDomainEventHandler> _logger;

    public PatientCreatedDomainEventHandler(
        IPublishEndpoint publishEndpoint,
        ILogger<PatientCreatedDomainEventHandler> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Handle(PatientCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Publishing PatientCreatedIntegrationEvent for patient {PatientId}",
            notification.PatientId);

        var integrationEvent = new PatientCreatedIntegrationEvent
        {
            PatientId = notification.PatientId,
            Email = notification.Email,
            FullName = $"{notification.FirstName} {notification.LastName}",
            CreatedAt = DateTime.UtcNow
        };

        await _publishEndpoint.Publish(integrationEvent, cancellationToken);

        _logger.LogInformation(
            "Published PatientCreatedIntegrationEvent {EventId}",
            integrationEvent.EventId);
    }
}
```

### Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         CREATE PATIENT FLOW                                  │
└─────────────────────────────────────────────────────────────────────────────┘

     API Request
          │
          ▼
┌──────────────────┐
│   Controller     │
│   POST /patients │
└────────┬─────────┘
         │ MediatR.Send()
         ▼
┌──────────────────┐
│ CreatePatient    │
│ CommandHandler   │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ Patient.Create() │  ← Raises PatientCreatedEvent (domain event)
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ UnitOfWork       │
│ SaveChangesAsync │  ← EF Interceptor dispatches domain events
└────────┬─────────┘
         │ MediatR.Publish()
         ▼
┌───────────────────────────┐
│ PatientCreatedEventHandler│  ← Domain event handler
│ (INotificationHandler)    │
└────────────┬──────────────┘
             │ _publishEndpoint.Publish()
             ▼
┌───────────────────────────┐
│       RabbitMQ            │
│ PatientCreatedIntegration │
│         Event             │
└───────────────────────────┘
             │
    ┌────────┼────────┐
    ▼        ▼        ▼
Billing   Notif.   Analytics
Consumer  Consumer  Consumer
```

---

## Consuming Integration Events

### Creating a Consumer

Consumers handle incoming messages:

```csharp
// Billing.Application/Consumers/PatientCreatedIntegrationEventConsumer.cs
using MassTransit;
using Scheduling.Application.IntegrationEvents;

namespace Billing.Application.Consumers;

public class PatientCreatedIntegrationEventConsumer
    : IConsumer<PatientCreatedIntegrationEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<PatientCreatedIntegrationEventConsumer> _logger;

    public PatientCreatedIntegrationEventConsumer(
        IMediator mediator,
        ILogger<PatientCreatedIntegrationEventConsumer> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received PatientCreatedIntegrationEvent {EventId} for patient {PatientId}",
            message.EventId,
            message.PatientId);

        // Create a billing profile for this patient
        var command = new CreateBillingProfileCommand
        {
            ExternalPatientId = message.PatientId,
            Email = message.Email,
            FullName = message.FullName
        };

        await _mediator.Send(command, context.CancellationToken);

        _logger.LogInformation(
            "Created billing profile for patient {PatientId}",
            message.PatientId);
    }
}
```

### Registering Consumers

Consumers are registered in MassTransit configuration:

```csharp
// Billing.Infrastructure/Messaging/MassTransitConfiguration.cs
services.AddMassTransit(x =>
{
    // Auto-register all consumers from assembly
    x.AddConsumers(typeof(PatientCreatedIntegrationEventConsumer).Assembly);

    // Or register explicitly
    x.AddConsumer<PatientCreatedIntegrationEventConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");
        cfg.ConfigureEndpoints(context);
    });
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
    │
    ├── Queue: billing-patient-created ──> Billing Consumer
    │
    ├── Queue: notification-patient-created ──> Notification Consumer
    │
    └── Queue: analytics-patient-created ──> Analytics Consumer
```

---

## Consumer Definition (Advanced Configuration)

For fine-grained control, use consumer definitions:

```csharp
public class PatientCreatedConsumerDefinition
    : ConsumerDefinition<PatientCreatedIntegrationEventConsumer>
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
        IConsumerConfigurator<PatientCreatedIntegrationEventConsumer> consumerConfigurator,
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
// Patient events
public record PatientCreatedIntegrationEvent : IntegrationEvent { ... }
public record PatientSuspendedIntegrationEvent : IntegrationEvent { ... }
public record PatientReactivatedIntegrationEvent : IntegrationEvent { ... }

// Appointment events
public record AppointmentScheduledIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required Guid PatientId { get; init; }
    public required Guid DoctorId { get; init; }
    public required DateTime ScheduledAt { get; init; }
    public required TimeSpan Duration { get; init; }
}

public record AppointmentCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required string CancellationReason { get; init; }
}

public record AppointmentCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required Guid PatientId { get; init; }
    public required DateTime CompletedAt { get; init; }
}

// Billing events (from Billing context)
public record InvoiceCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid InvoiceId { get; init; }
    public required Guid PatientId { get; init; }
    public required decimal Amount { get; init; }
}

public record PaymentReceivedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid InvoiceId { get; init; }
    public required decimal Amount { get; init; }
}
```

---

## Verification Checklist

- [ ] Integration events defined with `required` properties
- [ ] Events inherit from `IntegrationEvent` base class
- [ ] Domain event handlers publish integration events
- [ ] Consumers registered in MassTransit
- [ ] Each consumer has its own queue (verified in RabbitMQ UI)
- [ ] Consumer logs message receipt and processing
- [ ] Integration events visible in RabbitMQ Management UI

---

## Common Mistakes

| Mistake | Problem | Solution |
|---------|---------|----------|
| Publishing inside aggregate | Couples domain to infrastructure | Publish from domain event handler |
| Huge event payloads | Slow, memory issues | Include only necessary data |
| Synchronous request-response | Defeats async benefits | Use events, cache data locally |
| No event ID | Can't ensure idempotency | Always include unique EventId |
| Exposing internal IDs | Tight coupling | Use public correlation IDs |

---

> Next: [04-idempotency-error-handling.md](./04-idempotency-error-handling.md) - Making handlers safe and resilient
