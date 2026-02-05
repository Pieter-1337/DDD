# Event-Driven Architecture Overview

## What Is Event-Driven Architecture?

Event-Driven Architecture (EDA) is a pattern where systems communicate through **events** - notifications that something significant happened.

```
Traditional (Synchronous):
┌─────────┐   HTTP/RPC   ┌─────────┐
│ Service │ ───────────> │ Service │   Tight coupling, blocking
│    A    │ <─────────── │    B    │
└─────────┘              └─────────┘

Event-Driven (Asynchronous):
┌─────────┐              ┌─────────────┐              ┌─────────┐
│ Service │ ──publish──> │ Message Bus │ ──consume──> │ Service │
│    A    │              │ (RabbitMQ)  │              │    B    │
└─────────┘              └─────────────┘              └─────────┘
                               │
                               └──consume──> ┌─────────┐
                                             │ Service │
                                             │    C    │
                                             └─────────┘
```

---

## Messages vs Events

Two types of communication through a message broker:

```
MESSAGE - "Do something" (point-to-point)
──────────────────────────────────────────

     Producer
         │
         │ Send(SendWelcomeEmail)
         ▼
    ┌─────────┐
    │  Queue  │ ──────> One specific receiver
    └─────────┘

    - Sender knows the destination
    - Exactly ONE handler processes it
    - Examples: SendEmail, ProcessPayment


EVENT - "Something happened" (publish/subscribe)
─────────────────────────────────────────────────

     Producer
         │
         │ Publish(PatientCreated)
         ▼
    ┌─────────┐
    │Exchange │
    └────┬────┘
         │
    ┌────┴────┬────────┐  Only subscribers receive it
    ▼         ▼        ▼
  Queue1    Queue2   Queue3
    │         │        │
    ▼         ▼        ▼
 Billing   Notif   Analytics

    - Publisher doesn't know who's listening
    - Zero to many subscribers
    - Subscribers must opt-in (bind to exchange)
    - Examples: PatientCreated, OrderPlaced
```

| Aspect | Message | Event |
|--------|---------|-------|
| **Intent** | "Do this" | "This happened" |
| **Naming** | Imperative: `SendEmail` | Past tense: `EmailSent` |
| **Routing** | Point-to-point (one queue) | Broadcast (all subscribed queues) |
| **Receivers** | Exactly one | Zero to many |
| **Coupling** | Tighter (sender knows target) | Loose (sender doesn't care) |

### In MassTransit

```csharp
// MESSAGE - direct to one queue
var endpoint = await _bus.GetSendEndpoint(new Uri("queue:email-service"));
await endpoint.Send(new SendWelcomeEmail { ... });

// EVENT - broadcast to all subscribers
await _publishEndpoint.Publish(new PatientCreatedEvent { ... });
```

### Important: Events Require Subscription

Events don't magically appear at all services. Consumers must subscribe:

```csharp
// This creates a queue AND binds it to the event's exchange
x.AddConsumer<PatientCreatedEventConsumer>();
```

No subscription = event is published but nobody receives it.

### One Handler but "Something Happened"?

What if you have only one subscriber, but it's semantically a fact/notification?

**Use the intent, not the handler count, to decide:**

```
"PatientCreated" - only Billing subscribes today

As Event (Publish):                    As Message (Send):
─────────────────────                  ────────────────────
"A patient was created,                "Billing, set up
 whoever needs to know"                 this patient"

     ┌─────────┐                           ┌─────────┐
     │Exchange │ ──> Billing               │  Queue  │ ──> Billing
     └─────────┘                           └─────────┘
           │
           └──> Easy to add more           Need to change
                subscribers later          publisher to add more
```

| Question | Event | Message |
|----------|-------|---------|
| Is it a notification/fact? | Yes | No, it's an instruction |
| Might add subscribers later? | Easy | Requires publisher change |
| Should publisher be decoupled? | Yes | Doesn't matter |

**Rule of thumb:** If it "happened" → use Event, even with one subscriber.

```csharp
// These describe the same outcome, but different intent:

"PatientCreated"        → Event (notifying)
"CreateBillingProfile"  → Message (instructing)
```

When in doubt, default to events for looser coupling.

### In This Project

- **Domain Events** → In-memory within a context (MediatR)
- **Integration Events** → Cross-context via broker (MassTransit Publish)
- **Messages** → Direct instructions to specific service (MassTransit Send)

For cross-context communication, prefer **events** for loose coupling.

---

## Why Event-Driven?

### Problems with Synchronous Communication

```csharp
// Scheduling service creates appointment
public async Task<Guid> CreateAppointment(CreateAppointmentCommand command)
{
    var appointment = Appointment.Create(...);
    await _repository.Add(appointment);

    // Direct calls to other services - tight coupling!
    await _billingService.CreateInvoice(appointment.Id);      // What if Billing is down?
    await _notificationService.SendConfirmation(appointment); // What if this times out?
    await _analyticsService.TrackAppointment(appointment);    // Do we really need to wait?

    return appointment.Id;
}
```

**Problems:**
- **Coupling** - Scheduling knows about Billing, Notifications, Analytics
- **Availability** - If any service is down, the whole operation fails
- **Latency** - Must wait for all services to respond
- **Scalability** - Can't scale services independently

### With Event-Driven

```csharp
// Scheduling service creates appointment
public async Task<Guid> CreateAppointment(CreateAppointmentCommand command)
{
    var appointment = Appointment.Create(...);
    await _repository.Add(appointment);

    // Publish event - fire and forget
    await _messagebus.Publish(new AppointmentCreatedIntegrationEvent
    {
        AppointmentId = appointment.Id,
        PatientId = appointment.PatientId,
        DoctorId = appointment.DoctorId,
        ScheduledAt = appointment.ScheduledAt
    });

    return appointment.Id;
}
```

**Benefits:**
- **Decoupled** - Scheduling doesn't know who's listening
- **Resilient** - Other services can be temporarily down
- **Fast** - Returns immediately after publishing
- **Scalable** - Add more consumers without changing publisher

---

## Domain Events vs Integration Events

This is a critical distinction in DDD + Event-Driven systems:

| Aspect | Domain Events | Integration Events |
|--------|---------------|-------------------|
| **Scope** | Within a bounded context | Between bounded contexts |
| **Transport** | In-memory (MediatR) | Message broker (RabbitMQ) |
| **Timing** | Same transaction | After transaction commits |
| **Failure** | Fails the operation | Retried independently |
| **Schema** | Can change freely | Must be versioned |

### Domain Events

Domain events represent **something that happened within your domain**:

```csharp
// Domain event - internal to Scheduling context
public record PatientCreatedEvent(Guid PatientId, string Email) : IDomainEvent;

// Raised inside the aggregate
public class Patient : AggregateRoot
{
    public static Patient Create(string firstName, string lastName, string email, ...)
    {
        var patient = new Patient { ... };

        // Domain event raised
        patient.RaiseDomainEvent(new PatientCreatedEvent(patient.Id, email));

        return patient;
    }
}

// Handled in-memory, same transaction
public class PatientCreatedEventHandler : INotificationHandler<PatientCreatedEvent>
{
    public async Task Handle(PatientCreatedEvent notification, CancellationToken ct)
    {
        // Maybe update a read model, trigger side effects within same context
        _logger.LogInformation("Patient {Id} created", notification.PatientId);
    }
}
```

### Why Domain Events? Why Not Just Call Directly?

You could skip domain events and call things directly from the command handler:

```csharp
// Direct approach - no domain events
public async Task Handle(CreatePatientCommand command)
{
    var patient = Patient.Create(...);
    _repository.Add(patient);
    await _unitOfWork.SaveChanges();

    // Directly call side effects
    await _readModelUpdater.Update(patient);
    await _publishEndpoint.Publish(new PatientCreatedIntegrationEvent(...));
    await _auditLogger.Log("Patient created", patient.Id);
}
```

This works, but the handler now knows about all side effects. Domain events decouple this:

```
WITHOUT domain events:              WITH domain events:
──────────────────────              ───────────────────

CommandHandler                      CommandHandler
      │                                   │
      ├─> Save                            └─> Save (event raised internally)
      ├─> Update read model                        │
      ├─> Publish integration event                ▼
      └─> Audit log                       PatientCreatedEvent
                                                   │
Handler knows everything            ┌──────────────┼──────────────┐
                                    ▼              ▼              ▼
                                ReadModel    Integration      Audit
                                Updater      Publisher         Logger

                                Handlers react independently
```

**When to use domain events:**

| Scenario | Direct calls | Domain events |
|----------|--------------|---------------|
| One simple side effect | Fine | Overkill |
| Multiple side effects | Handler bloats | Clean separation |
| Side effects change often | Modify handler each time | Add/remove handlers |
| Want Open/Closed principle | No | Yes |

**When direct calls are fine:**
- Simple context with few side effects
- You prefer explicit flow over indirection
- Just starting out (add events later when needed)

Domain events add indirection but enable adding new reactions without modifying the command handler.

### Integration Events

Integration events represent **facts that other bounded contexts need to know**:

```csharp
// Integration event - crosses context boundaries
public record PatientCreatedIntegrationEvent : IntegrationEvent
{
    public Guid PatientId { get; init; }
    public string Email { get; init; }
    public string FullName { get; init; }
    public DateTime CreatedAt { get; init; }
}

// Published AFTER transaction commits
public class PatientCreatedEventHandler : INotificationHandler<PatientCreatedEvent>
{
    private readonly IMessageBus _messageBus;

    public async Task Handle(PatientCreatedEvent domainEvent, CancellationToken ct)
    {
        // Transform domain event to integration event
        var integrationEvent = new PatientCreatedIntegrationEvent
        {
            PatientId = domainEvent.PatientId,
            Email = domainEvent.Email,
            FullName = $"{domainEvent.FirstName} {domainEvent.LastName}",
            CreatedAt = DateTime.UtcNow
        };

        await _messageBus.Publish(integrationEvent, ct);
    }
}

// Consumed by Billing context (different service)
public class PatientCreatedIntegrationEventConsumer : IConsumer<PatientCreatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
    {
        // Create patient record in Billing context
        var command = new CreateBillingPatientCommand(
            context.Message.PatientId,
            context.Message.Email
        );
        await _mediator.Send(command);
    }
}
```

---

## Intra-Domain Messaging: Asynchronous Processing Within a Single Context

While domain events handle in-memory reactions and integration events cross context boundaries, there's a third pattern: **using messages within a single bounded context for asynchronous processing**.

### The Problem: Not Everything Should Be Synchronous

CQRS encourages commands (synchronous write operations) and queries (synchronous reads). But sometimes, even within a single domain, you need asynchronous processing:

```csharp
// Synchronous command - blocks until all 100,000 records are processed
public class MigratePatientRecordsCommand : IRequest
{
    public List<Guid> PatientIds { get; init; } // 100,000 IDs
}

public class MigratePatientRecordsCommandHandler : IRequestHandler<MigratePatientRecordsCommand>
{
    public async Task Handle(MigratePatientRecordsCommand command, CancellationToken ct)
    {
        // This will take a LONG time and block the caller
        foreach (var patientId in command.PatientIds) // 100,000 iterations
        {
            var patient = await _repository.GetById(patientId);
            patient.Migrate();
            await _repository.Update(patient);
        }

        await _unitOfWork.SaveChanges();
        // Caller is blocked for minutes/hours
    }
}
```

**Problems:**
- **Long-running** - Caller waits for all 100,000 records to process
- **No parallelism** - Processes one record at a time
- **Poor error handling** - If record 50,000 fails, you lose all progress
- **Resource contention** - Holds database connections for the entire duration
- **No retry logic** - Can't retry individual failures

### The Solution: Intra-Domain Messages

Instead of processing everything in one synchronous command, publish a message for each unit of work:

```csharp
// Step 1: Command initiates migration
public class InitiatePatientMigrationCommand : IRequest
{
    public List<Guid> PatientIds { get; init; }
}

public class InitiatePatientMigrationCommandHandler : IRequestHandler<InitiatePatientMigrationCommand>
{
    private readonly IPublishEndpoint _publishEndpoint;

    public async Task Handle(InitiatePatientMigrationCommand command, CancellationToken ct)
    {
        // Quickly publish a message for each patient
        foreach (var patientId in command.PatientIds)
        {
            await _publishEndpoint.Publish(new MigratePatientMessage
            {
                PatientId = patientId
            }, ct);
        }

        // Returns immediately - actual work happens asynchronously
        _logger.LogInformation("Queued {Count} patients for migration", command.PatientIds.Count);
    }
}

// Step 2: Consumer processes each patient independently
public class MigratePatientMessageConsumer : IConsumer<MigratePatientMessage>
{
    public async Task Consume(ConsumeContext<MigratePatientMessage> context)
    {
        var patientId = context.Message.PatientId;

        try
        {
            var patient = await _repository.GetById(patientId);
            patient.Migrate();
            await _repository.Update(patient);
            await _unitOfWork.SaveChanges();

            _logger.LogInformation("Migrated patient {PatientId}", patientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate patient {PatientId}", patientId);
            throw; // Retry handled by MassTransit
        }
    }
}
```

**Benefits:**
- **Non-blocking** - Caller returns immediately after queuing work
- **Parallel processing** - Multiple consumers process records simultaneously
- **Granular error handling** - Each record can retry independently
- **Progress tracking** - See how many succeeded/failed in real-time
- **Scalability** - Add more consumers to process faster
- **Idempotency** - Can safely retry individual records

### When to Use Intra-Domain Messages

| Scenario | Use Synchronous Command | Use Intra-Domain Message |
|----------|------------------------|--------------------------|
| Create a single patient | Yes | No (overkill) |
| Import 10,000 patients from CSV | No | Yes |
| Update a patient record | Yes | No |
| Migrate all patient records to new schema | No | Yes |
| Process a single invoice | Yes | No |
| Generate 50,000 monthly invoices | No | Yes |
| Send one email | Yes (or use message) | Either works |
| Send bulk emails to all patients | No | Yes |

**Use intra-domain messages when:**
- Processing many records (batch operations, migrations)
- Work can be parallelized
- Caller shouldn't wait for completion
- Individual items can fail independently
- You want retry logic per item

**Use synchronous commands when:**
- Single record operations
- Caller needs immediate result
- Work is fast and non-blocking
- Transactional consistency required across items

### Pattern: Queue Work, Process Asynchronously

```
┌──────────────────────────────────────────────────────────────┐
│                 SAME BOUNDED CONTEXT                          │
│                                                              │
│  User/API                                                    │
│     │                                                        │
│     ▼                                                        │
│  ┌─────────────────────────┐                                │
│  │ InitiateMigrationCommand│                                │
│  │      Handler            │                                │
│  └──────────┬──────────────┘                                │
│             │                                                │
│             │ Publish 100,000 messages                       │
│             ▼                                                │
│      ┌─────────────┐                                         │
│      │  RabbitMQ   │                                         │
│      │   Queue     │                                         │
│      └──────┬──────┘                                         │
│             │                                                │
│     ┌───────┴──────┬─────────┬──────────┐                   │
│     ▼              ▼         ▼          ▼                   │
│  Consumer 1    Consumer 2  Consumer 3  Consumer 4           │
│  (processes    (processes  (processes  (processes           │
│   patient      patient     patient     patient              │
│   in parallel) in parallel)in parallel)in parallel)         │
│                                                              │
│  All within Scheduling context, no cross-context calls      │
└──────────────────────────────────────────────────────────────┘
```

### Message vs Event for Intra-Domain Work

For intra-domain asynchronous processing, you often use **messages** (point-to-point) rather than events:

```csharp
// MESSAGE - specific instruction to process this patient
public record MigratePatientMessage
{
    public Guid PatientId { get; init; }
}

// One consumer handles the work
public class MigratePatientMessageConsumer : IConsumer<MigratePatientMessage> { ... }
```

You could also use events if multiple handlers need to react:

```csharp
// EVENT - notify that migration was initiated for this patient
public record PatientMigrationInitiatedEvent
{
    public Guid PatientId { get; init; }
}

// Multiple consumers could react
public class MigrateDataConsumer : IConsumer<PatientMigrationInitiatedEvent> { ... }
public class UpdateCacheConsumer : IConsumer<PatientMigrationInitiatedEvent> { ... }
```

**Choose based on intent:**
- One specific task → Message
- Multiple reactions → Event

### Example: Real-World Use Cases

**Batch Invoice Generation:**
```csharp
// Initiate: Publish a message per patient
await _publishEndpoint.Publish(new GenerateMonthlyInvoiceMessage
{
    PatientId = patient.Id,
    Month = DateTime.UtcNow.Month
});

// Process: Consumer handles each invoice
public class GenerateMonthlyInvoiceConsumer : IConsumer<GenerateMonthlyInvoiceMessage>
{
    public async Task Consume(ConsumeContext<GenerateMonthlyInvoiceMessage> context)
    {
        // Generate invoice for this patient
        // If it fails, only this patient's invoice retries
    }
}
```

**Data Migration:**
```csharp
// Initiate: Queue all records for migration
foreach (var id in recordIds)
{
    await _publishEndpoint.Publish(new MigrateRecordMessage { RecordId = id });
}

// Process: Multiple consumers process in parallel
public class MigrateRecordConsumer : IConsumer<MigrateRecordMessage>
{
    public async Task Consume(ConsumeContext<MigrateRecordMessage> context)
    {
        // Migrate this specific record
        // Retries automatically if it fails
    }
}
```

**Bulk Email Campaign:**
```csharp
// Initiate: One message per recipient
foreach (var patient in patients)
{
    await _publishEndpoint.Publish(new SendCampaignEmailMessage
    {
        PatientId = patient.Id,
        CampaignId = campaignId
    });
}

// Process: Email service consumes messages
public class SendCampaignEmailConsumer : IConsumer<SendCampaignEmailMessage>
{
    public async Task Consume(ConsumeContext<SendCampaignEmailMessage> context)
    {
        // Send email to this patient
        // Track delivery status per patient
    }
}
```

### Comparison: The Three Event/Message Types

| Aspect | Domain Events | Integration Events | Intra-Domain Messages |
|--------|---------------|-------------------|----------------------|
| **Scope** | Within context | Between contexts | Within context |
| **Transport** | In-memory (MediatR) | Message broker | Message broker |
| **Purpose** | React to domain changes | Notify other contexts | Asynchronous processing |
| **Timing** | Same transaction | After commit | After commit |
| **Example** | PatientCreated (domain) | PatientCreated (integration) | MigratePatientRecord |
| **When to use** | Side effects in domain | Cross-context facts | Batch work, long-running tasks |

### Key Takeaway

Not everything in a bounded context needs to be a synchronous CQRS command. When you have:
- Batch processing
- Long-running operations
- Work that can be parallelized
- Operations where individual failures should be retried

Consider using **intra-domain messages** to queue work and process it asynchronously, even though both the publisher and consumer are in the same bounded context.

This gives you the benefits of asynchronous processing (scalability, retry logic, non-blocking) without crossing context boundaries.

---

## The Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           SCHEDULING CONTEXT                                 │
│                                                                             │
│  ┌──────────────┐    ┌─────────────────┐    ┌──────────────────────────┐   │
│  │   Command    │───>│    Aggregate    │───>│   Domain Event           │   │
│  │   Handler    │    │ Patient.Create()│    │ PatientCreatedEvent      │   │
│  └──────────────┘    └─────────────────┘    └──────────┬───────────────┘   │
│                                                        │                    │
│                                                        │ MediatR            │
│                                                        ▼                    │
│                             ┌───────────────────────────────────────────┐   │
│                             │ Domain Event Handler                      │   │
│                             │ - Update read models (same context)       │   │
│                             │ - Publish Integration Event               │   │
│                             └───────────────────────┬───────────────────┘   │
│                                                     │                       │
└─────────────────────────────────────────────────────┼───────────────────────┘
                                                      │ MassTransit
                                                      ▼
                                          ┌───────────────────────┐
                                          │      RabbitMQ         │
                                          │                       │
                                          │ PatientCreated        │
                                          │ IntegrationEvent      │
                                          └───────────┬───────────┘
                                                      │
                    ┌─────────────────────────────────┼─────────────────────┐
                    │                                 │                     │
                    ▼                                 ▼                     ▼
┌──────────────────────────────┐  ┌──────────────────────────────┐  ┌─────────────────┐
│      BILLING CONTEXT         │  │    NOTIFICATION CONTEXT      │  │   ANALYTICS     │
│                              │  │                              │  │                 │
│  Create billing profile      │  │  Send welcome email          │  │  Track signup   │
│  for new patient             │  │                              │  │                 │
└──────────────────────────────┘  └──────────────────────────────┘  └─────────────────┘
```

---

## Message Delivery Guarantees

Understanding delivery semantics is crucial:

| Guarantee | Description | Use Case |
|-----------|-------------|----------|
| **At-most-once** | Message may be lost, never duplicated | Metrics, logs |
| **At-least-once** | Message never lost, may be duplicated | Most business events |
| **Exactly-once** | Message never lost, never duplicated | Financial transactions (hard to achieve) |

**RabbitMQ + MassTransit provide at-least-once** by default. This means:
- Messages are persisted and acknowledged
- If consumer fails, message is redelivered
- **Your handlers must be idempotent** (safe to run multiple times)

---

## Error Handling Strategy

```
Message Published
       │
       ▼
┌─────────────────┐
│    Consumer     │
│   Attempts      │
│   Processing    │
└────────┬────────┘
         │
    ┌────┴────┐
    │ Success │────> Message acknowledged, removed from queue
    └────┬────┘
         │
    ┌────┴────┐
    │ Failure │────> Retry with exponential backoff
    └────┬────┘
         │
         ▼
┌─────────────────┐
│  Retry Limit    │
│   Exceeded      │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│   Dead Letter   │
│     Queue       │────> Manual inspection, reprocessing
└─────────────────┘
```

### DLQ vs Lost Messages

| Scenario | What Happens | DLQ Helps? |
|----------|--------------|------------|
| Message published → consumer fails | Message goes to DLQ | Yes |
| Message published → consumer throws | Message goes to DLQ | Yes |
| SaveChanges succeeds → publish fails (broker down) | Message never reaches broker | No |

The last scenario is the "dual write" problem. Solutions:
1. **Outbox Pattern** - Save event to DB in same transaction, background worker publishes
2. **Transactional Outbox** - Use database as the queue
3. **Accept eventual consistency** - For learning, start simple

---

## What You'll Build in This Phase

1. **RabbitMQ + MassTransit Setup** - Infrastructure for messaging
2. **Integration Events** - Define events that cross boundaries
3. **Consumers** - Handle events from other contexts
4. **Idempotent Handlers** - Safe message processing
5. **Error Handling** - Retries and dead letter queues
6. **Saga Pattern** - Coordinate multi-step processes (optional/advanced)
7. **Event Versioning** - Schema evolution strategies

---

## Docs in This Phase

1. **01-event-driven-overview.md** - This file
2. **02-rabbitmq-masstransit-setup.md** - Docker + MassTransit configuration
3. **03-integration-events.md** - Publishing and consuming events
4. **04-idempotency-error-handling.md** - DLQ, retries, idempotent handlers
5. **05-sagas-orchestration.md** - Saga pattern for distributed workflows
6. **06-event-versioning.md** - Schema evolution and backwards compatibility

---

> Next: [02-rabbitmq-masstransit-setup.md](./02-rabbitmq-masstransit-setup.md) - Setting up the message infrastructure
