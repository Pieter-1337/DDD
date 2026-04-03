# Transactional Outbox Pattern

## Overview

The Transactional Outbox Pattern solves the dual-write problem: ensuring database commits and message publishing are atomic. Without it, crashes between database save and message publish can cause lost events.

This document covers:
- The crash gap problem with naive publish-after-commit
- How the outbox pattern provides atomicity
- MassTransit's EF Core outbox implementation
- Wolverine outbox alternative (separate schema approach)
- Bus Outbox vs Consumer Outbox
- Implementation walkthrough (MassTransit)
- Consumer-side idempotency via InboxState

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

---

## Wolverine Outbox Alternative

Wolverine takes a fundamentally different approach to the transactional outbox pattern. Instead of integrating with your EF Core `DbContext`, Wolverine manages its own outbox tables in a **separate SQL schema**.

### How It Works

Wolverine creates and manages its own set of tables:

| Table | Purpose |
|-------|---------|
| `wolverine.incoming_envelopes` | Inbox for incoming messages (idempotency) |
| `wolverine.outgoing_envelopes` | Outbox for outgoing messages |
| `wolverine.dead_letters` | Failed messages after all retries |

These tables are auto-provisioned on application startup — no EF Core migrations needed.

### Configuration

```csharp
builder.UseWolverine(opts =>
{
    var connectionString = builder.Configuration.GetConnectionString("schedulingdb");

    // Wolverine manages its own tables in the 'wolverine' schema
    opts.PersistMessagesWithSqlServer(connectionString!, "wolverine");

    // Auto-create tables on startup
    opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;

    opts.UseRabbitMq(new Uri(rabbitConnectionString!))
        .AutoProvision();
});
```

### Key Differences from MassTransit Outbox

| Aspect | MassTransit | Wolverine |
|--------|-------------|-----------|
| Table location | Inside your DbContext (EF Core entities) | Separate SQL schema (`wolverine.*`) |
| Migration strategy | EF Core migrations (`AddInboxStateEntity`, etc.) | Auto-provisioned on startup |
| DbContext changes | Required (`AddInboxStateEntity`, `AddOutboxMessageEntity`, `AddOutboxState`) | **None** — zero DbContext changes |
| Schema ownership | Your application | Wolverine framework |
| Coexistence | N/A | Both sets of tables can coexist in the same database |

### What You DON'T Need with Wolverine

If using Wolverine instead of MassTransit, you can skip:
- **DbContext changes** — No `modelBuilder.AddInboxStateEntity()`, `AddOutboxMessageEntity()`, or `AddOutboxState()` calls
- **EF Core migration** — No migration for outbox tables (Wolverine creates them automatically)
- **Outbox filter registration** — No `cfg.AddEntityFrameworkOutbox<TDbContext>()` in MassTransit config

### Both Can Coexist

If you have MassTransit outbox tables already in your database, the Wolverine tables live in a separate schema (`wolverine.*`) and do not conflict. This means you can run both frameworks side-by-side during a migration period.

---

## Implementation Walkthrough

### Step 1: Add NuGet Package

```xml
<!-- Directory.Packages.props -->
<ItemGroup>
  <PackageVersion Include="MassTransit.EntityFrameworkCore" Version="8.3.6" />
</ItemGroup>
```

Add package reference to:
- `BuildingBlocks.Infrastructure.MassTransit/BuildingBlocks.Infrastructure.MassTransit.csproj`
- `Core/Scheduling/Scheduling.Infrastructure/Scheduling.Infrastructure.csproj`
- `Core/Billing/Billing.Infrastructure/Billing.Infrastructure.csproj`

```xml
<ItemGroup>
  <PackageReference Include="MassTransit.EntityFrameworkCore" />
</ItemGroup>
```

### Step 2: Configure MassTransit Outbox

Update `MassTransitExtensions` to make it generic over `TDbContext` and configure the outbox:

```csharp
// BuildingBlocks.Infrastructure.MassTransit/Configuration/MassTransitExtensions.cs
using BuildingBlocks.Application.Messaging;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.MassTransit.Configuration;

public static class MassTransitExtensions
{
    /// <summary>
    /// Adds MassTransit event bus with EF Core outbox support
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type for outbox tables</typeparam>
    public static IServiceCollection AddMassTransitEventBus<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IRegistrationConfigurator>? configureConsumers = null)
        where TDbContext : DbContext
    {
        services.AddMassTransit(x =>
        {
            // Allow host to register consumers from specific assemblies
            configureConsumers?.Invoke(x);

            // Configure EF Core Outbox
            x.AddEntityFrameworkOutbox<TDbContext>(o =>
            {
                o.UseSqlServer();   // Outbox uses SQL Server
                o.UseBusOutbox();   // Intercept Publish() from application code

                // How often to check for pending messages
                o.QueryDelay = TimeSpan.FromSeconds(5);

                // Limit concurrent delivery
                o.QueryMessageLimit = 100;
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                // Try Aspire connection string first, fall back to manual config
                var connectionString = configuration.GetConnectionString("messaging");

                if (!string.IsNullOrEmpty(connectionString))
                {
                    cfg.Host(new Uri(connectionString));
                }
                else
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
                }

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

        // Register IEventBus implementation
        services.AddScoped<IEventBus, MassTransitEventBus>();

        return services;
    }
}
```

**Key configuration options:**

| Option | Purpose | Default | Our Value |
|--------|---------|---------|-----------|
| `QueryDelay` | How often to poll outbox for pending messages | 1 minute | 5 seconds (lower latency for learning/dev) |
| `QueryMessageLimit` | Max messages to fetch per poll | 100 | 100 (same as default) |

### Step 3: Configure DbContext

Add the three outbox tables to each DbContext that publishes integration events:

```csharp
// Core/Scheduling/Scheduling.Infrastructure/SchedulingDbContext.cs
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Scheduling.Infrastructure;

public class SchedulingDbContext : DbContext
{
    public SchedulingDbContext(DbContextOptions<SchedulingDbContext> options)
        : base(options) { }

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply entity configurations
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SchedulingDbContext).Assembly);

        // Add MassTransit outbox tables with prefixed names
        // Prefixing prevents conflicts when multiple contexts share the same database
        modelBuilder.AddInboxStateEntity(o => o.ToTable("Scheduling_InboxState"));
        modelBuilder.AddOutboxMessageEntity(o => o.ToTable("Scheduling_OutboxMessage"));
        modelBuilder.AddOutboxStateEntity(o => o.ToTable("Scheduling_OutboxState"));
    }
}
```

**Why prefix table names?**

Both SchedulingDbContext and BillingDbContext may share the same SQL Server instance. Without prefixes, MassTransit would try to create tables with the same names (InboxState, OutboxMessage, OutboxState), causing conflicts.

Prefixing gives each bounded context its own set of outbox tables:
- `Scheduling_InboxState`, `Scheduling_OutboxMessage`, `Scheduling_OutboxState`
- `Billing_InboxState`, `Billing_OutboxMessage`, `Billing_OutboxState`

Repeat for `BillingDbContext`:

```csharp
// Core/Billing/Billing.Infrastructure/BillingDbContext.cs
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);

    modelBuilder.AddInboxStateEntity(o => o.ToTable("Billing_InboxState"));
    modelBuilder.AddOutboxMessageEntity(o => o.ToTable("Billing_OutboxMessage"));
    modelBuilder.AddOutboxStateEntity(o => o.ToTable("Billing_OutboxState"));
}
```

### Step 4: Update API Registration

Pass the DbContext type to the generic registration method:

```csharp
// WebApplications/Scheduling.WebApi/Program.cs
builder.Services.AddMassTransitEventBus<SchedulingDbContext>(
    builder.Configuration,
    configure =>
    {
        // Register consumers if this API also consumes events
        configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
    });
```

```csharp
// WebApplications/Billing.WebApi/Program.cs
builder.Services.AddMassTransitEventBus<BillingDbContext>(
    builder.Configuration,
    configure =>
    {
        configure.AddConsumers(typeof(Billing.Infrastructure.ServiceCollectionExtensions).Assembly);
    });
```

### Step 5: Simplify UnitOfWork

The outbox changes the publish flow. Integration events are now published BEFORE `SaveChangesAsync()` (writing to the outbox table, not RabbitMQ). The background service handles actual RabbitMQ delivery.

**Before (with crash gap):**

```csharp
public async Task CloseTransactionAsync(Exception? exception = null, CancellationToken ct = default)
{
    if (_transaction is null) return;
    _transactionDepth--;
    if (_transactionDepth > 0) return;

    try
    {
        if (exception is not null)
        {
            await _transaction.RollbackAsync(ct);
            _queuedIntegrationEvents.Clear(); // Discard on rollback
        }
        else
        {
            await _transaction.CommitAsync(ct);                              // 1. SQL COMMIT
            await PublishAndClearIntegrationEventsAsync(ct);                 // 2. RabbitMQ ← crash gap!
        }
    }
    finally
    {
        await _transaction.DisposeAsync();
        _transaction = null;
        _transactionDepth = 0;
    }
}
```

**After (with outbox, no crash gap):**

```csharp
// BuildingBlocks.Infrastructure.EfCore/EfCoreUnitOfWork.cs (key methods only)

public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    await DispatchDomainEventsAsync(cancellationToken);

    // Publish integration events to the outbox BEFORE SaveChangesAsync.
    // With the Transactional Outbox, IEventBus.PublishAsync writes to the OutboxMessage
    // table (not RabbitMQ). SaveChangesAsync then persists domain data + outbox entries
    // atomically in a single transaction. The background BusOutboxDeliveryService
    // picks up outbox entries and delivers them to RabbitMQ.
    await PublishIntegrationEventsToOutboxAsync(cancellationToken);

    return await _context.SaveChangesAsync(cancellationToken);
}

public async Task CloseTransactionAsync(Exception? exception = null, CancellationToken cancellationToken = default)
{
    if (_transaction is null) return;
    _transactionDepth--;
    if (_transactionDepth > 0) return;

    try
    {
        // Only commit/rollback when depth reaches 0 (outermost call)
        if (exception is not null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            _queuedIntegrationEvents.Clear();
        }
        else
        {
            // Commit persists domain data + outbox entries atomically.
            // The background BusOutboxDeliveryService delivers outbox entries to RabbitMQ.
            await _transaction.CommitAsync(cancellationToken);
        }
    }
    finally
    {
        await _transaction.DisposeAsync();
        _transaction = null;
        _transactionDepth = 0;
    }
}
```

**Key changes from the "before" version:**

1. **Integration events publish BEFORE `SaveChangesAsync`** — writes to outbox table, not RabbitMQ
2. **No post-commit publish in `CloseTransactionAsync`** — removed `PublishAndClearIntegrationEventsAsync()` after commit
3. **Removed `PublishEventAsync` try/catch** — no more "database transaction was already committed" error handling
4. **`QueueIntegrationEvent()` API unchanged** — callers don't need to change anything

> **Wolverine Note**: Steps 3 (DbContext changes) and 6 (EF Core migrations) are **not needed** when using Wolverine. Wolverine auto-provisions its own outbox tables in the `wolverine` schema on startup — no DbContext entity configurations or migrations required.

### Step 6: Generate Migration

Create an EF Core migration to add the outbox tables:

```bash
# From project root
dotnet ef migrations add AddOutboxTables \
  -p Core/Scheduling/Scheduling.Infrastructure \
  -s WebApplications/Scheduling.WebApi

dotnet ef migrations add AddOutboxTables \
  -p Core/Billing/Billing.Infrastructure \
  -s WebApplications/Billing.WebApi
```

The migration will add six tables per database (3 per bounded context):
S
- `Scheduling_InboxState`, `Scheduling_OutboxMessage`, `Scheduling_OutboxState`
- `Billing_InboxState`, `Billing_OutboxMessage`, `Billing_OutboxState`

Apply migrations:

```bash
dotnet ef database update \
  -p Core/Scheduling/Scheduling.Infrastructure \
  -s WebApplications/Scheduling.WebApi

dotnet ef database update \
  -p Core/Billing/Billing.Infrastructure \
  -s WebApplications/Billing.WebApi
```

---

## Background Delivery Service

MassTransit automatically registers a hosted service (`BusOutboxDeliveryService`) when you configure the Bus Outbox. You don't need to register it manually.

### How It Works

```
Every QueryDelay interval (5 seconds):
    ↓
┌──────────────────────────────────┐
│ BusOutboxDeliveryService          │
│                                   │
│ 1. Query OutboxMessage table      │
│    WHERE SentTime IS NULL         │
│    LIMIT QueryMessageLimit (100)  │
└──────────┬───────────────────────┘
           ↓
    ┌──────────────────┐
    │ Pending Messages │
    └──────────────────┘
           ↓
    ┌──────────────────────────────┐
    │ For each message:             │
    │   1. Deserialize              │
    │   2. Publish to RabbitMQ      │
    │   3. Mark as sent (or delete) │
    └──────────────────────────────┘
           ↓
    ┌──────────────────┐
    │ Update checkpoint │
    │ LastSequenceNumber│
    └──────────────────┘
```

**On startup:**
- Service starts polling immediately
- Finds any undelivered messages from previous runs
- Delivers them to RabbitMQ
- Continues polling every 5 seconds

**Delivery guarantees:**
- **At-least-once:** A message may be delivered multiple times (if process crashes after publish but before marking as sent)
- **Ordering:** Messages are delivered in sequence number order per outbox
- **Durability:** Messages survive process restarts, server reboots, database failover

### Configuration Options

```csharp
x.AddEntityFrameworkOutbox<TDbContext>(o =>
{
    o.UseSqlServer();
    o.UseBusOutbox();

    // How often to check for pending messages (default: 1 minute)
    o.QueryDelay = TimeSpan.FromSeconds(5);

    // Max messages to fetch per poll (default: 100)
    o.QueryMessageLimit = 100;
});
```

**Tuning recommendations:**

| Scenario | QueryDelay | QueryMessageLimit | Notes |
|----------|-----------|-------------------|-------|
| Low volume | 10-30s | 50-100 | Reduce database load |
| High volume | 1-5s | 100-500 | Lower latency, handle bursts |
| Strict latency SLA | 1-2s | 100 | Near real-time delivery |
| Large backlogs | 5s | 500-1000 | Catch up faster |

---

## Consumer-Side Idempotency (InboxState)

The outbox provides publisher-side reliability (events aren't lost). The **InboxState** table provides consumer-side idempotency (duplicate deliveries are handled gracefully).

### How It Works

When a consumer processes a message:

```
Message delivered to consumer
    ↓
┌──────────────────────────────────┐
│ MassTransit checks InboxState     │
│ WHERE MessageId = @messageId      │
│   AND ConsumerId = @consumerId    │
└────────┬─────────────────────────┘
         │
    ┌────┴────┐
    │ Exists? │
    └────┬────┘
         │
    ┌────┴────────────────┐
    NO                    YES
    │                     │
    ▼                     ▼
┌─────────────┐    ┌─────────────┐
│ Process      │    │ Skip        │
│ message     │    │ (duplicate) │
└──────┬──────┘    └─────────────┘
       │
       ▼
┌─────────────────────┐
│ Record in InboxState │
│ - MessageId         │
│ - ConsumerId        │
│ - Delivered (UTC)   │
└─────────────────────┘
```

**This gives you idempotency FOR FREE** - no need to implement "already processed" checks in your handlers (though you still can for business-specific reasons).

### Example: Duplicate Delivery

```
Scenario: Handler crashes after processing but before acknowledging

1. First delivery:
   - Message arrives (MessageId: abc-123)
   - Handler processes (creates billing profile)
   - Handler saves to database
   - InboxState records MessageId: abc-123 ✅
   - CRASH before ACK sent to RabbitMQ

2. Second delivery (redelivery):
   - Same message arrives (MessageId: abc-123)
   - MassTransit checks InboxState
   - Finds MessageId: abc-123 already processed
   - Skips handler (returns success immediately)
   - ACKs message to RabbitMQ
```

**Without InboxState:** Billing profile created twice (duplicate data).

**With InboxState:** Second delivery is a no-op (idempotent).

### InboxState vs Business-Level Idempotency

InboxState only protects against **redelivery of the same message** (same MessageId). It does NOT protect against **duplicate business operations** — different messages that produce the same outcome.

**Example:** A user calls `POST /api/patients` twice with the same data. That creates two separate commands, two domain events, and two integration events with **different MessageIds**. InboxState sees two unique messages and processes both — resulting in two billing profiles for the same patient.

To guard against that, you still need **business-level idempotency**: a unique constraint on `PatientId` in `BillingProfiles`, or an existence check before creating the profile. See [05-idempotency-error-handling.md](./05-idempotency-error-handling.md) for patterns.

**In short:**
- **InboxState** = infrastructure-level deduplication (same message redelivered)
- **Business idempotency** = domain-level deduplication (different messages, same intent)

### Consumer Outbox Middleware

For InboxState to work, consumers must be configured with the outbox middleware. If you used `AddEntityFrameworkOutbox<TDbContext>`, this is automatic for all consumers registered via MassTransit — including those registered via assembly scanning:

```csharp
// All consumers discovered by assembly scanning get InboxState tracking automatically
configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
```

**InboxState only tracks messages consumed by this bounded context.** Each context has its own `{Context}_InboxState` table tracking what it has processed.

### Inbox Cleanup

MassTransit periodically cleans up old InboxState entries. The `DuplicateDetectionWindow` (default: 30 minutes) controls how long entries are kept. After this window, entries are removed and redelivered messages would be processed again — which is why business-level idempotency (e.g., unique constraints) remains important as a safety net.

---

## Complete Flow Diagram

```
+-----------------------------------------------------------------------------+
|                      TRANSACTIONAL OUTBOX FLOW                               |
+-----------------------------------------------------------------------------+

Command Handler
    |
    v
Entity.Create() -> AddDomainEvent(PatientCreatedEvent)
    |
    v
┌───────────────────────────────────────────────────────────────────────────┐
│ UnitOfWork.SaveChangesAsync()                                              │
│                                                                            │
│ 1. DispatchDomainEventsAsync()                                             │
│    → MediatR publishes PatientCreatedEvent                                 │
│    → PatientCreatedEventHandler receives event                             │
│    → Handler calls: _uow.QueueIntegrationEvent(PatientCreatedIntEvent)     │
│        → QueueIntegrationEvent calls _eventBus.PublishAsync()              │
│        → MassTransit Bus Outbox intercepts and writes to OutboxMessage     │
│                                                                            │
│ 2. _context.SaveChangesAsync()                                             │
│    ┌─────────────────────────────────────────────────────────────────┐   │
│    │ Single Database Transaction                                      │   │
│    │ ✅ INSERT INTO Patients (...)                                    │   │
│    │ ✅ INSERT INTO Scheduling_OutboxMessage (MessageId, Body, ...)   │   │
│    │ ✅ COMMIT                                                         │   │
│    └─────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ No publish to RabbitMQ yet!                                                │
└───────────────────────────────────────────────────────────────────────────┘
    |
    v
┌───────────────────────────────────────────────────────────────────────────┐
│ Background: BusOutboxDeliveryService (runs every 5 seconds)                │
│                                                                            │
│ SELECT * FROM Scheduling_OutboxMessage WHERE SentTime IS NULL              │
│ LIMIT 100                                                                  │
│                                                                            │
│ For each message:                                                          │
│   1. Deserialize message body                                              │
│   2. Publish to RabbitMQ                                                   │
│   3. DELETE FROM Scheduling_OutboxMessage (removed after delivery)         │
│   4. UPDATE Scheduling_OutboxState SET LastSequenceNumber = @last          │
└───────────────────────────────────────────────────────────────────────────┘
    |
    v
RabbitMQ Exchange: PatientCreatedIntegrationEvent
    |
    +-- Queue: billing-patient-created
            |
            v
    ┌───────────────────────────────────────────────┐
    │ Billing.PatientCreatedIntegrationEventHandler │
    │                                                │
    │ 1. MassTransit checks Billing_InboxState       │
    │    for MessageId (consumer-side idempotency)   │
    │                                                │
    │ 2. If not processed:                           │
    │    → Call HandleAsync()                        │
    │    → Create billing profile                    │
    │    → Save changes                              │
    │    → Record in Billing_InboxState              │
    │                                                │
    │ 3. If already processed:                       │
    │    → Skip handler                              │
    │    → ACK message                               │
    └───────────────────────────────────────────────┘
```

---

## Verification Checklist

After implementing the outbox pattern:

### Database Schema
- [ ] Six outbox tables exist in SQL Server (3 per bounded context)
  - `Scheduling_InboxState`, `Scheduling_OutboxMessage`, `Scheduling_OutboxState`
  - `Billing_InboxState`, `Billing_OutboxMessage`, `Billing_OutboxState`

### Functional Testing
- [ ] Create a patient via API
- [ ] Check `Scheduling_OutboxMessage` table (should briefly have a row)
- [ ] Within 5-10 seconds, row is marked as sent or deleted
- [ ] Billing receives `PatientCreatedIntegrationEvent`
- [ ] `Billing_InboxState` contains entry for the MessageId
- [ ] Create same patient again (idempotency test)
- [ ] Second event is processed once (check InboxState for duplicate MessageId)

### Crash Resilience Testing
- [ ] Create a patient, STOP the API before outbox delivery (within 5 seconds)
- [ ] Verify `Scheduling_OutboxMessage` still has pending message (SentTime = NULL)
- [ ] Restart API
- [ ] Background service picks up pending message and delivers it
- [ ] Billing receives the event

### Configuration
- [ ] `QueryDelay` is appropriate for your latency requirements (5-10 seconds typical)
- [ ] `QueryMessageLimit` handles expected burst sizes (100 default)

### Existing Tests
- [ ] All unit tests pass (domain logic unchanged)
- [ ] All integration tests pass (handler behavior unchanged)
- [ ] Test infrastructure may need updates if mocking IEventBus (outbox write is synchronous now)

---

## Troubleshooting

### Messages stuck in outbox

**Symptoms:** OutboxMessage table fills up, messages never delivered

**Possible causes:**
1. Background service not running (check logs for `BusOutboxDeliveryService`)
2. RabbitMQ unreachable (check network, credentials)
3. Serialization errors (check message body, ContentType)

**Fix:**
- Check logs for delivery errors
- Verify RabbitMQ connection in MassTransit configuration
- Inspect OutboxMessage rows for errors

### Duplicate messages on consumer

**Symptoms:** Billing profiles created twice for same patient

**Possible causes:**
1. Consumer not using outbox middleware (InboxState not recording)
2. InboxState cleanup too aggressive (entries deleted before redelivery window)

**Fix:**
- Ensure `AddEntityFrameworkOutbox<TDbContext>` is called
- Increase `DuplicateDetectionWindow` if messages are redelivered after cleanup
- Add business-level idempotency checks (e.g., unique constraint on ExternalPatientId)

### Outbox tables not created

**Symptoms:** Migration doesn't include outbox tables

**Possible causes:**
1. Forgot to call `modelBuilder.AddInboxStateEntity()` etc. in `OnModelCreating`
2. Wrong DbContext type passed to `AddEntityFrameworkOutbox<TDbContext>`

**Fix:**
- Verify DbContext has outbox entity configurations
- Ensure `AddMassTransitEventBus<SchedulingDbContext>` uses the correct DbContext type

---

## Performance Considerations

### Outbox Table Growth

OutboxMessage rows are deleted immediately after successful delivery to the broker. The table should normally be near-empty. If rows accumulate, it means the broker is unreachable or delivery is failing.

**Monitoring:**
```sql
-- Check pending messages (should be near zero under normal conditions)
SELECT COUNT(*) FROM Scheduling_OutboxMessage;
```

**Tuning:**
- Increase `QueryMessageLimit` to handle bursts
- Reduce `QueryDelay` for faster delivery (trade-off: more database polling)

### Database Load

The background service polls every `QueryDelay` seconds. In multi-instance deployments, each instance polls independently.

**Mitigation:**
- Increase `QueryDelay` to reduce polling frequency (trade-off: higher latency)
- Use database read replicas for outbox queries (requires MassTransit configuration)
- Limit concurrent instances (scale horizontally for processing, not outbox polling)

### Message Latency

Outbox adds latency: event must be written to database, then picked up by background service.

**Typical latency:**
- Without outbox: ~10-50ms (direct RabbitMQ publish)
- With outbox: ~5-10 seconds (depends on `QueryDelay`)

**If latency is critical:**
- Reduce `QueryDelay` to 1-2 seconds
- Use in-memory outbox for read models (eventual consistency acceptable)
- Hybrid: critical events bypass outbox (requires custom logic)

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
| **DbContext Changes** | None | Required (3 entity additions) | None |
| **Migration Needed** | No | Yes (EF Core migration) | No (auto-provisioned) |
| **Schema Location** | N/A | Your application schema | Separate `wolverine` schema |
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
