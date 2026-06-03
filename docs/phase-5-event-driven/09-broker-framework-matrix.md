# Supported Messaging-Framework × Broker Matrix

This is the recorded-knowledge record of **which messaging-framework / message-broker
combinations actually work in this system, which fail, and which are not wired** — so the
broker seam's constraints stop being tribal knowledge.

It is the checklist for the manual end-to-end verification of the broker seam, and it doubles
as the **spec for a future automated contract test** (out of scope — see [Out of scope](#out-of-scope)).

> Authoritative decisions live in **[ADR-0001 — message broker selection](../adr/0001-message-broker-selection.md)**.
> Future native-framework work and the `O(frameworks²)` interop rationale live in
> **[docs/requirements/native-framework-flows.md](../requirements/native-framework-flows.md)**.
> This document records the **currently implemented behavior** — verified against the merged
> code, with live-run gaps called out explicitly.

---

## The two axes (and what's fixed in the current system)

The system has two independent selection mechanisms, both **per-service config** (no central
enforcement — ADR-0001):

- **`MessagingFramework`** — `MassTransit` or `Wolverine`. Read by the **host** (`Program.cs`).
- **`MessageBroker`** — `RabbitMq` (default) or `AzureServiceBus`. Read by the **framework
  extension** via `BrokerSelector.Resolve(...)` (`BuildingBlocks.Application.Messaging`).

But two facts about the **hosts** constrain which combinations are reachable today:

1. **`Scheduling.WebApi` is hardcoded to MassTransit.** Its `Program.cs` calls
   `AddMassTransitEventBus` unconditionally — there is **no** `MessagingFramework` switch.
   So **Scheduling is always the publisher, always on MassTransit.**
2. **Only `Billing.WebApi` has a `MessagingFramework` switch** — `Wolverine` (default) or
   `MassTransit`. When on Wolverine, Billing consumes the cross-context event through
   `ListenToMassTransitQueue<PatientCreatedIntegrationEvent>` — a **RabbitMQ-exchange-based
   interop listener**, not a native Wolverine subscription.

The only cross-context flow in the system is therefore:

```
Scheduling (always MassTransit)  --PatientCreatedIntegrationEvent-->  Billing (Wolverine via interop, OR MassTransit native)
```

---

## Layer 1 — Per-framework capability on each broker

Each framework in isolation (publish + consume of its own native messages):

| Framework      | RabbitMq                          | Azure Service Bus                                                                 |
| -------------- | --------------------------------- | -------------------------------------------------------------------------------- |
| **MassTransit**| ✅ works (default, original path) | ✅ works *(wiring confirmed in code; live emulator topology-creation pending — see notes)* |
| **Wolverine**  | ✅ works                          | ⚠️ **partial** — native publish + native W→W valid on a **real namespace** (emulator AutoProvision blocked; not reachable in the current hosts — see Layer 2); the **MassTransit-interop listener fails fast** |

Notes:

- **MassTransit on Azure Service Bus** branches inside `AddMassTransitEventBus` to
  `UsingAzureServiceBus(...)`. On a **real namespace** it uses the single `messaging`
  connection string (one endpoint serves both AMQP data plane and HTTP management plane);
  on the **emulator** it uses two planes (see [ASB sub-cases](#azure-service-bus-sub-cases-emulator-vs-real-namespace)).
- **Wolverine on Azure Service Bus** branches to `UseAzureServiceBus(...).AutoProvision()`.
  This is valid for **publishing** and for **native Wolverine → Wolverine** flows. What does
  **not** work on ASB is the **MT-interop receive** (`ListenToMassTransitQueue<T>`), which is
  guarded — see Layer 2's dead cell. The guard is on the **interop helper**, not on
  `UseAzureServiceBus` itself, so "Wolverine on ASB" is not dead wholesale — only the
  RabbitMQ-exchange bridge is.

---

## Layer 2 — Realizable cross-context flows (Scheduling → Billing)

Because Scheduling is always MassTransit, the *publisher* framework is fixed. Of the four
conceptual framework pairings (MT→MT, MT→W, W→MT, W→W), **only the two with a MassTransit
publisher are realizable** today.

| Flow (publisher → consumer) | RabbitMq                                | Azure Service Bus                                                       |
| --------------------------- | --------------------------------------- | ---------------------------------------------------------------------- |
| **MT → Wolverine** (interop)| ✅ **works — this is today's default**  | ❌ **fails by design** (guard throws at startup — both emulator AND real namespace) |
| **MT → MassTransit** (native)| ✅ works                                | ✅ works *(ASB demo config; emulator wiring confirmed in code, live topology-creation pending)* |
| **Wolverine → MassTransit** | 🚫 **not wired** (Scheduling can't run on Wolverine) | 🚫 **not wired** |
| **Wolverine → Wolverine** (native) | 🚫 **not wired** (future work)   | 🚫 **not wired** (future work; additionally emulator-blocked — see notes) |

### MT → Wolverine: today's default — and the deliberately-dead cell on ASB

- **On RabbitMq** this is the **default configuration**: Billing's `MessagingFramework`
  defaults to `Wolverine` (`Program.cs`), and Wolverine receives the MassTransit-published
  `PatientCreatedIntegrationEvent` via `ListenToMassTransitQueue` (binds a Wolverine queue to
  MassTransit's `Namespace:TypeName` RabbitMQ exchange and uses MassTransit envelope interop).
- **On Azure Service Bus** this cell is **deliberately dead.** Configuring the interop
  listener while `MessageBroker=AzureServiceBus` throws an `InvalidOperationException` at
  startup. The gist of the exception:

  > The Wolverine MassTransit-interop listener (`ListenToMassTransitQueue`) is **RabbitMQ-only**:
  > it is built on RabbitMQ exchange semantics with no Azure Service Bus equivalent, and porting
  > it to Azure Service Bus was deliberately descoped (ADR-0001). `MessageBroker` is
  > `AzureServiceBus`, so this service cannot receive MassTransit-published events through
  > Wolverine. **Supported alternative: run this service with `MessagingFramework=MassTransit`
  > on this broker.** Wolverine on Azure Service Bus remains valid for publishing and native
  > Wolverine-to-Wolverine flows.

  **Named fix:** run Billing with `MessagingFramework=MassTransit` (moving the flow into the
  MT→MT cell, which works on ASB).

  **Why dead:** the bridge is built on RabbitMQ exchange semantics (`BindExchange`,
  `ToRabbitExchange`, MassTransit's `Namespace:TypeName` exchange naming), which have no direct
  equivalent on Azure Service Bus topics/subscriptions (ADR-0001). Porting it was descoped.

  **Independent of the publisher's framework:** the guard
  (`GuardMassTransitInteropSupported`) checks the **broker name only** (`broker == AzureServiceBus`).
  It throws on the emulator **and** on a real namespace alike — it does not check
  emulator-ness. And because Billing's host **hardcodes** the interop listener whenever it runs
  on Wolverine, the dead cell is a property of Billing's Wolverine configuration on ASB, not of
  what the publisher happens to be.

### Wolverine → Wolverine: future work (not wired today)

Native W→W is **not wired**: it would require Scheduling to publish on Wolverine, which its host
does not support, and Billing's Wolverine path uses the MT-interop listener rather than a native
Wolverine subscription. This is the future-work record in
[docs/requirements/native-framework-flows.md](../requirements/native-framework-flows.md).

Additionally, native Wolverine **AutoProvision against the emulator** is blocked: WolverineFx
4.12.2 cannot inject a separate admin client, so it does **not** consume the `messaging-admin`
string and its provisioning hits the emulator's management plane (`localhost:443`) wall. Native
Wolverine on a **real Azure namespace** is fine. (This concerns only the future W→W work; W→W is
**not wired today** regardless.)

---

## Azure Service Bus sub-cases: emulator vs real namespace

The MassTransit ASB path behaves differently on the local emulator versus a real namespace —
the distinction matters for the ✅ cells above.

| Sub-case               | What's needed                                                                                          |
| ---------------------- | ----------------------------------------------------------------------------------------------------- |
| **Real Azure namespace** | Plain — one endpoint serves both the AMQP data plane and the HTTP management plane; one `messaging` connection string; MassTransit's default TTLs are accepted. No emulator hacks. Supplied via a user-secrets `messaging` override (zero code change). |
| **ASB emulator** (local default) | Needs two emulator-specific fixes shipped in slice #3 (PR #8): (1) a **second `messaging-admin` connection string** — the emulator splits the AMQP data plane (5672) from the HTTP management plane (5300), and one connection string carries one `host:port`, so the Service Bus Administration Client used for topology creation needs its own string; (2) **clamping** MassTransit's transport `Defaults` TTL / `AutoDeleteOnIdle` to the emulator's **1-hour ceiling** (the emulator rejects MassTransit's production 366-day defaults at `CreateTopic`). Also requires emulator **image 2.0.0** and `Azure.Messaging.ServiceBus >= 7.20.1`. |

- **MT→MT works on both** ASB sub-cases (real namespace plainly; emulator via the two fixes
  above).
- **MT→Wolverine throws the guard on both** ASB sub-cases (the guard checks the broker name,
  not emulator-ness).

> **Live-verification gap (be precise).** The MT→MT **wiring** on the emulator is confirmed by
> reading the merged code, but the **runtime** success of MassTransit *creating* its
> type-derived topic + subscription against the 2.0.0 admin endpoint, and Wolverine's
> `AutoProvision` against the emulator, **were not executed** in slices #3/#4 (PRs #8 and #9
> both ship with the live end-to-end runs left as pre-merge manual checks). Treat the emulator
> ✅ cells as **"works by construction; live emulator topology-creation confirmation pending,"**
> not as a confirmed live pass. A real namespace needs none of the emulator hacks but was
> likewise not live-verified here.

---

## System-wide constraint: all services on a flow must align

There is **no central enforcement** of broker or framework choice — both are **per-service
config by design** (ADR-0001: production configures each service individually, so an
AppHost-propagated value would be a second, local-only mechanism).

The consequence is a **system-wide alignment requirement** for every cross-context flow:

1. **Same broker across all services on the flow.** If Scheduling runs RabbitMq and Billing
   runs AzureServiceBus (or vice-versa), cross-context events stop flowing. This fails **fast**:
   `BrokerSelector`'s connection-string **format guard** throws at startup when a service's
   `MessageBroker` value does not match the connection string it received (expects `amqp://`
   for RabbitMq, `Endpoint=sb://` for AzureServiceBus), with an error message naming the
   misalignment and the fix.
2. **Framework alignment per the supported pairings.** Same-framework-native (MT→MT, native
   W→W) or the **single MT→W bridge** (RabbitMq-only). Any other cross-framework-on-ASB
   combination fails fast via the interop guard.

Alignment is **trusted to developers now** and is **checkable by a CI/release pipeline later**
(out of scope — ADR-0001).

---

## Fail-fast cheat sheet

| Misconfiguration                                                        | What happens at startup                                                                 |
| ----------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| `MessageBroker=AzureServiceBus` but `messaging` is an `amqp://` URI     | `BrokerSelector` format guard throws (names the misalignment + fix)                     |
| `MessageBroker=RabbitMq` but `messaging` is an `Endpoint=sb://` string  | `BrokerSelector` format guard throws (names the misalignment + fix)                     |
| `messaging` connection string missing                                   | `BrokerSelector` throws (asks for the Aspire resource or `ConnectionStrings:messaging`) |
| Unrecognized `MessageBroker` value                                      | `BrokerSelector` throws (lists valid values)                                            |
| Billing on `MessagingFramework=Wolverine` + `MessageBroker=AzureServiceBus` | Interop guard throws (names `MessagingFramework=MassTransit` as the fix)            |

---

## How to switch locally

See **[../SETUP.md](../SETUP.md)** for the full fresh-machine guide. The short version of
switching the broker for the local Azure Service Bus demo — **all three settings are needed
together**, or a startup guard trips:

1. **AppHost** — `Parameters:messaging-broker=AzureServiceBus` (or `ASPIRE_MESSAGING_BROKER`):
   provisions the emulator and injects the `Endpoint=sb://` `messaging` (and emulator-only
   `messaging-admin`) connection strings.
2. **Each service** — `MessageBroker=AzureServiceBus`. The AppHost does **not** propagate this
   (per-service config by design — it swaps the resource and injects `messaging-admin`, but
   never sets `MessageBroker`). Set it on **both** Scheduling and Billing, or `BrokerSelector`'s
   format guard throws (it expected `amqp://` but got `Endpoint=sb://`). This partial-config
   trip is itself the fail-fast design at work.
3. **Billing** — `MessagingFramework=MassTransit` (for the ASB demo), or the interop guard
   throws.

The emulator has **no management UI** (and is incompatible with the community Service Bus
Explorer tools). Message-flow observability comes from the **OpenTelemetry traces in the Aspire
dashboard** — a connected publish→consume span tree confirms the flow.

---

## Out of scope

- Porting the Wolverine↔MassTransit interop bridge to Azure Service Bus topics/subscriptions
  (the constrained matrix is the deliberate alternative — ADR-0001).
- The CI/release pipeline check that all services' `MessageBroker` values align (feasible,
  deliberately deferred).
- **Automated contract tests.** This matrix doubles as the spec for a future automated contract
  test that would assert each cell's startup behavior (works / throws-with-message / not-wired)
  without a human running the system.

---

> Related: [ADR-0001 — message broker selection](../adr/0001-message-broker-selection.md) ·
> [native-framework-flows requirement](../requirements/native-framework-flows.md) ·
> [01-event-driven-overview.md](./01-event-driven-overview.md)
