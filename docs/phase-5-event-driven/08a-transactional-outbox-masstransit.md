# Transactional Outbox: MassTransit Implementation

> This document is a step-by-step implementation guide. For the conceptual overview of the Transactional Outbox Pattern, see [08 — Transactional Outbox Pattern](./08-transactional-outbox.md).

---

## Implementation Walkthrough

The following steps show how to implement the transactional outbox with MassTransit. For the Wolverine implementation, see [08b — Wolverine Outbox Implementation](./08b-transactional-outbox-wolverine.md).

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

### Step 5: Update UnitOfWork

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


### Step 6: Generate Migration

Create an EF Core migration to add the outbox tables:

```bash
# From project root
dotnet ef migrations add AddOutboxTables \
  -p Core/Scheduling/Scheduling.Infrastructure \
  -s WebApplications/Scheduling.WebApi
```

The migration will add six tables per database (3 per bounded context):

- `Scheduling_InboxState`, `Scheduling_OutboxMessage`, `Scheduling_OutboxState`

Apply migrations:

```bash
dotnet ef database update \
  -p Core/Scheduling/Scheduling.Infrastructure \
  -s WebApplications/Scheduling.WebApi
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

> **Next:** [08b — Wolverine Outbox Implementation](./08b-transactional-outbox-wolverine.md)

> **Back to concepts:** [08 — Transactional Outbox Pattern](./08-transactional-outbox.md)
