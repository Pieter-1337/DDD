---
name: 'ddd-integration-event'
description: >
  Publish a cross-bounded-context integration event and consume it in another context.
  Define the event as a record : IntegrationEventBase in Shared/IntegrationEvents/<Context>,
  bridge to it from a domain-event handler via IUnitOfWork.QueueIntegrationEvent (written to
  the transactional outbox in EfCoreUnitOfWork.SaveChangesAsync), and write a consumer in the
  consuming context's Infrastructure/Consumers for BOTH MassTransit (IntegrationEventHandler<T>
  base) and Wolverine (plain Handle method). Register consumers in the consuming WebApi's
  Program.cs. Use for Phase 5/6 event-driven communication between contexts.
paths: Core/**, Shared/**, WebApplications/**
---

# DDD Integration Event

Wires one bounded context's domain event to a reaction in another context, over the message
broker, via the transactional outbox. The canonical flow is **Scheduling → Billing**:
`PatientCreatedEvent` (domain) → `PatientCreatedEventHandler` queues `PatientCreatedIntegrationEvent`
→ outbox → Billing consumer dispatches `CreateBillingProfileCommand`.

Three moving parts: **(A)** the event contract in `Shared`, **(B)** the publish bridge in the
producing context, **(C)** the consumer + registration in the consuming context.

## How it flows (read this first)

1. A domain event handler in the **producing** context calls `IUnitOfWork.QueueIntegrationEvent(...)`
   (it does *not* publish to the broker directly).
2. `EfCoreUnitOfWork.SaveChangesAsync` → `PublishIntegrationEventsToOutboxAsync` writes each queued
   event to the **outbox** (MassTransit: `OutboxMessage` table; Wolverine: outbox buffer), in the same
   transaction as the domain data. On rollback the queue is discarded — no phantom messages.
3. The transaction commit (or the Wolverine `ICommitStrategy`) persists data + outbox atomically; a
   background delivery service ships the message to the broker.
4. The **consuming** context's consumer receives it and typically dispatches a command via `IMediator.Send`.

**Same-framework requirement:** producer and consumer must use the same messaging framework end-to-end
for a given flow (see `docs/` framework × broker matrix and the interop fail-fast guard). The repo's
default is configurable per WebApi via `MessagingFramework` (`"Wolverine"` default, else MassTransit);
`MessageBroker` selects RabbitMQ vs Azure Service Bus via `BrokerSelector`. Write **both** consumer
shapes (below) so the flow works whichever framework is selected.

## Part A — Define the contract (Shared)

`Shared/IntegrationEvents/<ProducingContext>/<Event>IntegrationEvent.cs`:

```csharp
using BuildingBlocks.Application.Messaging;

namespace IntegrationEvents.<ProducingContext>;

/// <summary>
/// Public cross-bounded-context contract. Other contexts consume this.
/// </summary>
public record <Aggregate>CreatedIntegrationEvent(
    Guid <Aggregate>Id,
    string FirstName,
    string LastName,
    string Email,
    DateTime DateOfBirth
) : IntegrationEventBase;
```

- `IntegrationEventBase` (`BuildingBlocks.Application.Messaging`) supplies `Guid EventId` + `DateTime OccurredOn`.
- This is a **published contract** — keep the payload flat and stable. Treat schema changes as versioning
  events (add fields nullable/additive; don't repurpose). The namespace is `IntegrationEvents.<Context>`
  (the `Shared/IntegrationEvents` project), shared by producer and consumer.

## Part B — Bridge from a domain-event handler (producing context)

In the producing context's domain-event handler, inject `IUnitOfWork` and queue the integration event.
Mirror `PatientCreatedEventHandler` (`Core/Scheduling/Scheduling.Application/Patients/EventHandlers/`):

```csharp
using BuildingBlocks.Application.Interfaces;
using IntegrationEvents.<ProducingContext>;
using MediatR;
using Microsoft.Extensions.Logging;
using <ProducingContext>.Domain.<Aggregates>.Events;

namespace <ProducingContext>.Application.<Aggregates>.EventHandlers;

internal class <Aggregate>CreatedEventHandler : INotificationHandler<<Aggregate>CreatedEvent>
{
    private readonly ILogger<<Aggregate>CreatedEventHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public <Aggregate>CreatedEventHandler(ILogger<<Aggregate>CreatedEventHandler> logger, IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public Task Handle(<Aggregate>CreatedEvent notification, CancellationToken cancellationToken)
    {
        _unitOfWork.QueueIntegrationEvent(new <Aggregate>CreatedIntegrationEvent(
            notification.<Aggregate>Id,
            notification.FirstName,
            notification.LastName,
            notification.Email,
            notification.DateOfBirth));

        return Task.CompletedTask;
    }
}
```

If the producing aggregate/domain event doesn't exist yet, run **ddd-domain-event** first.

The producing WebApi must register an event bus so the outbox exists. `Scheduling.WebApi` uses MassTransit
(`AddMassTransitEventBus<SchedulingDbContext>`); a Wolverine producer uses `AddWolverineEventBus<TContext>`.

## Part C — Consume it (consuming context)

Write **both** consumer shapes under `Core/<ConsumingContext>/<ConsumingContext>.Infrastructure/Consumers/`.

### MassTransit consumer — `Consumers/MassTransit/<Event>Handler.cs`

Derive from `IntegrationEventHandler<TEvent>` (`BuildingBlocks.Infrastructure.MassTransit`), which is an
`IConsumer<TEvent>` that wraps `HandleAsync` with start/complete/error logging:

```csharp
using BuildingBlocks.Infrastructure.MassTransit;
using IntegrationEvents.<ProducingContext>;
using MediatR;
using Microsoft.Extensions.Logging;
using <ConsumingContext>.Application.<Aggregates>.Commands;

namespace <ConsumingContext>.Infrastructure.Consumers.MassTransit;

public class <Aggregate>CreatedIntegrationEventHandler
    : IntegrationEventHandler<<Aggregate>CreatedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public <Aggregate>CreatedIntegrationEventHandler(
        IMediator mediator,
        ILogger<<Aggregate>CreatedIntegrationEventHandler> logger) : base(logger)
    {
        _mediator = mediator;
    }

    protected override async Task HandleAsync(
        <Aggregate>CreatedIntegrationEvent message, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Reacting to {Aggregate} {Id}", nameof(<Aggregate>), message.<Aggregate>Id);

        var command = new Create<Thing>Command(new Create<Thing>Request { /* map from message */ });
        await _mediator.Send(command, cancellationToken);
    }
}
```

### Wolverine consumer — `Consumers/Wolverine/<Event>Handler.cs`

A plain class with a public `Handle` method; Wolverine injects dependencies as method parameters:

```csharp
using IntegrationEvents.<ProducingContext>;
using MediatR;
using Microsoft.Extensions.Logging;
using <ConsumingContext>.Application.<Aggregates>.Commands;

namespace <ConsumingContext>.Infrastructure.Consumers.Wolverine;

public class <Aggregate>CreatedIntegrationEventHandler
{
    public async Task Handle(
        <Aggregate>CreatedIntegrationEvent message,
        IMediator mediator,
        ILogger<<Aggregate>CreatedIntegrationEventHandler> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Reacting to {Aggregate} {Id}", nameof(<Aggregate>), message.<Aggregate>Id);

        var command = new Create<Thing>Command(new Create<Thing>Request { /* map from message */ });
        await mediator.Send(command, cancellationToken);
    }
}
```

**Idempotency is your responsibility.** Delivery is at-least-once — the same event may arrive twice.
Guard the command/handler (e.g. check existence by natural key before creating) so reprocessing is safe.
Failures bubble up and hit the configured retry/dead-letter policy.

## Part D — Register the consumer (consuming WebApi `Program.cs`)

Mirror `Billing.WebApi/Program.cs`. The framework switch determines which consumer shape is active:

```csharp
var messagingFramework = builder.Configuration.GetValue<string>("MessagingFramework") ?? "Wolverine";

if (messagingFramework == "Wolverine")
{
    builder.AddWolverineEventBus<<ConsumingContext>DbContext>(connectionString, "wolverine_<context>", opts =>
    {
        opts.Discovery.IncludeAssembly(typeof(<ConsumingContext>.Infrastructure.ServiceCollectionExtensions).Assembly);
        // One ListenToMassTransitQueue per consumed event (interop with MassTransit-shaped queues):
        opts.ListenToMassTransitQueue<<Aggregate>CreatedIntegrationEvent>("<context>-<aggregate>-created");
    });
}
else
{
    builder.Services.AddMassTransitEventBus<<ConsumingContext>DbContext>(builder.Configuration, configure =>
    {
        // Scans the Infrastructure assembly for IConsumer<T> (your MassTransit handler):
        configure.AddConsumers(typeof(<ConsumingContext>.Infrastructure.ServiceCollectionExtensions).Assembly);
    });
}
```

- MassTransit discovers `IntegrationEventHandler<T>` via `AddConsumers(...assembly)`.
- Wolverine discovers the plain `Handle` method via `Discovery.IncludeAssembly(...)`; add a
  `ListenToMassTransitQueue<TEvent>("queue-name")` line per event so the queue/topology is bound.

## Step-by-step
1. **Define** the contract in `Shared/IntegrationEvents/<ProducingContext>` (Part A).
2. **Bridge** from the producing domain-event handler with `QueueIntegrationEvent` (Part B; run `ddd-domain-event` first if needed).
3. **Consume** — write both MassTransit and Wolverine handlers in the consuming context (Part C).
4. **Register** the consumer in the consuming WebApi's `Program.cs` (Part D).
5. **Make it idempotent** — guard the dispatched command against duplicate delivery.

## Verify

```
dotnet build
```

Then run end-to-end under Aspire (which starts the broker): trigger the producing action (e.g. create a
patient) and confirm the consuming context reacts (e.g. a billing profile appears). Check the producer's
`OutboxMessage` table drains and the consumer logs the "Handling … / Handled …" pair. Test the duplicate-delivery
path once to confirm idempotency. Keep producer and consumer on the **same** `MessagingFramework` for the flow.
