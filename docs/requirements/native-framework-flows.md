# Requirement: Native Wolverine→Wolverine flow + framework alignment

**Status:** Grilled & PRD-ready (future work — not yet built). Supersedes the interop-centric framing of [ADR-0001](../adr/0001-message-broker-selection.md) for the Wolverine path; see "Decision" below.
**Relates to:** PRD #1 (broker seam), [ADR-0001](../adr/0001-message-broker-selection.md)

## Context

PRD #1 made the message broker a switchable seam (RabbitMQ ↔ Azure Service Bus) and shipped a single cross-framework interop bridge — MassTransit → Wolverine via `ListenToMassTransitQueue` — as a **learning artifact**, deliberately RabbitMQ-only. That bridge was only ever a test of "can Wolverine consume a MassTransit-published event"; it was never meant to be a supported production topology.

This requirement closes the symmetric gap: a **native Wolverine → Wolverine** flow that stands on equal footing with the existing native MassTransit → MassTransit flow. The goal (set during the grill) is **full parity with MT→MT** — a first-class, switchable, permanently-wired flow, not a spike.

## The principle (why we constrain rather than generalise)

Cross-framework interop works by making the **consumer replicate the producer framework's wire dialect** — its envelope format *and* its topology naming. That couples a consumer not just to the *contract* but to *which framework sent it*. The integration surface then grows with the number of framework **pairs**, not services — `O(frameworks²)`, a spider web.

The escape (if ever needed) is a **framework-neutral Published Language**: every service translates between its internal framework and one shared wire format (e.g. CloudEvents) owned by the shared kernel (`Shared/IntegrationEvents`), via an Anti-Corruption Layer at each context edge. That collapses `O(frameworks²)` bridges into `O(frameworks)` adapters. **Out of scope** here — recorded as the known generalisation, not a commitment.

The same principle drives the alignment rule below: rather than maintain a runtime wire-format negotiation, we require all services on a flow to run the **same** framework — exactly parallel to ADR-0001's broker-alignment rule.

## Decision (crystallised in the grill)

1. **Same-framework flows run native; frameworks must align across services.**
   - MassTransit → MassTransit: native MassTransit conventional topology (works today; RabbitMQ + ASB emulator).
   - Wolverine → Wolverine: native Wolverine **conventional routing** (the new capability). *Rationale: MT→MT uses MassTransit's conventional topology — `ConfigureEndpoints` + type-routed `Publish`, zero hardcoded names — so true parity means W→W uses Wolverine's conventional routing, not hand-named exchanges. The only place explicit `Namespace:TypeName` names exist is the interop bridge, precisely because it imitates MassTransit's convention.*
   - **Framework-alignment rule:** all services on a flow run the same framework (both MassTransit, or both Wolverine). There is no runnable mixed configuration. This mirrors ADR-0001's "broker must match across all services or events silently stop flowing." The framework axis is **unguardable** locally (a service cannot see another's framework — unlike the broker's self-describing `amqp://` vs `sb://` connection string), so alignment is enforced by aligned defaults + a single AppHost fan-out (see "Default & alignment").

2. **The MT→W interop bridge is demoted to commented reference.** In `Billing.WebApi`, the `ListenToMassTransitQueue<PatientCreatedIntegrationEvent>(...)` call is **commented out** with a note that it is kept purely as a reference for re-testing an MT→W flow; the supported Wolverine path is native W→W. The bridge code itself (`ListenToMassTransitQueue`, `PublishToMassTransitExchange`, `GuardMassTransitInteropSupported`, the MassTransit-interop envelope mapper, and `WolverineMassTransitInteropGuardTests`) **stays in BuildingBlocks** as dead-but-documented reference — no deletion, no test churn.

3. **No `IntegrationEventWireFormat` config knob.** Wire format is determined entirely by `MessagingFramework`. (The richer per-listener wire-format design is preserved as reference only — see "Reference design".)

4. **Wolverine → MassTransit is not pursued in any broker** (ADR-0001 descoped this; `PublishToMassTransitExchange` exists as reference only). Native-vs-interop is a configuration/alignment decision, never runtime detection of the producer's framework — a channel carries one wire format.

## What native W→W requires (the concrete work)

Surprisingly little, because conventional routing removes all name bookkeeping:

1. **`Scheduling.WebApi`** — add a `MessagingFramework` switch mirroring `Billing.WebApi` (default `MassTransit`). Its Wolverine branch publishes `PatientCreatedIntegrationEvent` via the existing `WolverineDbContextEventBus` outbox; conventional routing sends it to the convention exchange automatically — **no explicit publish rule**.
2. **`AddWolverineEventBus`** — chain `.UseConventionalRouting()` on **both** transport expressions (`UseRabbitMq(...)` and `UseAzureServiceBus(...)`), alongside the existing `.AutoProvision()` calls. *(It is a transport-expression API, not a `WolverineOptions` one, so it lives in both branches — not the shared block.)*
3. **`Billing.WebApi`** — comment out `ListenToMassTransitQueue<PatientCreatedIntegrationEvent>(...)`. With conventional routing on, Wolverine auto-creates the listening endpoint for `PatientCreatedIntegrationEvent` (it has a native `Handle` consumer) — **no explicit listen call**.

The shared `PatientCreatedIntegrationEvent` type in `Shared/IntegrationEvents` guarantees the publisher's and listener's convention-derived names match with zero coordination.

## Default & alignment

- **Both services default to `MassTransit`.** Out-of-box flow stays **MT→MT** — works on every broker, including the ASB emulator. This flips `Billing.WebApi`'s current `?? "Wolverine"` default to `?? "MassTransit"`. Without this, the default pairing (Scheduling-MT publish → Billing-Wolverine native listen) would silently drop every message.
- **W→W is opt-in by setting *both* services to `Wolverine`.**
- **The AppHost fans out one value per axis.** `Aspire.AppHost` reads a single `messaging-framework` value (default `MassTransit`) and injects it into both services via `WithEnvironment("MessagingFramework", …)`; it now does the same for the broker (`messaging-broker` → each service's `MessageBroker`), so one knob per axis aligns both services and they cannot drift locally. This does **not** violate ADR-0001's "one mechanism" principle: services still read the same config keys they read in prod — the AppHost is merely one config *source* (like user-secrets) supplying an aligned value for local dev, and the injection happens only when the AppHost launches the services (production stays per-service config).

## Acceptance criteria

- [ ] With both services on `MessagingFramework=Wolverine`, the Patient → BillingProfile flow runs end-to-end via **native Wolverine conventional routing** (no MassTransit-interop envelope), verified on **RabbitMQ**.
- [ ] Verification confirms the **topology names agree** — the publisher's exchange and the listener's queue/binding (inspect the RabbitMQ topology or Wolverine startup logs), not merely that a BillingProfile appeared. A silent convention-name mismatch is the primary failure mode.
- [ ] Default run (no override) is **MT→MT** and still works on RabbitMQ and the ASB emulator; the MT→MT and (commented) MT→W reference paths are otherwise unchanged.
- [ ] The supported-combinations matrix ([phase-5 doc 09](../phase-5-event-driven/09-broker-framework-matrix.md)) is updated: native W→W supported on RabbitMQ; framework-alignment rule documented; emulator W→W noted as a known WolverineFx 4.12.2 limitation.
- [ ] One AppHost `messaging-framework` knob flips both services together.

## Out of scope

- **W→W on the ASB emulator** — blocked by WolverineFx 4.12.2, which cannot inject a separate admin client for the emulator's port-5300 management plane. **Confirmed empirically (2026-06-04):** a build probe showed 4.12.2's `AzureServiceBusTransport` has no `ManagementConnectionString` property, so `AutoProvision` can't reach the management plane. Current WolverineFx adds `transport.ManagementConnectionString` (latest 6.4.3), which could be fed from the AppHost's existing `messaging-admin` string — but reaching it is a **major 4→6 upgrade** (breaking-change risk across the whole Wolverine surface), so verification stays **RabbitMQ-only** for now. RabbitMQ is unaffected; a real Azure namespace also removes the limit.
- **W→W on a real Azure Service Bus namespace** — theoretically works (emulator blocker does not apply), but standing up a real namespace is an on-demand user-secrets override, not part of this requirement's bar. Optional follow-up.
- CloudEvents / framework-neutral published language (the `O(frameworks)` generalisation).
- Porting the MT↔W interop bridge to Azure Service Bus, or any Wolverine → MassTransit interop.
- Real-namespace production hardening (managed identity, IaC provisioning, topology management).

## Reference design — per-listener wire format + mixed multi-framework topology (NOT built)

Preserved from the grill as a design we may want if the system ever runs **mixed** frameworks across more than two contexts. **Not implemented; no config knob exists.** Recorded so the idea isn't lost.

The framework-alignment rule above keeps things simple by forbidding mixed topologies. But if a future system had, say, four contexts where one Wolverine consumer must receive an event from a MassTransit publisher (interop) *and* another event from a Wolverine publisher (native) **simultaneously**, a single per-service framework flag is insufficient — wire format becomes a property of each **flow**, not each service.

Three legal edge types (publisher's framework decides the wire format):

```
 MT-native  :  MassTransit pub → MassTransit consumer   (ConfigureEndpoints, automatic)
 MT-interop :  MassTransit pub → Wolverine  consumer   (ListenToMassTransitQueue<T>, RabbitMQ-only)
 W -native  :  Wolverine  pub → Wolverine  consumer   (conventional routing)
 W →  MT    :  ✗ unsupported — Wolverine pub → MassTransit consumer (never wired)
```

A worked 4-service example. **Notifications** is the case that breaks a per-service flag — two inbound edges, two different wire formats at once:

```
                       PatientCreated (MassTransit wire)
   Scheduling [MT] ──┬──────────────┬───────────────────┬──────────────────►
    (publisher)      │ MT-interop   │ MT-interop         │ MT-native
                     ▼              ▼                    ▼
              Billing [W]     Notifications [W]     Analytics [MT]
              pub + sub        (consumer)            (consumer)
                     │              ▲
   BillingProfileCreated (Wolverine wire)
                     └──── W-native ┘

  Analytics is [MT] → can only join MT-published flows; it CANNOT read
  BillingProfileCreated (that would be a W→MT edge, never wired).
```

| Service | Framework | wire format(s) it must declare |
| --- | --- | --- |
| Scheduling | MassTransit | — (publisher; emits MT-native) |
| Billing | Wolverine | `PatientCreated` → interop &nbsp; *(single value OK)* |
| Analytics | MassTransit | `PatientCreated` → MT-native (auto) &nbsp; *(single value OK)* |
| Notifications | Wolverine | `PatientCreated` → interop **and** `BillingProfileCreated` → native &nbsp; **(needs per-listener)** |

The design (if ever built):
- A `WireFormat` enum (`Native | MassTransitInterop`) + a resolver in `BuildingBlocks.Application.Messaging`, mirroring `BrokerSelector`.
- A single shared seam — `opts.ListenForIntegrationEvent<TEvent>(WireFormat)` — dispatching to `ListenToMassTransitQueue<T>` (interop) or native conventional listen. **Per-listener** (chosen at each call site), so one service can mix formats. Config stays a simple per-service scalar default; per-flow config is only modelled when a second heterogeneous flow actually exists.
- Wire format is consumer-side only (a Wolverine publisher always emits native; W→MT publishing is not pursued).
