# Integration Events - Publishing and Consuming

## Overview

Integration events enable asynchronous communication between bounded contexts via RabbitMQ/MassTransit.

This document covers:
- Defining integration events
- Publishing via domain event handlers (the recommended pattern)
- Consuming in bounded contexts
- The publish-subscribe pattern

**Key Pattern:** Command handlers are kept clean (just entity operations + save). Domain event handlers listen for domain events via MediatR and queue integration events for cross-BC communication.

---

## Architecture

```
+-----------------------------------------------------------------------------+
|                         EVENT ARCHITECTURE                                   |
+-----------------------------------------------------------------------------+

Domain Events (Internal)                Integration Events (External)
+-----------------------------------+   +-----------------------------------+
| - Raised by entities               |   | - Published to RabbitMQ           |
| - Dispatched via MediatR           |   | - Cross bounded context           |
| - INotificationHandler<T>          |   | - Queued by domain event handlers |
| - Internal side effects            |-->| - Published AFTER commit          |
|   (logging, audit, etc.)           |   | - Discarded on rollback           |
| - Bridge to integration events     |   | - Durability, retry, DLQ          |
+-----------------------------------+   +-----------------------------------+

Flow:
  Entity -> Domain Event -> Domain Event Handler -> Queue Integration Event -> RabbitMQ
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

### The Pattern: Domain Event Handlers Queue Integration Events

Integration events are queued by **domain event handlers**, not directly in command handlers. This keeps command handlers clean and focused on the core domain operation.

**Flow:**
1. Entity raises domain event during state change
2. Command handler saves changes (triggers domain event dispatch)
3. Domain event handler receives the event and queues integration event
4. Integration events are published AFTER the transaction commits

```
Entity.Create() -> AddDomainEvent(PatientCreatedEvent)
    |
    v
SaveChangesAsync()
    |
    +-- 1. DispatchDomainEventsAsync() -> MediatR
    |       |
    |       +-- PatientCreatedEventHandler
    |               +-- Logs event
    |               +-- _uow.QueueIntegrationEvent(PatientCreatedIntegrationEvent)
    |
    +-- 2. _context.SaveChangesAsync() (DB save)
    |
    +-- 3. [after commit] -> RabbitMQ
            |
            +-- Billing context receives event
            +-- Notifications context receives event
```

**Why this matters:**
- Command handlers stay clean and focused
- Events are never published for rolled-back operations
- Consumers only see events for data that is actually persisted
- Provides transactional consistency between DB and message broker

### Step 1: Command Handler (Clean)

Command handlers focus only on entity operations and saving:

```csharp
// Scheduling.Application/Patients/Commands/CreatePatientCommandHandler.cs
using BuildingBlocks.Application.Interfaces;
using MediatR;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands;

public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    private readonly IUnitOfWork _uow;

    public CreatePatientCommandHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
    {
        // 1. Create domain entity (raises PatientCreatedEvent internally)
        var patient = Patient.Create(
            cmd.FirstName,
            cmd.LastName,
            cmd.Email,
            cmd.DateOfBirth);

        // 2. Add to repository
        _uow.RepositoryFor<Patient>().Add(patient);

        // 3. Save changes - triggers domain event dispatch
        await _uow.SaveChangesAsync(ct);

        return patient.Id;
    }
}
```

### Step 2: Domain Event Handler (Queues Integration Event)

Domain event handlers listen for domain events and queue integration events:

```csharp
// Scheduling.Application/Patients/EventHandlers/PatientCreatedEventHandler.cs
using BuildingBlocks.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Scheduling.Domain.Patients.Events;
using Shared.IntegrationEvents.Scheduling;

namespace Scheduling.Application.Patients.EventHandlers;

public class PatientCreatedEventHandler : INotificationHandler<PatientCreatedEvent>
{
    private readonly ILogger<PatientCreatedEventHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public PatientCreatedEventHandler(
        ILogger<PatientCreatedEventHandler> logger,
        IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public Task Handle(PatientCreatedEvent notification, CancellationToken cancellationToken)
    {
        // Internal side effect: logging
        _logger.LogInformation("Patient created: {PatientId}", notification.PatientId);

        // Queue integration event for cross-BC communication
        _unitOfWork.QueueIntegrationEvent(new PatientCreatedIntegrationEvent
        {
            PatientId = notification.PatientId,
            Email = notification.Email,
            FullName = $"{notification.FirstName} {notification.LastName}",
            DateOfBirth = notification.DateOfBirth
        });

        return Task.CompletedTask;
    }
}
```

### Flow Diagram (With TransactionBehavior)

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
| TransactionBehavior                                                         |
+--------+---------+
         |
         +-- BeginTransactionAsync()
         |
         v
+------------------+
| CreatePatient    |
| CommandHandler   |  <- Clean: just entity ops + save
+--------+---------+
         |
         v
+--------------------------------------------------+
| 1. Patient.Create()                              |
|       +-- AddDomainEvent(PatientCreatedEvent)    |
| 2. _uow.RepositoryFor<Patient>().Add(patient)    |
+---------+----------------------------------------+
         |
         v
+--------------------------------------------------+
| UnitOfWork.SaveChangesAsync()                    |
|   +-- DispatchDomainEventsAsync() [BEFORE save]  |
|   |       +-- PatientCreatedEventHandler         |
|   |               +-- Log event                  |
|   |               +-- QueueIntegrationEvent()    |
|   +-- _context.SaveChangesAsync() [in txn]       |
|   +-- [integration events queued, NOT published] |
+---------+----------------------------------------+
         |
         v
+--------------------------------------------------+
| TransactionBehavior.CloseTransactionAsync()      |
|   +-- CommitAsync()                              |
|   +-- PublishAndClearIntegrationEventsAsync()    |
+---------+----------------------------------------+
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

**Note:** If the transaction rolls back (due to an exception), queued integration events are discarded and nothing is published to RabbitMQ.

### Automatic Logging

The `EfCoreUnitOfWork` automatically logs every integration event when published:

```
info: BuildingBlocks.Infrastructure.EfCore.EfCoreUnitOfWork[0]
      Publishing integration event PatientCreatedIntegrationEvent with EventId 2046b276-be9f-4945-b3ee-315053a0e969
```

This provides visibility into all integration events leaving your bounded context without adding logging to each handler.

### IntegrationEventHandler Base Class

Handlers inherit from `IntegrationEventHandler<TEvent>` which provides automatic logging on the consumer side:

```csharp
// BuildingBlocks.Infrastructure.MassTransit/IntegrationEventHandler.cs
public abstract class IntegrationEventHandler<TEvent> : IConsumer<TEvent>
    where TEvent : class, IIntegrationEvent
{
    protected readonly ILogger Logger;

    public async Task Consume(ConsumeContext<TEvent> context)
    {
        Logger.LogInformation("Handling {EventType} with EventId {EventId}", ...);

        try
        {
            await HandleAsync(context.Message, context.CancellationToken);
            Logger.LogInformation("Handled {EventType} with EventId {EventId}", ...);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling {EventType} with EventId {EventId}", ...);
            throw;
        }
    }

    protected abstract Task HandleAsync(TEvent message, CancellationToken cancellationToken);
}
```

**Log output:**
```
info: Scheduling.Infrastructure.Consumers.PatientCreatedIntegrationEventHandler[0]
      Handling PatientCreatedIntegrationEvent with EventId 2046b276-be9f-4945-b3ee-315053a0e969
info: Scheduling.Infrastructure.Consumers.PatientCreatedIntegrationEventHandler[0]
      Processing patient 2046b276-be9f-4945-b3ee-315053a0e969 - John Doe (john.doe@example.com)
info: Scheduling.Infrastructure.Consumers.PatientCreatedIntegrationEventHandler[0]
      Handled PatientCreatedIntegrationEvent with EventId 2046b276-be9f-4945-b3ee-315053a0e969
```

### Why This Pattern?

| Benefit | Description |
|---------|-------------|
| **Clean command handlers** | Just entity operations + save, no side effect logic |
| **Transactional safety** | Events only published after transaction commits |
| **Rollback protection** | Failed transactions discard queued events automatically |
| **Single responsibility** | Domain event handlers handle side effects |
| **Easy to extend** | Add new handlers without modifying command handlers |
| **Testable** | Each handler can be tested in isolation |

---

## Consuming Integration Events

### Creating a Handler

Handlers inherit from `IntegrationEventHandler<TEvent>` which provides automatic logging:

```csharp
// Billing.Infrastructure/Consumers/PatientCreatedIntegrationEventHandler.cs
using BuildingBlocks.Infrastructure.MassTransit;
using IntegrationEvents.Scheduling;
using Microsoft.Extensions.Logging;

namespace Billing.Infrastructure.Consumers;

public class PatientCreatedIntegrationEventHandler
    : IntegrationEventHandler<PatientCreatedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public PatientCreatedIntegrationEventHandler(
        IMediator mediator,
        ILogger<PatientCreatedIntegrationEventHandler> logger) : base(logger)
    {
        _mediator = mediator;
    }

    protected override async Task HandleAsync(
        PatientCreatedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        // Business logic only - logging is automatic via base class
        var command = new CreateBillingProfileCommand
        {
            ExternalPatientId = message.PatientId,
            Email = message.Email,
            FullName = message.FullName
        };

        await _mediator.Send(command, cancellationToken);

        Logger.LogInformation(
            "Created billing profile for patient {PatientId}",
            message.PatientId);
    }
}
```

### Registering Handlers

Handlers are registered in MassTransit configuration:

```csharp
// WebApi/Program.cs
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    // Auto-register all consumers from assembly
    configure.AddConsumers(typeof(Billing.Infrastructure.ServiceCollectionExtensions).Assembly);

    // Or register explicitly
    configure.AddConsumer<PatientCreatedIntegrationEventHandler>();
});
```

---

## Multiple Handlers

Multiple bounded contexts can handle the same event:

```csharp
// In Billing context
public class PatientCreatedIntegrationEventHandler_Billing
    : IntegrationEventHandler<PatientCreatedIntegrationEvent>
{
    public PatientCreatedIntegrationEventHandler_Billing(
        ILogger<PatientCreatedIntegrationEventHandler_Billing> logger) : base(logger) { }

    protected override Task HandleAsync(
        PatientCreatedIntegrationEvent message, CancellationToken ct)
    {
        // Create billing profile (logging is automatic)
        return Task.CompletedTask;
    }
}

// In Notification context
public class PatientCreatedIntegrationEventHandler_Notification
    : IntegrationEventHandler<PatientCreatedIntegrationEvent>
{
    public PatientCreatedIntegrationEventHandler_Notification(
        ILogger<PatientCreatedIntegrationEventHandler_Notification> logger) : base(logger) { }

    protected override Task HandleAsync(
        PatientCreatedIntegrationEvent message, CancellationToken ct)
    {
        // Send welcome email (logging is automatic)
        return Task.CompletedTask;
    }
}

// In Analytics context
public class PatientCreatedIntegrationEventHandler_Analytics
    : IntegrationEventHandler<PatientCreatedIntegrationEvent>
{
    public PatientCreatedIntegrationEventHandler_Analytics(
        ILogger<PatientCreatedIntegrationEventHandler_Analytics> logger) : base(logger) { }

    protected override Task HandleAsync(
        PatientCreatedIntegrationEvent message, CancellationToken ct)
    {
        // Track signup metric (logging is automatic)
        return Task.CompletedTask;
    }
}
```

Each handler gets its own queue:

```
Exchange: PatientCreatedIntegrationEvent
    |
    +-- Queue: billing-patient-created ------> Billing Handler
    |
    +-- Queue: notification-patient-created -> Notification Handler
    |
    +-- Queue: analytics-patient-created ----> Analytics Handler
```

---

## Handler Definition (Advanced Configuration)

For fine-grained control, use handler definitions:

```csharp
public class PatientCreatedIntegrationEventHandlerDefinition
    : ConsumerDefinition<PatientCreatedIntegrationEventHandler>
{
    public PatientCreatedIntegrationEventHandlerDefinition()
    {
        // Custom endpoint name
        EndpointName = "billing-patient-created";

        // Concurrent message limit
        ConcurrentMessageLimit = 10;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PatientCreatedIntegrationEventHandler> consumerConfigurator,
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

// Handler responds
// Note: Request/response handlers use IConsumer<T> directly since they need
// ConsumeContext.RespondAsync() - IntegrationEventHandler<T> is for fire-and-forget events
public class GetPatientRequestHandler : IConsumer<GetPatientRequest>
{
    private readonly ILogger<GetPatientRequestHandler> _logger;

    public GetPatientRequestHandler(ILogger<GetPatientRequestHandler> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<GetPatientRequest> context)
    {
        _logger.LogInformation(
            "Processing request for patient {PatientId}",
            context.Message.PatientId);

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
- [ ] Domain event handlers queue integration events via `_uow.QueueIntegrationEvent()`
- [ ] Domain event handlers implement `INotificationHandler<TDomainEvent>`
- [ ] Command handlers are clean (just entity ops + save)
- [ ] Handlers registered in MassTransit
- [ ] Each handler has its own queue (verified in RabbitMQ UI)
- [ ] Handler logs message receipt and processing
- [ ] Integration events visible in RabbitMQ Management UI

---

## Additional Example: Appointment Domain Event Handler

```csharp
// Scheduling.Application/Appointments/EventHandlers/AppointmentScheduledEventHandler.cs
using BuildingBlocks.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Scheduling.Domain.Appointments.Events;
using Shared.IntegrationEvents.Scheduling;

namespace Scheduling.Application.Appointments.EventHandlers;

public class AppointmentScheduledEventHandler : INotificationHandler<AppointmentScheduledEvent>
{
    private readonly ILogger<AppointmentScheduledEventHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public AppointmentScheduledEventHandler(
        ILogger<AppointmentScheduledEventHandler> logger,
        IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public Task Handle(AppointmentScheduledEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Appointment scheduled: {AppointmentId} for patient {PatientId}",
            notification.AppointmentId,
            notification.PatientId);

        _unitOfWork.QueueIntegrationEvent(new AppointmentScheduledIntegrationEvent
        {
            AppointmentId = notification.AppointmentId,
            PatientId = notification.PatientId,
            DoctorId = notification.DoctorId,
            ScheduledAt = notification.ScheduledAt,
            Duration = notification.Duration
        });

        return Task.CompletedTask;
    }
}
```

---

## Common Mistakes

| Mistake | Problem | Solution |
|---------|---------|----------|
| Publishing before commit | Data might not persist (txn could rollback) | TransactionBehavior publishes after CommitAsync |
| Huge event payloads | Slow, memory issues | Include only necessary data |
| Synchronous request-response | Defeats async benefits | Use events, cache data locally |
| No event ID | Can't ensure idempotency | Always include unique EventId (IntegrationEventBase provides this) |
| Queueing in command handler | Mixes concerns, harder to maintain | Use domain event handlers to queue integration events |
| Publishing in non-transactional flow | Events may publish before data is committed | Use Command<T> base type to ensure TransactionBehavior wraps the handler |
| Missing domain event handler | Other BCs don't get notified | Create domain event handler that queues integration event |

---

> Next: [04-idempotency-error-handling.md](./04-idempotency-error-handling.md) - Making handlers safe and resilient
