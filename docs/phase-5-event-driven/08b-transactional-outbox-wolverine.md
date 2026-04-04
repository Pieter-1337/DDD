# Transactional Outbox: Wolverine Implementation

> This document is a step-by-step implementation guide. For the conceptual overview of the Transactional Outbox Pattern, see [08 — Transactional Outbox Pattern](./08-transactional-outbox.md).

---

## Overview

Wolverine's outbox integration relies on two key abstractions to hook into the UnitOfWork: `ICommitStrategy` and `IEventBus`. Unlike MassTransit's transparent outbox via `UseBusOutbox()`, Wolverine requires explicit coordination between the event bus and the commit strategy.

The key insight: both `WolverineDbContextEventBus<T>` and `WolverineCommitStrategy<T>` share the same scoped `IDbContextOutbox<T>` instance. Messages published via the EventBus are staged in the same outbox that the strategy flushes during `CloseTransactionAsync()` at the outermost transaction depth.

---

## Implementation Walkthrough

### Step 1: ICommitStrategy Interface

This abstraction allows the UnitOfWork to delegate the transaction commit operation to a strategy. When Wolverine is active, the strategy calls `SaveChangesAndFlushMessagesAsync()` on the outbox at commit time; when MassTransit is active (or no messaging framework is registered), the UnitOfWork falls back to a regular `_transaction.CommitAsync()`.

```csharp
// BuildingBlocks.Application/Interfaces/ICommitStrategy.cs
namespace BuildingBlocks.Application.Interfaces;

public interface ICommitStrategy
{
    Task CommitAsync(CancellationToken ct = default);
}
```

### Step 2: Wolverine Event Bus

The event bus implementation wraps `IDbContextOutbox<TDbContext>` to stage integration events for delivery. When `PublishAsync()` is called, the message is added to the outbox's pending message collection — **not sent to RabbitMQ yet**.

```csharp
// BuildingBlocks.Infrastructure.Wolverine/WolverineDbContextEventBus.cs
internal sealed class WolverineDbContextEventBus<TDbContext> : IEventBus
    where TDbContext : DbContext
{
    private readonly IDbContextOutbox<TDbContext> _outbox;

    public WolverineDbContextEventBus(IDbContextOutbox<TDbContext> outbox)
        => _outbox = outbox;

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class, IIntegrationEvent
        => await _outbox.PublishAsync(@event);
}
```

This is scoped per request. All calls to `PublishAsync()` within a single handler invocation share the same outbox instance.

### Step 3: Wolverine Commit Strategy

The commit strategy is injected into the UnitOfWork and is invoked ONLY at transaction commit time (in `CloseTransactionAsync()` at depth 0). It calls `SaveChangesAndFlushMessagesAsync()` on the outbox, which:

1. Adds outbox envelope entities to the DbContext change tracker
2. Calls `SaveChangesAsync()` — domain entities + outbox messages written atomically
3. Commits the transaction
4. Flushes messages to RabbitMQ after the commit succeeds

```csharp
// BuildingBlocks.Infrastructure.Wolverine/WolverineCommitStrategy.cs
internal sealed class WolverineCommitStrategy<TDbContext> : ICommitStrategy
    where TDbContext : DbContext
{
    private readonly IDbContextOutbox<TDbContext> _outbox;

    public WolverineCommitStrategy(IDbContextOutbox<TDbContext> outbox)
        => _outbox = outbox;

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _outbox.SaveChangesAndFlushMessagesAsync(ct);
        return 0;
    }
}
```

Note that the strategy doesn't return the actual number of entities saved — Wolverine's API doesn't expose this. If your application logic depends on the return value of `SaveChangesAsync()`, this is not affected since the strategy only runs at commit time, not save time.

### Step 4: EfCoreUnitOfWork Integration

The UnitOfWork is modified to accept an optional `ICommitStrategy`. The key architectural points:

1. **`SaveChangesAsync()`** — ALWAYS calls `_context.SaveChangesAsync()` regardless of messaging framework. This persists domain entities and queues integration events to the outbox (MassTransit writes to `OutboxMessage` table, Wolverine buffers in memory).

2. **`CloseTransactionAsync()`** — At transaction depth 0 (outermost transaction close), if a commit strategy is present, it delegates to `_commitStrategy.CommitAsync()`. Otherwise, it falls back to `_transaction.CommitAsync()`.

```csharp
// BuildingBlocks.Infrastructure.EfCore/EfCoreUnitOfWork.cs (key changes only)
public class EfCoreUnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly ICommitStrategy? _commitStrategy;
    // ... other fields ...

    public EfCoreUnitOfWork(
        TContext context,
        IEventBus? eventBus = null,
        ICommitStrategy? commitStrategy = null,  // Optional — injected by Wolverine
        IMediator? mediator = null,
        ILogger<EfCoreUnitOfWork<TContext>>? logger = null)
    {
        // ...
        _commitStrategy = commitStrategy;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await DispatchDomainEventsAsync(cancellationToken);
        await PublishIntegrationEventsToOutboxAsync(cancellationToken);

        // ALWAYS call regular SaveChangesAsync — no delegation to strategy here
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task CloseTransactionAsync(Exception? exception = null, CancellationToken cancellationToken = default)
    {
        // ... depth tracking logic ...

        // Only commit/rollback when depth reaches 0 (outermost call)
        if (exception is not null)
        {
            await _transaction.RollbackAsync(cancellationToken);
        }
        else if (_commitStrategy is not null)
        {
            // Wolverine path: strategy persists outbox + commits transaction + delivers messages
            await _commitStrategy.CommitAsync(cancellationToken);
        }
        else
        {
            // MassTransit path: regular commit persists outbox entries atomically
            await _transaction.CommitAsync(cancellationToken);
        }
    }
}
```

**MassTransit comparison:** With MassTransit, no `ICommitStrategy` is registered. The UnitOfWork falls back to `_transaction.CommitAsync()`, and the MassTransit outbox is completely transparent via `UseBusOutbox()` — it intercepts `IPublishEndpoint.Publish()` at a lower level.

**Why this architecture?** Calling the strategy at commit time (not save time) prevents premature commits at nested transaction depths. The strategy is only invoked when the outermost transaction is ready to commit, ensuring all saves within the transaction are complete before flushing messages.

### Step 5: Extension Method Registration

The `AddWolverineEventBus<TDbContext>()` extension method wires up all the pieces:

```csharp
// BuildingBlocks.Infrastructure.Wolverine/WolverineExtensions.cs (key registrations)
public static IHostApplicationBuilder AddWolverineEventBus<TDbContext>(
    this IHostApplicationBuilder builder,
    string dbConnectionString,
    string schemaName,
    Action<WolverineOptions>? configureWolverine = null)
    where TDbContext : DbContext
{
    // Register outbox-aware event bus and commit strategy
    builder.Services.AddScoped<IEventBus, WolverineDbContextEventBus<TDbContext>>();
    builder.Services.AddScoped<ICommitStrategy, WolverineCommitStrategy<TDbContext>>();
    builder.Services.AddScoped(typeof(IDbContextOutbox<TDbContext>), typeof(DbContextOutbox<TDbContext>));

    builder.UseWolverine(opts =>
    {
        // ... RabbitMQ configuration ...

        // Per-BC schema for outbox tables
        opts.PersistMessagesWithSqlServer(dbConnectionString, schemaName);
        opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;

        // EF Core transaction integration
        opts.UseEntityFrameworkCoreTransactions();

        // ... retry configuration ...
        configureWolverine?.Invoke(opts);
    });

    return builder;
}
```

All three registrations are scoped to the same request scope. This ensures that:
1. `WolverineDbContextEventBus` receives the same outbox instance as `WolverineCommitStrategy`
2. Messages published via `IEventBus.PublishAsync()` are buffered in the same outbox that the strategy flushes at commit time
3. The outbox participates in the same EF Core transaction as the domain entities

### Step 6: DbContext Mapping

Each DbContext that participates in the Wolverine outbox must map the envelope storage tables in `OnModelCreating`. This allows EF Core to include Wolverine's envelope entities in its change tracking and batch operations.

```csharp
// Core/Billing/Billing.Infrastructure/Persistence/BillingDbContext.cs
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);

    // Wolverine envelope storage — per-BC schema
    modelBuilder.MapWolverineEnvelopeStorage("wolverine_billing");
}
```

The schema name (e.g., `wolverine_billing`) must match the `schemaName` parameter passed to `AddWolverineEventBus<TDbContext>()` in Program.cs. Wolverine auto-provisions the actual tables on startup via Weasel — **no EF Core migration is needed** for the envelope tables themselves.

Wolverine creates three tables in the specified schema:

| Table | Purpose |
|-------|---------|
| `wolverine_billing.incoming_envelopes` | Inbox for incoming messages (idempotency) |
| `wolverine_billing.outgoing_envelopes` | Outbox for outgoing messages |
| `wolverine_billing.dead_letters` | Failed messages after all retries |

### Step 7: Program.cs Registration

Wire up the Wolverine event bus in the API host's `Program.cs`:

```csharp
// WebApplications/Billing.WebApi/Program.cs
var connectionString = builder.Configuration.GetConnectionString("SqlServer")
    ?? throw new InvalidOperationException("SqlServer connection string is required");

builder.AddWolverineEventBus<BillingDbContext>(connectionString, "wolverine_billing", opts =>
{
    opts.Discovery.IncludeAssembly(
        typeof(Billing.Infrastructure.ServiceCollectionExtensions).Assembly);
});
```

The `AddWolverineEventBus<TDbContext>()` call:
- Registers `IEventBus` as `WolverineDbContextEventBus<TDbContext>`
- Registers `ICommitStrategy` as `WolverineCommitStrategy<TDbContext>`
- Configures Wolverine with RabbitMQ transport, SQL Server persistence, and EF Core transactions
- Sets up per-BC schema for envelope tables

---

## Atomicity Flow

```
1. TransactionBehavior calls BeginTransactionAsync()
2. Handler runs → modifies entities → queues integration events
3. UnitOfWork.SaveChangesAsync():
   a. DispatchDomainEventsAsync() — MediatR publishes domain events
   b. PublishIntegrationEventsToOutboxAsync():
      → WolverineDbContextEventBus._outbox.PublishAsync() — buffers message in memory
   c. _context.SaveChangesAsync() — persists domain entities ONLY (messages still buffered)
4. TransactionBehavior calls CloseTransactionAsync():
   a. At depth 0 (outermost transaction), calls _commitStrategy.CommitAsync()
   b. WolverineCommitStrategy._outbox.SaveChangesAndFlushMessagesAsync():
      → Adds outbox envelope entities to DbContext
      → Calls SaveChangesAsync() — outbox messages persisted atomically
      → Commits the transaction
      → Flushes messages to RabbitMQ
```

If the process crashes at any point before step 4b completes, both the domain changes and the outbox messages are rolled back. The transaction is still open until the commit strategy completes — no partial state.

**Key difference from MassTransit:** With MassTransit, step 3c writes both domain entities AND outbox entries (via `OutboxMessage` table), and step 4 is just a regular `_transaction.CommitAsync()`. With Wolverine, messages are buffered in memory during step 3, and the commit strategy in step 4 persists + flushes them atomically.

**Why defer to commit time?** This prevents premature commits at nested transaction depths. If a handler calls `SaveChangesAsync()` multiple times (or nested handlers do), the messages are only flushed once, at the outermost transaction commit.

---

## Verification Checklist

After implementing the Wolverine outbox:

### Database Schema
- [ ] Wolverine envelope tables exist in SQL Server (per bounded context schema)
  - `wolverine_billing.incoming_envelopes`
  - `wolverine_billing.outgoing_envelopes`
  - `wolverine_billing.dead_letters`
- [ ] Tables were auto-provisioned on startup (no EF Core migration needed)

### DbContext Mapping
- [ ] `MapWolverineEnvelopeStorage("wolverine_<bc_name>")` is in each participating DbContext's `OnModelCreating`
- [ ] Schema name matches the `schemaName` in `AddWolverineEventBus<TDbContext>()`

### DI Registration
- [ ] `IEventBus` resolves to `WolverineDbContextEventBus<TDbContext>`
- [ ] `ICommitStrategy` resolves to `WolverineCommitStrategy<TDbContext>`
- [ ] Both share the same scoped `IDbContextOutbox<TDbContext>` instance

### Functional Testing
- [ ] Trigger a command that publishes an integration event
- [ ] Verify the event is delivered to RabbitMQ (check RabbitMQ management UI)
- [ ] Verify the consuming bounded context processes the event
- [ ] Check `wolverine_<bc_name>.outgoing_envelopes` (should be empty after delivery)

### Crash Resilience Testing
- [ ] Trigger a command, STOP the API before outbox flush
- [ ] Verify `wolverine_<bc_name>.outgoing_envelopes` has pending message
- [ ] Restart API
- [ ] Wolverine picks up pending message and delivers it

---

> **Previous:** [08a — MassTransit Outbox Implementation](./08a-transactional-outbox-masstransit.md)

> **Back to concepts:** [08 — Transactional Outbox Pattern](./08-transactional-outbox.md)
