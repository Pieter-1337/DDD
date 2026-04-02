# Event Versioning & Schema Evolution

## Overview

Integration events are contracts between services. When you change an event, you risk breaking consumers. This document covers:
- Why versioning matters
- Backwards compatibility rules
- Versioning strategies
- MassTransit message versioning

> **Framework Note**: The event versioning strategies described in this document (schema evolution, upcasting, versioned event types) are framework-agnostic and apply equally to both MassTransit and Wolverine. Both frameworks serialize events as JSON and support the same versioning patterns at the message contract level.

---

## The Problem

Producer and consumers evolve independently:

```
Week 1:
┌────────────────┐         ┌────────────────┐
│   Producer     │  v1     │   Consumer A   │
│   (Scheduling) │ ──────> │   (Billing)    │
└────────────────┘         └────────────────┘

Week 3: Producer updates event, Consumer hasn't
┌────────────────┐         ┌────────────────┐
│   Producer     │  v2     │   Consumer A   │  💥 Deserialize fails!
│   (Scheduling) │ ──────> │   (still v1)   │
└────────────────┘         └────────────────┘
```

You can't update all consumers simultaneously in a distributed system.

---

## Backwards Compatibility Rules

### Safe Changes (Non-Breaking)

| Change | Safe? | Why |
|--------|-------|-----|
| Add optional field | ✅ | Old consumers ignore it |
| Add field with default | ✅ | Old consumers get default |
| Remove unused field | ✅ | Old consumers ignore extras |
| Widen type (int → long) | ⚠️ | Depends on serializer |

### Breaking Changes

| Change | Breaking? | Why |
|--------|-----------|-----|
| Remove required field | ❌ | Old consumers expect it |
| Rename field | ❌ | Deserialization fails |
| Change field type | ❌ | Deserialization fails |
| Change semantics | ❌ | Logic breaks |

---

## Strategy 1: Additive Changes Only

The simplest strategy - only add, never remove or rename:

```csharp
// Version 1
public record PatientCreatedIntegrationEvent : IntegrationEvent
{
    public Guid PatientId { get; init; }
    public string Email { get; init; }
}

// Version 2 - ADD new field, keep old ones
public record PatientCreatedIntegrationEvent : IntegrationEvent
{
    public Guid PatientId { get; init; }
    public string Email { get; init; }

    // NEW: Added phone number (nullable for backwards compat)
    public string? PhoneNumber { get; init; }
}

// Version 3 - ADD more fields
public record PatientCreatedIntegrationEvent : IntegrationEvent
{
    public Guid PatientId { get; init; }
    public string Email { get; init; }
    public string? PhoneNumber { get; init; }

    // NEW: Split name into parts
    public string? FirstName { get; init; }
    public string? LastName { get; init; }

    // Keep FullName for old consumers
    public string FullName { get; init; }
}
```

**Pros:** Simple, no coordination needed
**Cons:** Events grow over time, deprecated fields linger

---

## Strategy 2: Explicit Versioning

Create new event types for breaking changes:

```csharp
// Original event
public record PatientCreatedIntegrationEvent : IntegrationEvent
{
    public Guid PatientId { get; init; }
    public string Email { get; init; }
    public string FullName { get; init; }  // Will be deprecated
}

// New version with breaking change
public record PatientCreatedIntegrationEventV2 : IntegrationEvent
{
    public Guid PatientId { get; init; }
    public string Email { get; init; }
    public string FirstName { get; init; }  // Breaking: split from FullName
    public string LastName { get; init; }
}
```

### Migration Path

```
Phase 1: Publish both versions
┌────────────────┐         ┌────────────────┐
│   Producer     │ ──v1──> │ Consumer A(v1) │
│                │ ──v2──> │                │ (ignores v2)
└────────────────┘         └────────────────┘

Phase 2: Consumers migrate
┌────────────────┐         ┌────────────────┐
│   Producer     │ ──v1──> │ Consumer A     │
│                │ ──v2──> │ (now handles   │
└────────────────┘         │ both v1 & v2)  │
                           └────────────────┘

Phase 3: Stop publishing old version
┌────────────────┐         ┌────────────────┐
│   Producer     │ ──v2──> │ Consumer A(v2) │
└────────────────┘         └────────────────┘
```

### Publisher Publishes Both

```csharp
public async Task Handle(PatientCreatedEvent notification, CancellationToken ct)
{
    // Publish v1 (for old consumers)
    await _publishEndpoint.Publish(new PatientCreatedIntegrationEvent
    {
        PatientId = notification.PatientId,
        Email = notification.Email,
        FullName = $"{notification.FirstName} {notification.LastName}"
    }, ct);

    // Publish v2 (for new consumers)
    await _publishEndpoint.Publish(new PatientCreatedIntegrationEventV2
    {
        PatientId = notification.PatientId,
        Email = notification.Email,
        FirstName = notification.FirstName,
        LastName = notification.LastName
    }, ct);
}
```

### Handler Handles Both

```csharp
// Handles v1 (base class provides logging)
public class PatientCreatedIntegrationEventHandler
    : IntegrationEventHandler<PatientCreatedIntegrationEvent>
{
    public PatientCreatedIntegrationEventHandler(
        ILogger<PatientCreatedIntegrationEventHandler> logger) : base(logger) { }

    protected override async Task HandleAsync(
        PatientCreatedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        var (firstName, lastName) = ParseFullName(message.FullName);
        await CreateProfile(message.PatientId, firstName, lastName, cancellationToken);
    }
}

// Handles v2 (base class provides logging)
public class PatientCreatedIntegrationEventV2Handler
    : IntegrationEventHandler<PatientCreatedIntegrationEventV2>
{
    public PatientCreatedIntegrationEventV2Handler(
        ILogger<PatientCreatedIntegrationEventV2Handler> logger) : base(logger) { }

    protected override async Task HandleAsync(
        PatientCreatedIntegrationEventV2 message,
        CancellationToken cancellationToken)
    {
        await CreateProfile(
            message.PatientId,
            message.FirstName,
            message.LastName,
            cancellationToken);
    }
}
```

---

## Strategy 3: Message Wrapper with Version

Embed version info in the message:

```csharp
public record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }

    // Version info
    public int Version { get; init; } = 1;
}

public record PatientCreatedIntegrationEvent : IntegrationEvent
{
    // Common fields (all versions)
    public Guid PatientId { get; init; }
    public string Email { get; init; }

    // V1 fields
    public string? FullName { get; init; }

    // V2 fields
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}
```

Handler checks version:

```csharp
public class PatientCreatedIntegrationEventHandler
    : IntegrationEventHandler<PatientCreatedIntegrationEvent>
{
    public PatientCreatedIntegrationEventHandler(
        ILogger<PatientCreatedIntegrationEventHandler> logger) : base(logger) { }

    protected override async Task HandleAsync(
        PatientCreatedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        string firstName, lastName;

        if (message.Version >= 2)
        {
            firstName = message.FirstName!;
            lastName = message.LastName!;
        }
        else
        {
            (firstName, lastName) = ParseFullName(message.FullName!);
        }

        await CreateProfile(message.PatientId, firstName, lastName, cancellationToken);
    }
}
```

---

## MassTransit Polymorphism

MassTransit supports interface-based message contracts:

```csharp
// Interface (contract)
public interface IPatientCreated
{
    Guid PatientId { get; }
    string Email { get; }
}

// V1 implementation
public record PatientCreatedV1 : IPatientCreated, IntegrationEvent
{
    public Guid PatientId { get; init; }
    public string Email { get; init; }
    public string FullName { get; init; }
}

// V2 implementation
public record PatientCreatedV2 : IPatientCreated, IntegrationEvent
{
    public Guid PatientId { get; init; }
    public string Email { get; init; }
    public string FirstName { get; init; }
    public string LastName { get; init; }
}

// Handler handles any implementation via interface
// Note: For interface-based consumers, use IConsumer<T> directly
// IntegrationEventHandler<T> is for concrete event types
public class PatientCreatedInterfaceHandler : IConsumer<IPatientCreated>
{
    private readonly ILogger<PatientCreatedInterfaceHandler> _logger;

    public PatientCreatedInterfaceHandler(
        ILogger<PatientCreatedInterfaceHandler> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IPatientCreated> context)
    {
        _logger.LogInformation(
            "Handling IPatientCreated for patient {PatientId}",
            context.Message.PatientId);

        // Works with V1 or V2
        await CreateProfile(context.Message.PatientId, context.Message.Email);
    }
}
```

---

## Schema Registry (Advanced)

For large systems, consider a schema registry:

```
┌─────────────────────────────────────────────────────────┐
│                   Schema Registry                        │
│                                                         │
│  PatientCreatedEvent                                    │
│  ├── v1: { patientId, email, fullName }                │
│  ├── v2: { patientId, email, firstName, lastName }     │
│  └── v3: { patientId, email, firstName, lastName, ... }│
│                                                         │
│  Compatibility rules: BACKWARD, FORWARD, FULL          │
└─────────────────────────────────────────────────────────┘
```

Options:
- **Confluent Schema Registry** (with Kafka)
- **AWS Glue Schema Registry**
- **Azure Schema Registry**
- **Custom solution** with a database

For this learning project, additive changes + explicit versioning is sufficient.

---

## Deprecation Process

When you want to remove support for an old version:

```
Timeline:
│
├── Week 1: Announce deprecation of v1
│           Publish: v1 + v2
│           Consume: v1 + v2
│
├── Week 4: Monitor v1 usage
│           Confirm all consumers migrated
│
├── Week 6: Stop publishing v1
│           Publish: v2 only
│           Consume: v2 (remove v1 handler)
│
└── Week 8: Remove v1 event definition
            Clean up code
```

### Logging for Deprecation Monitoring

```csharp
public class PatientCreatedV1IntegrationEventHandler
    : IntegrationEventHandler<PatientCreatedIntegrationEvent>
{
    public PatientCreatedV1IntegrationEventHandler(
        ILogger<PatientCreatedV1IntegrationEventHandler> logger) : base(logger) { }

    protected override async Task HandleAsync(
        PatientCreatedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        // Log deprecation warning (in addition to base class logging)
        Logger.LogWarning(
            "Received deprecated PatientCreatedIntegrationEvent v1 for EventId {EventId}. " +
            "Please migrate to v2.",
            message.EventId);

        // Process anyway...
        await ProcessV1(message, cancellationToken);
    }
}
```

---

## Best Practices

### 1. Design for Evolution

```csharp
// Bad: Will need breaking change to add last name
public record PatientCreated
{
    public string Name { get; init; }
}

// Good: Structured from start
public record PatientCreated
{
    public string FirstName { get; init; }
    public string LastName { get; init; }
}
```

### 2. Use Nullable for New Fields

```csharp
// Adding a field? Make it nullable
public record PatientCreated
{
    public Guid PatientId { get; init; }
    public string Email { get; init; }
    public string? PhoneNumber { get; init; }  // New, nullable
}
```

### 3. Never Rename, Add Instead

```csharp
// Bad: Rename breaks consumers
public record PatientCreated
{
    public string EmailAddress { get; init; }  // Was: Email
}

// Good: Add new, keep old
public record PatientCreated
{
    public string Email { get; init; }         // Keep for compatibility
    public string? EmailAddress { get; init; } // New preferred name
}
```

### 4. Document Breaking Changes

```csharp
/// <summary>
/// Patient created integration event.
/// </summary>
/// <remarks>
/// Version history:
/// - V1 (2024-01): Initial version with FullName
/// - V2 (2024-03): Split FullName into FirstName/LastName
///                 FullName deprecated, will be removed 2024-06
/// </remarks>
public record PatientCreatedIntegrationEvent { }
```

---

## Verification Checklist

- [ ] Understand safe vs breaking changes
- [ ] Choose a versioning strategy
- [ ] New fields are nullable or have defaults
- [ ] Breaking changes use new event types (v2)
- [ ] Deprecation process documented
- [ ] Handlers handle multiple versions (during migration)
- [ ] Logging for deprecated event usage

---

## Quick Reference

```
┌────────────────────────────────────────────────────────────────┐
│                    VERSIONING DECISION TREE                    │
└────────────────────────────────────────────────────────────────┘

Is the change backwards compatible?
│
├── YES (add optional field, add field with default)
│   └── Just change the event, no versioning needed
│
└── NO (remove field, rename, change type)
    │
    ├── Can you add instead of change?
    │   └── YES: Add new field, keep old, deprecate later
    │
    └── Must break?
        └── Create v2 event type
            1. Publish v1 + v2
            2. Migrate consumers
            3. Stop publishing v1
            4. Remove v1
```

---

## Summary

| Strategy | When to Use |
|----------|-------------|
| Additive only | Simple changes, small team |
| Explicit versioning (V2) | Breaking changes needed |
| Interface contracts | Multiple implementations |
| Schema registry | Large enterprise, many teams |

For this learning project:
1. Start with **additive changes**
2. Use **explicit V2 events** when you must break
3. Consider schema registry later for production

---

> This concludes Phase 5 documentation. Return to [01-event-driven-overview.md](./01-event-driven-overview.md) for the overview.
