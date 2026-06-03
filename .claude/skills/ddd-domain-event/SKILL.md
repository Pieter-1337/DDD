---
name: 'ddd-domain-event'
description: >
  Add a domain event to an aggregate in a bounded context (Core/<Context>/).
  Define the event as a record implementing IDomainEvent (in
  <Context>.Domain/<Aggregates>/Events), raise it from a static Create factory or
  a named mutator on the aggregate via AddDomainEvent, and write an
  INotificationHandler<TEvent> in <Context>.Application/<Aggregates>/EventHandlers.
  Events dispatch in-process through MediatR inside EfCoreUnitOfWork.SaveChangesAsync
  before the DB save. Use when an aggregate state change needs an in-context reaction
  (or to bridge to an integration event — see ddd-integration-event).
paths: Core/**
---

# DDD Domain Event

Adds a domain event + its handler to an existing aggregate. A domain event records
*something that happened inside this bounded context*; handlers react in-process. The
canonical example is `PatientCreatedEvent` (`Core/Scheduling/Scheduling.Domain/Patients/Events/`)
and `PatientCreatedEventHandler` (`Core/Scheduling/Scheduling.Application/Patients/EventHandlers/`).

## How dispatch works (read this first)

Domain events are **not** dispatched by an EF interceptor. `EfCoreUnitOfWork<TContext>.SaveChangesAsync`
(`BuildingBlocks/BuildingBlocks.Infrastructure.EfCore/EfCoreUnitOfWork.cs`) does it explicitly:

1. `DispatchDomainEventsAsync` — scans the `ChangeTracker` for entities implementing `IHasDomainEvents`,
   collects their events, **clears them** (prevents re-processing), then `await _mediator.Publish(domainEvent)` for each.
2. This runs **BEFORE** `_context.SaveChangesAsync()`, so handlers can still modify tracked state or queue integration events.
3. Then integration events queued during step 1 are written to the outbox.

Consequences:
- Handlers run **synchronously, in the same transaction**, before the DB commit. A throwing handler aborts the save.
- Events only fire if the aggregate is tracked by the context at save time (it is, when added/modified via a repository).
- Multiple `INotificationHandler<T>` for the same event all run (MediatR fan-out). Order is not guaranteed.

## Conventions

- **Event**: a `record` implementing `IDomainEvent` (`BuildingBlocks.Domain.Events`, which extends MediatR's `INotification`).
  Carries `DateTime OccurredOn { get; } = DateTime.UtcNow;` and a flat payload of primitives/IDs — never entity references.
  Past tense, named `<Aggregate><PastTenseVerb>Event`.
- **Location**: `Core/<Context>/<Context>.Domain/<Aggregates>/Events/<Event>.cs`.
- **Raising**: from inside the aggregate (the `Entity` base exposes `protected void AddDomainEvent(IDomainEvent)`).
  Raise in the static `Create` factory (creation) or a named mutator (state change) — never from outside the aggregate.
- **Handler**: `internal class <Event>Handler : INotificationHandler<<Event>>` in
  `Core/<Context>/<Context>.Application/<Aggregates>/EventHandlers/`. `internal` is fine — MediatR's
  assembly scan registers it. Inject `ILogger<>` and whatever it needs (`IUnitOfWork`, other services).
- Keep handlers idempotent-friendly and side-effect-light; cross-aggregate writes go through `IUnitOfWork`.

## Step 1 — Clarify
- Which aggregate, which bounded context.
- What just happened (creation vs a specific state change)? → which factory/mutator raises it.
- What should react? In-context side effect (logging, another aggregate) and/or a cross-BC integration event (then also run `ddd-integration-event`).
- Payload: which IDs/values the handler needs (flat, no entity refs).

## Step 2 — Define the event

`Core/<Context>/<Context>.Domain/<Aggregates>/Events/<Aggregate>SuspendedEvent.cs`:

```csharp
using BuildingBlocks.Domain.Events;

namespace <Context>.Domain.<Aggregates>.Events;

public record <Aggregate>SuspendedEvent(
    Guid <Aggregate>Id,
    string Reason) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
```

## Step 3 — Raise it from the aggregate

In `<Context>.Domain/<Aggregates>/<Aggregate>.cs`, raise from the relevant mutator (mirrors `Patient.Suspend`):

```csharp
public void Suspend(string reason = "")
{
    if (Status == <Aggregate>Status.Suspended)
        return; // guard: don't raise on a no-op

    Status = <Aggregate>Status.Suspended;

    AddDomainEvent(new <Aggregate>SuspendedEvent(Id, reason));
}
```

For creation events, raise inside the static `Create` factory after building the instance (see `Patient.Create`).

## Step 4 — Write the handler

`Core/<Context>/<Context>.Application/<Aggregates>/EventHandlers/<Aggregate>SuspendedEventHandler.cs`:

```csharp
using BuildingBlocks.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using <Context>.Domain.<Aggregates>.Events;

namespace <Context>.Application.<Aggregates>.EventHandlers;

internal class <Aggregate>SuspendedEventHandler : INotificationHandler<<Aggregate>SuspendedEvent>
{
    private readonly ILogger<<Aggregate>SuspendedEventHandler> _logger;

    public <Aggregate>SuspendedEventHandler(ILogger<<Aggregate>SuspendedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(<Aggregate>SuspendedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("<Aggregate> {Id} suspended: {Reason}",
            notification.<Aggregate>Id, notification.Reason);

        return Task.CompletedTask;
    }
}
```

To bridge to another bounded context, inject `IUnitOfWork` and call `_unitOfWork.QueueIntegrationEvent(...)`
here — exactly as `PatientCreatedEventHandler` does. See **ddd-integration-event**.

## Step 5 — Test

Use **ddd-backend-unit-test**:
- Domain test: call the mutator/factory and assert the event is in `aggregate.DomainEvents` with the right payload (no base class).
- Handler test: construct the handler with mocks and assert its effect (log/queued integration event). Handlers have no base class.

For an end-to-end check that the event actually dispatches on save, a **ddd-backend-integration-test** through
`GetMediator().Send(command)` exercises the real `SaveChangesAsync` dispatch path.

## Step 6 — Verify

```
dotnet build
dotnet test
```

Checklist: event is a past-tense `record : IDomainEvent` with `OccurredOn`; raised only from inside the aggregate;
mutator guards against no-op raises; handler is `INotificationHandler<T>` in `EventHandlers/`; payload is flat (no entity refs).
