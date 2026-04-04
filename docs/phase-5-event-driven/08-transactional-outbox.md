# Transactional Outbox Pattern

## Overview

The Transactional Outbox Pattern solves the dual-write problem: ensuring database commits and message publishing are atomic. Without it, crashes between database save and message publish can cause lost events.

This document covers:
- The crash gap problem with naive publish-after-commit
- How the outbox pattern provides atomicity
- MassTransit's EF Core outbox (3 tables, Bus vs Consumer outbox)
- Wolverine outbox alternative (separate schema approach)
- Comparison and key takeaways

For step-by-step implementation guides, see:
- [08a — MassTransit Outbox Implementation](./08a-transactional-outbox-masstransit.md)
- [08b — Wolverine Outbox Implementation](./08b-transactional-outbox-wolverine.md)

**Key Pattern:** Integration events are written to an outbox TABLE in the same database transaction as domain data. A background service reads from the outbox and delivers to the message broker. If the process crashes after commit, events remain in the outbox and are delivered on restart.

---

## The Problem: The Crash Gap

### Current Flow (Vulnerable to Data Loss)

The current implementation saves changes to SQL Server, then publishes integration events to RabbitMQ:

```csharp
// EfCoreUnitOfWork.CloseTransactionAsync()
await _transaction.CommitAsync(cancellationToken);              // 1. SQL Server COMMIT
await PublishAndClearIntegrationEventsAsync(cancellationToken); // 2. RabbitMQ PUBLISH
```

**The crash gap:**

```
SaveChangesAsync()
    ↓
┌─────────────────────┐
│ SQL Server COMMIT   │  ✅ Data persisted
└─────────────────────┘
    ↓
    ╳ ← CRASH HERE = events lost forever
    ↓
┌─────────────────────┐
│ RabbitMQ PUBLISH    │  ✗ Never reached
└─────────────────────┘
```

If the process crashes, is killed, or loses power between steps 1 and 2:
- The database transaction has committed (patient created, appointment scheduled, etc.)
- Integration events were never published to RabbitMQ
- Other bounded contexts (Billing, Notifications) never receive the events
- **Data inconsistency:** Scheduling has a patient, but Billing never creates a billing profile

**Why try/catch doesn't help:**

```csharp
try
{
    await _eventBus.PublishAsync(integrationEvent, cancellationToken);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to publish event...");
    // Event is lost! Can't rollback - transaction already committed
}
```

Once the database transaction commits, you can't roll it back. The event is lost.

---

## The Solution: Transactional Outbox Pattern

### How It Works

Instead of publishing directly to RabbitMQ, write events to an outbox table in the SAME database transaction as domain data:

```
SaveChangesAsync()
    ↓
┌─────────────────────────────────┐
│ Single Database Transaction     │
│                                  │
│  1. Insert/Update domain data   │
│  2. Insert outbox entries       │ ← Both or neither
└─────────────────────────────────┘
    ↓
    ✅ COMMIT (atomic)
    ↓
┌─────────────────────────────────┐
│ Background Delivery Service     │
│ (polls outbox table)            │
│                                  │
│  1. Read pending messages       │
│  2. Publish to RabbitMQ         │
│  3. Mark as delivered (delete)  │
└─────────────────────────────────┘
```

**If the process crashes after commit:**
- Domain data is persisted ✅
- Outbox entries are persisted ✅
- On restart, background service picks up pending messages and delivers them

**The gap is eliminated:**

```
┌─────────────────────────────────┐
│  Domain Data + Outbox Entries   │
│  (Single Transaction)           │
└─────────────────────────────────┘
    ↓
    ╳ ← CRASH HERE = events still in outbox table
    ↓
┌─────────────────────────────────┐
│  Process Restarts               │
│  Background service finds       │
│  pending messages and delivers  │
└─────────────────────────────────┘
```

### Guarantees

| Scenario | Without Outbox | With Outbox |
|----------|---------------|-------------|
| Happy path | Events published ✅ | Events published ✅ |
| Crash before commit | No data, no events ✅ | No data, no events ✅ |
| Crash after commit, before publish | Data saved, events lost ❌ | Data saved, events in outbox ✅ |
| RabbitMQ unavailable | Events lost ❌ | Events queued in outbox ✅ |
| Process killed | Events lost ❌ | Events in outbox ✅ |

---

## MassTransit's EF Core Outbox

MassTransit provides a built-in outbox implementation for Entity Framework Core. It uses three tables to manage the publish and consume lifecycle.

### The Three Tables

```csharp
// Stores serialized messages waiting for delivery
public class OutboxMessage
{
    public long SequenceNumber { get; set; }      // Auto-increment ID
    public Guid MessageId { get; set; }          // MassTransit message ID
    public Guid ConversationId { get; set; }     // Correlation ID
    public string ContentType { get; set; }      // application/json
    public byte[] Body { get; set; }             // Serialized message
    public DateTime? SentTime { get; set; }      // When published (null = pending)
    // ... other metadata
}

// Tracks delivery state per outbox
public class OutboxState
{
    public Guid OutboxId { get; set; }           // Unique per outbox instance
    public long LastSequenceNumber { get; set; } // Checkpoint for delivery
    public DateTime Created { get; set; }
    // ... other state
}

// Tracks consumed messages for consumer-side idempotency
public class InboxState
{
    public Guid MessageId { get; set; }          // Unique message ID
    public Guid ConsumerId { get; set; }         // Which consumer processed it
    public DateTime Delivered { get; set; }      // When delivered
    // ... other tracking
}
```

**OutboxMessage** stores events waiting to be delivered. When the background service publishes an event, it's either marked as sent (SentTime populated) or deleted.

**OutboxState** tracks the delivery checkpoint. The background service uses `LastSequenceNumber` to know where to resume after a restart.

**InboxState** provides consumer-side deduplication. When a consumer processes a message, MassTransit records the MessageId. If the same message is redelivered, MassTransit checks InboxState and skips it.

### Bus Outbox vs Consumer Outbox

MassTransit provides two outbox interceptors. Both solve the same problem (atomic publish via outbox table), but they intercept `Publish()` calls in different execution contexts. The difference is about **WHERE** the call originates, not what it does.

| Type | Intercepts | Execution Context | Typical Usage |
|------|-----------|-------------------|---------------|
| **Bus Outbox** | `IPublishEndpoint.Publish()` from application code | Outside any MassTransit consumer | Command handlers, domain event handlers, controllers |
| **Consumer Outbox** | `context.Publish()` from within a consumer's `Consume()` method | Inside a `ConsumeContext` scope | Sagas, event choreography, multi-step workflows |

#### How MassTransit Determines Which Outbox to Use

MassTransit automatically selects the correct outbox based on DI scoping — **no detection logic needed from the developer**.

When MassTransit invokes a consumer, it creates a scoped `ConsumeContext`. Within that scope, `IPublishEndpoint` resolves to the `ConsumeContext` itself, and the **Consumer Outbox middleware intercepts**.

Outside a consumer (command handlers, domain event handlers), there's no `ConsumeContext`, so `IPublishEndpoint` resolves to the bus-level endpoint, and the **Bus Outbox intercepts**.

#### Why We Need Bus Outbox Specifically

In our application, the first publish originates from **application code**, not from a consumer:

```
CreatePatientCommand
  → CreatePatientCommandHandler
    → Patient.Create() (adds PatientCreatedEvent)
      → UnitOfWork.SaveChangesAsync()
        → Dispatches PatientCreatedEvent (domain event)
          → PatientCreatedEventHandler (MediatR handler)
            → IEventBus.PublishAsync() (integration event)
              → IPublishEndpoint.Publish() ← NO ConsumeContext here!
```

At that point, there's no `ConsumeContext` scope. Without `UseBusOutbox()`, the publish would bypass the outbox entirely and go directly to RabbitMQ — **we'd be back to the crash gap**.

#### You Get Both Outboxes Together

`AddEntityFrameworkOutbox<TDbContext>()` **always enables the Consumer Outbox**. Adding `UseBusOutbox()` enables the Bus Outbox on top. So our configuration gives us both:

```csharp
x.AddEntityFrameworkOutbox<TDbContext>(o =>
{
    o.UseSqlServer();
    o.UseBusOutbox();  // ← Adds Bus Outbox; Consumer Outbox is always enabled
});
```

This means:
- **Bus Outbox** intercepts the initial publish from Scheduling (command handler → domain event handler → `IEventBus`)
- **Consumer Outbox** intercepts any secondary publishes from within consumers

#### Practical Example: Both Outboxes in Action

**Scenario 1: Creating a patient (Bus Outbox)**

```csharp
// Application code: Domain event handler publishing to another BC
public class PatientCreatedEventHandler : INotificationHandler<PatientCreatedEvent>
{
    private readonly IUnitOfWork _unitOfWork;

    public Task Handle(PatientCreatedEvent notification, CancellationToken ct)
    {
        // Publishing from MediatR handler (no ConsumeContext)
        // → Bus Outbox intercepts this and writes to Scheduling_OutboxMessage
        _unitOfWork.QueueIntegrationEvent(new PatientCreatedIntegrationEvent
        {
            PatientId = notification.Patient.Id.Value,
            FullName = notification.Patient.FullName
        });
        return Task.CompletedTask;
    }
}
```

**Scenario 2: Consuming that event and publishing to a third BC (Consumer Outbox)**

```csharp
// Consumer in Billing BC receives the event from Scheduling
public class PatientCreatedIntegrationEventHandler
    : IConsumer<PatientCreatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
    {
        // Create billing profile...
        var profile = BillingProfile.Create(...);

        // Now publish to a third bounded context (e.g., CRM)
        // This is INSIDE a ConsumeContext → Consumer Outbox intercepts
        await context.Publish(new BillingProfileCreatedIntegrationEvent
        {
            ProfileId = profile.Id.Value,
            PatientId = context.Message.PatientId
        });

        // Both the billing profile creation AND the outbox entry for CRM
        // are in the same transaction (atomic)
    }
}
```

**Summary of the flow:**

```
Scheduling (Bus Outbox)
  → Publishes PatientCreatedIntegrationEvent to RabbitMQ
    → Billing consumes event
      → Billing (Consumer Outbox)
        → Publishes BillingProfileCreatedIntegrationEvent to RabbitMQ
          → CRM consumes event
```

The first publish uses **Bus Outbox** (application code). The second publish uses **Consumer Outbox** (inside a MassTransit consumer).

> **Implementation:** See [08a — MassTransit Outbox Implementation](./08a-transactional-outbox-masstransit.md) for the step-by-step walkthrough.

---

## Wolverine Outbox Alternative

Wolverine provides EF Core integration for its transactional outbox via the `WolverineFx.EntityFrameworkCore` package. Like MassTransit, it can persist outbox messages atomically within your DbContext's transaction — but the mechanism differs.

### How It Works

Wolverine uses `IDbContextOutbox<TDbContext>` to attach outbox messages to your EF Core `DbContext`. When `CommitAsync()` is called, it:

1. Adds outgoing envelope records to the DbContext's change tracker
2. Calls `SaveChangesAsync()` — domain entities + outbox messages written atomically
3. Flushes messages to RabbitMQ after the save succeeds

Wolverine also manages its own infrastructure tables in a **per-BC SQL schema**:

| Table | Purpose |
|-------|---------|
| `wolverine_billing.incoming_envelopes` | Inbox for incoming messages (idempotency) |
| `wolverine_billing.outgoing_envelopes` | Outbox for outgoing messages |
| `wolverine_billing.dead_letters` | Failed messages after all retries |

These tables are auto-provisioned on startup — no EF Core migrations needed for the envelope tables themselves. However, the DbContext needs `MapWolverineEnvelopeStorage()` to map envelope entities for EF Core batching.

### Configuration

```csharp
// In YourDbContext.OnModelCreating:
modelBuilder.MapWolverineEnvelopeStorage("wolverine_<bc_name>");

// In Program.cs:
builder.AddWolverineEventBus<YourDbContext>(connectionString, "wolverine_<bc_name>", opts =>
{
    opts.Discovery.IncludeAssembly(typeof(YourBoundedContext.Infrastructure.ServiceCollectionExtensions).Assembly);
});
```

The `AddWolverineEventBus<TDbContext>()` extension method:
- Registers `IDbContextOutbox<TDbContext>` for outbox message staging
- Registers `ICommitStrategy` as `WolverineCommitStrategy<TDbContext>` — calls `CommitAsync()`
- Configures `PersistMessagesWithSqlServer(connectionString, schemaName)` with a per-BC schema
- Enables `UseEntityFrameworkCoreTransactions()` for Wolverine's handler pipeline

### Key Differences from MassTransit Outbox

| Aspect | MassTransit | Wolverine |
|--------|-------------|-----------|
| Table location | Inside your DbContext (EF Core entities) | Per-BC SQL schema (e.g., `wolverine_billing.*`) |
| Migration strategy | EF Core migrations (`AddInboxStateEntity`, etc.) | Auto-provisioned on startup + `MapWolverineEnvelopeStorage()` |
| DbContext changes | Required (`AddInboxStateEntity`, `AddOutboxMessageEntity`, `AddOutboxState`) | Required (`MapWolverineEnvelopeStorage()`) |
| Atomicity mechanism | `UseBusOutbox()` — transparent via `IPublishEndpoint` | `IDbContextOutbox<T>` + `ICommitStrategy` — explicit flush |
| Schema ownership | Your application | Wolverine framework (per-BC schema) |
| Coexistence | N/A | Both sets of tables can coexist in the same database |

### What You Need with Wolverine

When using Wolverine with EF Core outbox integration:
- **DbContext mapping** — `modelBuilder.MapWolverineEnvelopeStorage("wolverine_<bc_name>")` in `OnModelCreating`
- **No EF Core migration** — Wolverine auto-provisions envelope tables via Weasel
- **No outbox filter registration** — No `cfg.AddEntityFrameworkOutbox<TDbContext>()` equivalent

### Both Can Coexist

If you have MassTransit outbox tables already in your database, the Wolverine tables live in a separate schema (`wolverine_billing.*`) and do not conflict. This means you can run both frameworks side-by-side during a migration period. The `BillingDbContext.OnModelCreating` contains mappings for both — only the active framework's tables are used at runtime.

> **Implementation:** See [08b — Wolverine Outbox Implementation](./08b-transactional-outbox-wolverine.md) for the step-by-step walkthrough.

---

## Comparison: Before vs After

| Aspect | Before (No Outbox) | After (MassTransit Outbox) | After (Wolverine Outbox) |
|--------|-------------------|----------------------------|--------------------------|
| **Reliability** | Events lost on crash | Events guaranteed delivery | Events guaranteed delivery |
| **Atomicity** | Database + broker = 2 operations | Database only (outbox table) | Database only (outbox table) |
| **Latency** | ~10-50ms | ~5-10 seconds | ~5-10 seconds |
| **Failure Mode** | Silent data loss | Retry until success | Retry until success |
| **RabbitMQ Unavailable** | Events lost | Events queued in outbox | Events queued in outbox |
| **Consumer Idempotency** | Manual (if implemented) | Automatic via InboxState | Automatic via incoming envelopes |
| **DbContext Changes** | None | Required (3 entity additions) | Required (`MapWolverineEnvelopeStorage()`) |
| **Migration Needed** | No | Yes (EF Core migration) | No (auto-provisioned) |
| **Schema Location** | N/A | Per-BC prefixed tables (e.g., `Scheduling_OutboxMessage`) | Per-BC schema (e.g., `wolverine_billing.*`) |
| **Operational Overhead** | Low (just RabbitMQ) | Medium (outbox cleanup, monitoring) | Medium (envelope cleanup, monitoring) |
| **Debugging** | No audit trail | Outbox table provides history | Envelope tables provide history |

---

## Key Takeaways

1. **The outbox pattern eliminates the crash gap** between database commit and message publish. Events are persisted with domain data in a single transaction.

2. **MassTransit's EF Core outbox handles all complexity** - serialization, delivery, retries, cleanup, and consumer-side idempotency.

3. **Bus Outbox is required when publishing from application code** (domain event handlers, command handlers). Consumer Outbox is for sagas and event choreography.

4. **InboxState provides consumer-side deduplication for free** - handlers don't need to implement "already processed" checks (though you can add business-specific checks).

5. **Table prefixing prevents conflicts** when multiple bounded contexts share the same SQL Server instance.

6. **The UnitOfWork simplifies dramatically** - no more in-memory queues, no post-commit publish logic, no crash gap error handling.

7. **Trade-off: latency for reliability** - outbox adds ~5-10 seconds of latency (tunable), but guarantees delivery.

8. **Background delivery service is automatic** - MassTransit registers `BusOutboxDeliveryService` as a hosted service when you configure the outbox.

9. **Testing is critical** - verify crash resilience by stopping the API mid-transaction and ensuring events are delivered on restart.

10. **Monitor outbox table growth** in production - implement cleanup policies and alerting for stuck messages.

---

## Additional Resources

- [MassTransit Outbox Documentation](https://masstransit.io/documentation/patterns/transactional-outbox)
- [Microservices Patterns: Transactional Outbox](https://microservices.io/patterns/data/transactional-outbox.html)
- Chris Richardson: "Why Event Sourcing and CQRS?"
- [MassTransit EntityFrameworkCore GitHub](https://github.com/MassTransit/MassTransit/tree/develop/src/Persistence/MassTransit.EntityFrameworkCoreIntegration)

---

> **Next:** Phase 6 - Integration (combining all patterns into a cohesive system with Aspire orchestration and observability)

> **Related:** [05-idempotency-error-handling.md](./05-idempotency-error-handling.md) - Consumer-side idempotency strategies (InboxState complements these patterns)
