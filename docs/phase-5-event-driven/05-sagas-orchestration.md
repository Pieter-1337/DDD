# Sagas & Orchestration

## Overview

When a business process spans multiple services, you need a way to coordinate. This document covers:
- Choreography vs Orchestration
- The Saga pattern
- MassTransit state machines
- Compensation (rollback)

> **Note:** Sagas are an advanced topic. You may want to skip this initially and return when you have a real multi-step workflow to implement.

---

## The Problem: Distributed Transactions

In a monolith, you can wrap multiple operations in a database transaction:

```csharp
// Monolith - easy
using var transaction = await _context.Database.BeginTransactionAsync();
try
{
    await _context.Appointments.AddAsync(appointment);
    await _context.Invoices.AddAsync(invoice);
    await _context.Notifications.AddAsync(notification);
    await _context.SaveChangesAsync();
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

With microservices, each service has its own database:

```
┌───────────────┐    ┌───────────────┐    ┌───────────────┐
│   Scheduling  │    │    Billing    │    │ Notification  │
│   Service     │    │    Service    │    │   Service     │
└───────┬───────┘    └───────┬───────┘    └───────┬───────┘
        │                    │                    │
        ▼                    ▼                    ▼
   ┌─────────┐          ┌─────────┐          ┌─────────┐
   │   DB    │          │   DB    │          │   DB    │
   └─────────┘          └─────────┘          └─────────┘

   No shared transaction possible!
```

---

## Choreography vs Orchestration

Two approaches to coordinate distributed processes:

### Choreography (Event-Driven)

Each service reacts to events and publishes new events:

```
┌─────────────┐  AppointmentCreated  ┌─────────────┐  InvoiceCreated  ┌─────────────┐
│  Scheduling │ ──────────────────>  │   Billing   │ ───────────────> │ Notification│
└─────────────┘                      └─────────────┘                  └─────────────┘
                                            │
                                            │ PaymentFailed
                                            ▼
                                     ┌─────────────┐
                                     │  Scheduling │  (Cancel appointment)
                                     └─────────────┘
```

**Pros:**
- Loosely coupled
- No single point of failure
- Simple for straightforward flows

**Cons:**
- Hard to understand the full flow
- Difficult to track process state
- Complex error handling

### Orchestration (Saga)

A central coordinator manages the flow:

```
                    ┌─────────────────────┐
                    │   Saga Orchestrator │
                    │   (State Machine)   │
                    └──────────┬──────────┘
                               │
        ┌──────────────────────┼──────────────────────┐
        │                      │                      │
        ▼                      ▼                      ▼
 ┌─────────────┐        ┌─────────────┐        ┌─────────────┐
 │  Scheduling │        │   Billing   │        │ Notification│
 └─────────────┘        └─────────────┘        └─────────────┘
```

**Pros:**
- Clear process flow
- Easy to track state
- Centralized error handling

**Cons:**
- Single point of control (not failure if done right)
- More coupling to orchestrator

---

## The Saga Pattern

A saga is a sequence of local transactions. If one step fails, compensating transactions undo previous steps.

```
Book Appointment Saga:

Step 1: Create Appointment    ──> Compensate: Cancel Appointment
           │
           ▼
Step 2: Create Invoice        ──> Compensate: Void Invoice
           │
           ▼
Step 3: Send Confirmation     ──> (No compensation needed)
           │
           ▼
       Complete ✅


If Step 2 fails:
Step 1: Create Appointment ✅
Step 2: Create Invoice ❌
        │
        ▼
Compensate Step 1: Cancel Appointment
        │
        ▼
    Saga Failed
```

---

## MassTransit State Machines

MassTransit provides `MassTransitStateMachine<T>` for saga orchestration.

### Define the Saga State

```csharp
// BookAppointmentSaga/BookAppointmentSagaState.cs
public class BookAppointmentSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }  // Required by MassTransit
    public string CurrentState { get; set; }  // Tracks state

    // Saga data
    public Guid AppointmentId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public DateTime ScheduledAt { get; set; }

    // Track what's been done (for compensation)
    public bool AppointmentCreated { get; set; }
    public bool InvoiceCreated { get; set; }
    public Guid? InvoiceId { get; set; }

    // Error tracking
    public string? FailureReason { get; set; }
}
```

### Define the State Machine

```csharp
// BookAppointmentSaga/BookAppointmentStateMachine.cs
public class BookAppointmentStateMachine : MassTransitStateMachine<BookAppointmentSagaState>
{
    // States
    public State CreatingAppointment { get; private set; }
    public State CreatingInvoice { get; private set; }
    public State SendingConfirmation { get; private set; }
    public State Completed { get; private set; }
    public State Failed { get; private set; }
    public State Compensating { get; private set; }

    // Events (messages that trigger state transitions)
    public Event<BookAppointmentRequested> BookAppointmentRequested { get; private set; }
    public Event<AppointmentCreated> AppointmentCreated { get; private set; }
    public Event<AppointmentCreationFailed> AppointmentCreationFailed { get; private set; }
    public Event<InvoiceCreated> InvoiceCreated { get; private set; }
    public Event<InvoiceCreationFailed> InvoiceCreationFailed { get; private set; }
    public Event<ConfirmationSent> ConfirmationSent { get; private set; }
    public Event<AppointmentCancelled> AppointmentCancelled { get; private set; }

    public BookAppointmentStateMachine()
    {
        // Define the state property
        InstanceState(x => x.CurrentState);

        // Event correlation (how to find the saga instance)
        Event(() => BookAppointmentRequested, x =>
            x.CorrelateById(context => context.Message.CorrelationId));
        Event(() => AppointmentCreated, x =>
            x.CorrelateById(context => context.Message.CorrelationId));
        // ... other event correlations

        // Initial state - waiting for request
        Initially(
            When(BookAppointmentRequested)
                .Then(context =>
                {
                    context.Saga.PatientId = context.Message.PatientId;
                    context.Saga.DoctorId = context.Message.DoctorId;
                    context.Saga.ScheduledAt = context.Message.ScheduledAt;
                })
                .Publish(context => new CreateAppointmentCommand
                {
                    CorrelationId = context.Saga.CorrelationId,
                    PatientId = context.Saga.PatientId,
                    DoctorId = context.Saga.DoctorId,
                    ScheduledAt = context.Saga.ScheduledAt
                })
                .TransitionTo(CreatingAppointment)
        );

        // Creating Appointment state
        During(CreatingAppointment,
            When(AppointmentCreated)
                .Then(context =>
                {
                    context.Saga.AppointmentId = context.Message.AppointmentId;
                    context.Saga.AppointmentCreated = true;
                })
                .Publish(context => new CreateInvoiceCommand
                {
                    CorrelationId = context.Saga.CorrelationId,
                    AppointmentId = context.Saga.AppointmentId,
                    PatientId = context.Saga.PatientId
                })
                .TransitionTo(CreatingInvoice),

            When(AppointmentCreationFailed)
                .Then(context => context.Saga.FailureReason = context.Message.Reason)
                .TransitionTo(Failed)
                .Finalize()
        );

        // Creating Invoice state
        During(CreatingInvoice,
            When(InvoiceCreated)
                .Then(context =>
                {
                    context.Saga.InvoiceId = context.Message.InvoiceId;
                    context.Saga.InvoiceCreated = true;
                })
                .Publish(context => new SendConfirmationCommand
                {
                    CorrelationId = context.Saga.CorrelationId,
                    PatientId = context.Saga.PatientId,
                    AppointmentId = context.Saga.AppointmentId
                })
                .TransitionTo(SendingConfirmation),

            When(InvoiceCreationFailed)
                .Then(context => context.Saga.FailureReason = context.Message.Reason)
                // Compensate - cancel the appointment
                .Publish(context => new CancelAppointmentCommand
                {
                    CorrelationId = context.Saga.CorrelationId,
                    AppointmentId = context.Saga.AppointmentId,
                    Reason = "Invoice creation failed"
                })
                .TransitionTo(Compensating)
        );

        // Compensating state
        During(Compensating,
            When(AppointmentCancelled)
                .TransitionTo(Failed)
                .Finalize()
        );

        // Sending Confirmation state
        During(SendingConfirmation,
            When(ConfirmationSent)
                .TransitionTo(Completed)
                .Finalize()
        );

        // Clean up completed sagas
        SetCompletedWhenFinalized();
    }
}
```

### Visual Flow

```
         BookAppointmentRequested
                   │
                   ▼
         ┌─────────────────┐
         │    Creating     │
         │   Appointment   │
         └────────┬────────┘
                  │
      ┌───────────┴───────────┐
      │                       │
AppointmentCreated    AppointmentCreationFailed
      │                       │
      ▼                       ▼
┌─────────────────┐     ┌──────────┐
│    Creating     │     │  Failed  │
│     Invoice     │     └──────────┘
└────────┬────────┘
         │
    ┌────┴─────┐
    │          │
InvoiceCreated │
    │          │
    ▼     InvoiceCreationFailed
┌─────────────┐     │
│   Sending   │     ▼
│Confirmation │  ┌──────────────┐
└──────┬──────┘  │ Compensating │
       │         │  (Cancel     │
       │         │ Appointment) │
ConfirmationSent └──────┬───────┘
       │                │
       ▼         AppointmentCancelled
┌─────────────┐         │
│  Completed  │         ▼
└─────────────┘   ┌──────────┐
                  │  Failed  │
                  └──────────┘
```

---

## Register the Saga

```csharp
// In MassTransit configuration
services.AddMassTransit(x =>
{
    // Register the saga with state machine
    x.AddSagaStateMachine<BookAppointmentStateMachine, BookAppointmentSagaState>()
        .InMemoryRepository(); // For development

    // For production, use a persistent store:
    // .EntityFrameworkRepository(r =>
    // {
    //     r.ConcurrencyMode = ConcurrencyMode.Pessimistic;
    //     r.AddDbContext<SagaDbContext, SagaDbContextFactory>();
    // });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");
        cfg.ConfigureEndpoints(context);
    });
});
```

---

## Saga Persistence

For production, persist saga state to survive restarts:

### EF Core Repository

```csharp
// SagaDbContext.cs
public class SagaDbContext : DbContext
{
    public DbSet<BookAppointmentSagaState> BookAppointmentSagas { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BookAppointmentSagaState>(entity =>
        {
            entity.HasKey(s => s.CorrelationId);
            entity.Property(s => s.CurrentState).HasMaxLength(64);
        });
    }
}

// Configuration
x.AddSagaStateMachine<BookAppointmentStateMachine, BookAppointmentSagaState>()
    .EntityFrameworkRepository(r =>
    {
        r.ConcurrencyMode = ConcurrencyMode.Pessimistic;
        r.ExistingDbContext<SagaDbContext>();
    });
```

---

## Choreography Alternative

For simpler flows, choreography with events may be enough:

```csharp
// Service A publishes event
await _publishEndpoint.Publish(new AppointmentCreatedIntegrationEvent { ... });

// Service B consumes and publishes next event in chain
public class AppointmentCreatedIntegrationEventHandler
    : IntegrationEventHandler<AppointmentCreatedIntegrationEvent>
{
    private readonly IBillingService _billingService;
    private readonly IPublishEndpoint _publishEndpoint;

    public AppointmentCreatedIntegrationEventHandler(
        IBillingService billingService,
        IPublishEndpoint publishEndpoint,
        ILogger<AppointmentCreatedIntegrationEventHandler> logger) : base(logger)
    {
        _billingService = billingService;
        _publishEndpoint = publishEndpoint;
    }

    protected override async Task HandleAsync(
        AppointmentCreatedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        var invoice = await _billingService.CreateInvoice(message, cancellationToken);

        // Publish next event in chain
        await _publishEndpoint.Publish(new InvoiceCreatedIntegrationEvent
        {
            InvoiceId = invoice.Id,
            AppointmentId = message.AppointmentId
        }, cancellationToken);
    }
}

// Service C consumes
public class InvoiceCreatedIntegrationEventHandler
    : IntegrationEventHandler<InvoiceCreatedIntegrationEvent>
{
    private readonly INotificationService _notificationService;

    public InvoiceCreatedIntegrationEventHandler(
        INotificationService notificationService,
        ILogger<InvoiceCreatedIntegrationEventHandler> logger) : base(logger)
    {
        _notificationService = notificationService;
    }

    protected override async Task HandleAsync(
        InvoiceCreatedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        await _notificationService.SendConfirmation(message, cancellationToken);
    }
}
```

### When to Use Each

| Scenario | Approach |
|----------|----------|
| 2-3 simple steps | Choreography |
| Complex flow with branches | Orchestration (Saga) |
| Need to track process state | Saga |
| Need rollback/compensation | Saga |
| Loose coupling is priority | Choreography |

---

## Compensation Strategies

### Semantic Compensation

Not all actions can be literally undone:

| Action | Compensation |
|--------|--------------|
| Create appointment | Cancel appointment |
| Create invoice | Void invoice |
| Charge credit card | Refund |
| Send email | Send cancellation email |
| Ship order | (Cannot undo - create return) |

### Compensation Order

Compensate in reverse order:

```
Forward:
Step 1 → Step 2 → Step 3 → ❌ Fail

Compensate:
Step 3 (already failed, skip) → Compensate Step 2 → Compensate Step 1
```

---

## Verification Checklist

- [ ] Understand choreography vs orchestration tradeoffs
- [ ] Saga state class with CorrelationId and CurrentState
- [ ] State machine with states, events, and transitions
- [ ] Compensation logic for each step
- [ ] Saga registered in MassTransit
- [ ] Saga state persistence (for production)
- [ ] States visible in monitoring (optional)

---

## When to Add Sagas

Start without sagas. Add them when:
1. You have a multi-step process that needs coordination
2. You need to track process state
3. Compensation is required if a step fails
4. Choreography becomes too complex to understand

For this learning project, you may not need sagas initially. Start with simple event choreography.

---

> Next: [06-event-versioning.md](./06-event-versioning.md) - Handling schema evolution
