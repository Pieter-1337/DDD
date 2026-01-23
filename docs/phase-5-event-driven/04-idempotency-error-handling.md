# Idempotency & Error Handling

## Overview

With at-least-once delivery, messages can be delivered multiple times. This document covers:
- Making consumers idempotent (safe to reprocess)
- Retry strategies
- Dead Letter Queues (DLQ)
- Error handling patterns

---

## Why Idempotency Matters

Messages can be redelivered because:
- Consumer crashes after processing but before acknowledgment
- Network issues during acknowledgment
- Broker failover
- Manual reprocessing from DLQ

```
         Message delivered
               │
               ▼
    ┌──────────────────┐
    │     Consumer     │
    │   processes...   │
    └────────┬─────────┘
             │
             ▼
    ┌──────────────────┐
    │  Database saved  │  ✅
    └────────┬─────────┘
             │
             ▼
    ┌──────────────────┐
    │  Acknowledge     │  💥 Consumer crashes here
    │  message         │
    └──────────────────┘
             │
             ▼
    Message redelivered  ← Same message, again!
```

**Without idempotency:** Duplicate billing profile created, duplicate email sent, etc.

**With idempotency:** Second processing is a no-op.

---

## Idempotency Strategies

### Strategy 1: Check Before Processing

Check if you've already processed this event:

```csharp
public class PatientCreatedConsumer : IConsumer<PatientCreatedIntegrationEvent>
{
    private readonly BillingDbContext _context;

    public async Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
    {
        var message = context.Message;

        // Check if already processed
        var exists = await _context.BillingProfiles
            .AnyAsync(p => p.ExternalPatientId == message.PatientId);

        if (exists)
        {
            _logger.LogInformation(
                "Billing profile already exists for patient {PatientId}, skipping",
                message.PatientId);
            return; // Idempotent - already processed
        }

        // Process normally
        var profile = new BillingProfile
        {
            ExternalPatientId = message.PatientId,
            Email = message.Email,
            FullName = message.FullName
        };

        _context.BillingProfiles.Add(profile);
        await _context.SaveChangesAsync();
    }
}
```

### Strategy 2: Upsert (Insert or Update)

Use database upsert capabilities:

```csharp
public async Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
{
    var message = context.Message;

    // Upsert - insert if not exists, update if exists
    var profile = await _context.BillingProfiles
        .FirstOrDefaultAsync(p => p.ExternalPatientId == message.PatientId);

    if (profile is null)
    {
        profile = new BillingProfile { ExternalPatientId = message.PatientId };
        _context.BillingProfiles.Add(profile);
    }

    // Update properties (works for both insert and update)
    profile.Email = message.Email;
    profile.FullName = message.FullName;

    await _context.SaveChangesAsync();
}
```

### Strategy 3: Processed Events Table

Track which events have been processed:

```csharp
// Table to track processed events
public class ProcessedEvent
{
    public Guid EventId { get; set; }        // From IntegrationEvent.EventId
    public string EventType { get; set; }
    public DateTime ProcessedAt { get; set; }
}

public class PatientCreatedConsumer : IConsumer<PatientCreatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
    {
        var message = context.Message;

        // Check if already processed (by EventId)
        if (await _context.ProcessedEvents.AnyAsync(e => e.EventId == message.EventId))
        {
            _logger.LogInformation("Event {EventId} already processed, skipping", message.EventId);
            return;
        }

        // Process in a transaction
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // 1. Do the work
            var profile = new BillingProfile { ... };
            _context.BillingProfiles.Add(profile);

            // 2. Mark event as processed
            _context.ProcessedEvents.Add(new ProcessedEvent
            {
                EventId = message.EventId,
                EventType = nameof(PatientCreatedIntegrationEvent),
                ProcessedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw; // Will trigger retry
        }
    }
}
```

### Strategy Comparison

| Strategy | Pros | Cons |
|----------|------|------|
| Check before process | Simple | Race condition possible |
| Upsert | Atomic, no race | Only works for updates |
| Processed events table | Reliable, works for any action | Extra table, cleanup needed |

---

## Retry Strategies

### MassTransit Retry Configuration

```csharp
x.UsingRabbitMq((context, cfg) =>
{
    cfg.Host("localhost");

    // Global retry policy
    cfg.UseMessageRetry(r =>
    {
        // Exponential backoff: 1s, 5s, 15s, 30s, 60s
        r.Exponential(
            retryLimit: 5,
            minInterval: TimeSpan.FromSeconds(1),
            maxInterval: TimeSpan.FromMinutes(1),
            intervalDelta: TimeSpan.FromSeconds(5)
        );

        // Or fixed intervals
        r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)
        );

        // Retry only certain exceptions
        r.Handle<TimeoutException>();
        r.Handle<DbUpdateException>();

        // Never retry validation errors
        r.Ignore<ValidationException>();
        r.Ignore<ArgumentException>();
    });

    cfg.ConfigureEndpoints(context);
});
```

### Per-Consumer Retry

```csharp
public class PatientCreatedConsumerDefinition
    : ConsumerDefinition<PatientCreatedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PatientCreatedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // This consumer gets more retries
        endpointConfigurator.UseMessageRetry(r =>
        {
            r.Intervals(
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10)
            );
        });
    }
}
```

---

## Dead Letter Queues

When all retries are exhausted, messages go to the Dead Letter Queue (DLQ).

### How It Works

```
     Message
         │
         ▼
  ┌─────────────┐
  │   Consumer  │
  └──────┬──────┘
         │
    ┌────┴────┐
    │ Failure │
    └────┬────┘
         │
         ▼
  ┌─────────────┐
  │   Retry 1   │ ──> Fail
  │   Retry 2   │ ──> Fail
  │   Retry 3   │ ──> Fail
  └──────┬──────┘
         │ All retries exhausted
         ▼
  ┌─────────────────┐
  │  Dead Letter    │
  │     Queue       │
  │  _error suffix  │
  └─────────────────┘
         │
         ▼
  Manual inspection
  and reprocessing
```

### DLQ Naming in MassTransit

```
Original Queue: billing-patient-created
Error Queue:    billing-patient-created_error
Skip Queue:     billing-patient-created_skipped
```

### Viewing Failed Messages

1. Go to RabbitMQ Management UI: `http://localhost:15672`
2. Navigate to Queues tab
3. Find queues ending in `_error`
4. Click on queue, then "Get messages" to inspect

### Reprocessing from DLQ

Option 1: Manual via RabbitMQ UI
- Move messages from error queue back to main queue

Option 2: Programmatic reprocessing
```csharp
// Move messages from DLQ back to main queue
public async Task ReprocessDeadLetters(string queueName)
{
    var errorQueueName = $"{queueName}_error";
    // Use RabbitMQ admin API to shovel messages
}
```

Option 3: MassTransit fault handling
```csharp
// Handle faults programmatically
public class PatientCreatedFaultConsumer : IConsumer<Fault<PatientCreatedIntegrationEvent>>
{
    public async Task Consume(ConsumeContext<Fault<PatientCreatedIntegrationEvent>> context)
    {
        var faultedMessage = context.Message.Message;
        var exceptions = context.Message.Exceptions;

        _logger.LogError(
            "Failed to process PatientCreated event {EventId}. Errors: {Errors}",
            faultedMessage.EventId,
            string.Join(", ", exceptions.Select(e => e.Message)));

        // Alert, create ticket, etc.
        await _alertService.SendAlert($"Failed event: {faultedMessage.EventId}");
    }
}
```

---

## Error Handling Patterns

### Pattern 1: Let It Fail

Simple approach - let exceptions bubble up:

```csharp
public async Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
{
    // If this throws, MassTransit handles retry + DLQ
    await _billingService.CreateProfile(context.Message);
}
```

**When to use:** Most cases. MassTransit's retry + DLQ is usually enough.

### Pattern 2: Graceful Degradation

Handle expected errors, let unexpected ones fail:

```csharp
public async Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
{
    try
    {
        await _billingService.CreateProfile(context.Message);
    }
    catch (DuplicateProfileException)
    {
        // Expected - already processed (idempotency)
        _logger.LogInformation("Profile already exists, skipping");
        return;
    }
    // Other exceptions bubble up -> retry -> DLQ
}
```

### Pattern 3: Circuit Breaker

Prevent cascading failures:

```csharp
endpointConfigurator.UseCircuitBreaker(cb =>
{
    cb.TrackingPeriod = TimeSpan.FromMinutes(1);
    cb.TripThreshold = 15;      // Trip after 15 failures
    cb.ActiveThreshold = 10;    // in the last 10 messages
    cb.ResetInterval = TimeSpan.FromMinutes(5);
});
```

```
         Normal operation
               │
               ▼
  ┌─────────────────────────┐
  │   Failures < Threshold  │───> Keep processing
  └─────────────────────────┘
               │
    Failures >= Threshold
               │
               ▼
  ┌─────────────────────────┐
  │   Circuit OPEN          │───> Reject messages (fail fast)
  └─────────────────────────┘
               │
    After ResetInterval
               │
               ▼
  ┌─────────────────────────┐
  │   Circuit HALF-OPEN     │───> Try one message
  └─────────────────────────┘
               │
      ┌────────┴────────┐
      │                 │
   Success           Failure
      │                 │
      ▼                 ▼
   CLOSED            OPEN
```

---

## Transient vs Permanent Failures

| Type | Examples | Action |
|------|----------|--------|
| **Transient** | Timeout, connection lost, DB busy | Retry |
| **Permanent** | Invalid data, business rule violation | DLQ immediately |

```csharp
cfg.UseMessageRetry(r =>
{
    r.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

    // Retry transient failures
    r.Handle<TimeoutException>();
    r.Handle<SqlException>(ex => ex.IsTransient);

    // Don't retry permanent failures
    r.Ignore<ValidationException>();
    r.Ignore<InvalidOperationException>();
});
```

---

## Monitoring & Alerting

### Logging

```csharp
public async Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
{
    using var scope = _logger.BeginScope(new Dictionary<string, object>
    {
        ["EventId"] = context.Message.EventId,
        ["CorrelationId"] = context.CorrelationId,
        ["PatientId"] = context.Message.PatientId
    });

    _logger.LogInformation("Processing PatientCreated event");

    try
    {
        await ProcessAsync(context.Message);
        _logger.LogInformation("Successfully processed PatientCreated event");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to process PatientCreated event");
        throw;
    }
}
```

### Metrics

```csharp
// Track processing metrics
services.AddSingleton<IConsumeObserver, MetricsConsumeObserver>();

public class MetricsConsumeObserver : IConsumeObserver
{
    public async Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception)
    {
        _metrics.IncrementCounter("message_consume_failed", new Tags
        {
            { "message_type", typeof(T).Name },
            { "exception_type", exception.GetType().Name }
        });
    }
}
```

---

## Verification Checklist

- [ ] Consumers are idempotent (choose a strategy)
- [ ] Retry policy configured (global or per-consumer)
- [ ] Transient exceptions are retried
- [ ] Permanent exceptions go to DLQ immediately
- [ ] Error queues visible in RabbitMQ Management UI
- [ ] Fault consumers for alerting (optional)
- [ ] Logging includes EventId and CorrelationId
- [ ] Metrics tracking (optional)

---

## Quick Reference

```csharp
// Idempotent consumer template
public class MyEventConsumer : IConsumer<MyIntegrationEvent>
{
    public async Task Consume(ConsumeContext<MyIntegrationEvent> context)
    {
        var message = context.Message;

        // 1. Check idempotency
        if (await AlreadyProcessed(message.EventId))
        {
            _logger.LogInformation("Event {EventId} already processed", message.EventId);
            return;
        }

        // 2. Process
        await DoWork(message);

        // 3. Mark as processed (if using processed events table)
        await MarkAsProcessed(message.EventId);
    }
}
```

---

> Next: [05-sagas-orchestration.md](./05-sagas-orchestration.md) - Coordinating multi-step processes
