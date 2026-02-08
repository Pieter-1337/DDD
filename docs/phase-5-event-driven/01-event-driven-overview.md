# Event-Driven Architecture Overview

## What Is Event-Driven Architecture?

Event-Driven Architecture (EDA) is a pattern where systems communicate through **events** - notifications that something significant happened.

```
Traditional (Synchronous):
+---------+   HTTP/RPC   +---------+
| Service | -----------> | Service |   Tight coupling, blocking
|    A    | <----------- |    B    |
+---------+              +---------+

Event-Driven (Asynchronous):
+---------+              +-------------+              +---------+
| Service | --publish--> | Message Bus | --consume--> | Service |
|    A    |              | (RabbitMQ)  |              |    B    |
+---------+              +-------------+              +---------+
                               |
                               +--consume--> +---------+
                                             | Service |
                                             |    C    |
                                             +---------+
```

---

## Two Types of Events

This project uses **two types of events** for different purposes:

| Type | Purpose | Transport | Scope |
|------|---------|-----------|-------|
| **Domain Events** | Internal decoupling within a bounded context | MediatR (in-memory) | Same process |
| **Integration Events** | Cross-bounded-context communication | MassTransit/RabbitMQ | Across services |

### The Full Event Flow (With TransactionBehavior)

When using commands wrapped in `TransactionBehavior`, the flow ensures transactional consistency:

```
TransactionBehavior.Handle()
  |
  +-- BeginTransactionAsync()
  |
  +-- Handler runs
  |     |
  |     +-- Entity.Suspend()
  |     |       +-- AddDomainEvent(PatientSuspendedEvent)
  |     |
  |     +-- _uow.SaveChangesAsync()
  |           |
  |           +-- 1. DispatchDomainEventsAsync() -> MediatR (BEFORE DB save)
  |           |       +-- AuditHandler (log the action)
  |           |       +-- NotificationHandler (send email)
  |           |       +-- IntegrationEventHandler -> QueueIntegrationEvent()
  |           |
  |           +-- 2. _context.SaveChangesAsync() (DB save, in transaction)
  |           |
  |           +-- 3. [integration events queued, NOT published yet]
  |
  +-- CloseTransactionAsync()
        |
        +-- CommitAsync() (transaction committed)
        |
        +-- PublishAndClearIntegrationEventsAsync() -> RabbitMQ (AFTER commit)
                +-- Billing context
                +-- Analytics context
```

**Key guarantees:**
- Domain events dispatch BEFORE DB save (handlers can modify state in same transaction)
- If a domain event handler throws, the entire transaction rolls back
- Integration events are published AFTER the transaction commits
- On rollback, queued integration events are discarded (never published)

### Domain Events (Internal)

Domain events enable decoupling **within** a bounded context:

```csharp
// Entity raises domain event
public void Suspend()
{
    Status = PatientStatus.Suspended;
    AddDomainEvent(new PatientSuspendedEvent(Id));
}

// Handler reacts (in same process)
public class AuditPatientSuspensionHandler : INotificationHandler<PatientSuspendedEvent>
{
    public Task Handle(PatientSuspendedEvent evt, CancellationToken ct)
    {
        // Log audit entry
        return Task.CompletedTask;
    }
}
```

**Use for:**
- Audit logging
- Cache invalidation
- Sending notifications
- Internal workflow triggers

### Integration Events (External)

Integration events communicate **across** bounded contexts:

```csharp
// Queued in command handler or domain event handler
_uow.QueueIntegrationEvent(new PatientSuspendedIntegrationEvent
{
    PatientId = patient.Id,
    SuspendedAt = DateTime.UtcNow
});

// Consumed in another bounded context
public class PatientSuspendedConsumer : IConsumer<PatientSuspendedIntegrationEvent>
{
    public Task Consume(ConsumeContext<PatientSuspendedIntegrationEvent> context)
    {
        // Billing: pause invoicing
        return Task.CompletedTask;
    }
}
```

**Use for:**
- Cross-context communication
- Microservice integration
- External system notifications

---

## Messages vs Events

Two types of communication through a message broker:

```
MESSAGE - "Do something" (point-to-point)
------------------------------------------

     Producer
         |
         | Send(SendWelcomeEmail)
         v
    +---------+
    |  Queue  | ------> One specific receiver
    +---------+

    - Sender knows the destination
    - Exactly ONE handler processes it
    - Examples: SendEmail, ProcessPayment


EVENT - "Something happened" (publish/subscribe)
-------------------------------------------------

     Producer
         |
         | Publish(PatientCreated)
         v
    +---------+
    |Exchange |
    +----+----+
         |
    +----+----+--------+  Only subscribers receive it
    v         v        v
  Queue1    Queue2   Queue3
    |         |        |
    v         v        v
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
await _publishEndpoint.Publish(new PatientCreatedIntegrationEvent { ... });
```

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
    _uow.RepositoryFor<Appointment>().Add(appointment);

    // Queue event - published after save
    _uow.QueueIntegrationEvent(new AppointmentCreatedIntegrationEvent
    {
        AppointmentId = appointment.Id,
        PatientId = appointment.PatientId,
        DoctorId = appointment.DoctorId,
        ScheduledAt = appointment.ScheduledAt
    });

    await _uow.SaveChangesAsync();
    return appointment.Id;
}
```

**Benefits:**
- **Decoupled** - Scheduling doesn't know who's listening
- **Resilient** - Other services can be temporarily down
- **Fast** - Returns immediately after publishing
- **Scalable** - Add more consumers without changing publisher

---

## Integration Events

Integration events enable cross-bounded-context communication. They:
- Live in `Shared/IntegrationEvents/`
- Are published to RabbitMQ via MassTransit
- Have durability, retry, and dead-letter queues

### Defining an Integration Event

```csharp
// Shared/IntegrationEvents/Scheduling/PatientCreatedIntegrationEvent.cs
public record PatientCreatedIntegrationEvent(
    Guid PatientId,
    string FirstName,
    string LastName,
    string Email,
    DateTime DateOfBirth
) : IntegrationEventBase;
```

### Publishing from Command Handlers

```csharp
public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    private readonly IUnitOfWork _uow;

    public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
    {
        var patient = Patient.Create(
            cmd.FirstName,
            cmd.LastName,
            cmd.Email,
            cmd.DateOfBirth);

        _uow.RepositoryFor<Patient>().Add(patient);

        // Queue integration event
        _uow.QueueIntegrationEvent(new PatientCreatedIntegrationEvent(
            patient.Id,
            cmd.FirstName,
            cmd.LastName,
            cmd.Email,
            cmd.DateOfBirth));

        // SaveChangesAsync publishes queued events after successful save
        await _uow.SaveChangesAsync(ct);

        return patient.Id;
    }
}
```

### Consuming Integration Events

```csharp
// Scheduling.Infrastructure/Consumers/PatientCreatedEventConsumer.cs
public class PatientCreatedEventConsumer : IConsumer<PatientCreatedIntegrationEvent>
{
    private readonly ILogger<PatientCreatedEventConsumer> _logger;

    public Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Consumed PatientCreatedIntegrationEvent: {PatientId} - {FirstName} {LastName}",
            message.PatientId,
            message.FirstName,
            message.LastName);

        // React to the event: send email, create billing profile, etc.

        return Task.CompletedTask;
    }
}
```

---

## The Publishing Flow

```
+-----------------------------------------------------------------------------+
|                           SCHEDULING CONTEXT                                  |
|                                                                             |
|  +----------------------------------------------------------------------+   |
|  |   CreatePatientCommandHandler                                        |   |
|  |                                                                      |   |
|  |   1. var patient = Patient.Create(...);                              |   |
|  |                                                                      |   |
|  |   2. _uow.RepositoryFor<Patient>().Add(patient);                     |   |
|  |                                                                      |   |
|  |   3. _uow.QueueIntegrationEvent(                                     |   |
|  |         new PatientCreatedIntegrationEvent(...));                    |   |
|  |                                                                      |   |
|  |   4. await _uow.SaveChangesAsync();                                  |   |
|  |      // Publishes queued events to RabbitMQ after save               |   |
|  +----------------------------------------------------------------------+   |
|                                                                             |
+-----------------------------------------------------------------------------+
                                      |
                                      | MassTransit (via IEventBus)
                                      v
                          +-----------------------+
                          |      RabbitMQ         |
                          |                       |
                          | PatientCreated        |
                          | IntegrationEvent      |
                          +-----------+-----------+
                                      |
                    +-----------------+-----------------+
                    |                 |                 |
                    v                 v                 v
+------------------------------+  +------------------------------+  +-----------------+
|      BILLING CONTEXT         |  |    NOTIFICATION CONTEXT      |  |   ANALYTICS     |
|                              |  |                              |  |                 |
|  Create billing profile      |  |  Send welcome email          |  |  Track signup   |
|  for new patient             |  |                              |  |                 |
+------------------------------+  +------------------------------+  +-----------------+
```

---

## Intra-Domain Messaging: Async Processing Within a Context

While integration events typically cross bounded context boundaries, you can also use messaging for **asynchronous processing within a single context**.

### The Problem: Not Everything Should Be Synchronous

```csharp
// Synchronous command - blocks until all 100,000 records are processed
public async Task Handle(MigratePatientRecordsCommand cmd, CancellationToken ct)
{
    foreach (var patientId in cmd.PatientIds) // 100,000 iterations
    {
        var patient = await _repository.GetById(patientId);
        patient.Migrate();
        await _repository.Update(patient);
    }
    // Caller is blocked for minutes/hours
}
```

### The Solution: Queue Work, Process Asynchronously

```csharp
// Step 1: Command queues work
public async Task Handle(InitiatePatientMigrationCommand cmd, CancellationToken ct)
{
    foreach (var patientId in cmd.PatientIds)
    {
        await _publishEndpoint.Publish(new MigratePatientMessage
        {
            PatientId = patientId
        }, ct);
    }
    // Returns immediately
}

// Step 2: Consumer processes each patient independently
public class MigratePatientMessageConsumer : IConsumer<MigratePatientMessage>
{
    public async Task Consume(ConsumeContext<MigratePatientMessage> context)
    {
        var patient = await _repository.GetById(context.Message.PatientId);
        patient.Migrate();
        await _unitOfWork.SaveChanges();
        // Each patient processed independently, with retry on failure
    }
}
```

**Benefits:**
- Non-blocking for caller
- Parallel processing
- Individual retry on failure
- Scalable (add more consumers)

---

## Message Delivery Guarantees

| Guarantee | Description | Use Case |
|-----------|-------------|----------|
| **At-most-once** | Message may be lost, never duplicated | Metrics, logs |
| **At-least-once** | Message never lost, may be duplicated | Most business events |
| **Exactly-once** | Message never lost, never duplicated | Financial transactions (hard) |

**RabbitMQ + MassTransit provide at-least-once** by default. This means:
- Messages are persisted and acknowledged
- If consumer fails, message is redelivered
- **Your handlers must be idempotent** (safe to run multiple times)

---

## Error Handling Strategy

```
Message Published
       |
       v
+-----------------+
|    Consumer     |
|   Attempts      |
|   Processing    |
+--------+--------+
         |
    +----+----+
    | Success |----> Message acknowledged, removed from queue
    +----+----+
         |
    +----+----+
    | Failure |----> Retry with exponential backoff
    +----+----+
         |
         v
+-----------------+
|  Retry Limit    |
|   Exceeded      |
+--------+--------+
         |
         v
+-----------------+
|   Dead Letter   |
|     Queue       |----> Manual inspection, reprocessing
+-----------------+
```

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
