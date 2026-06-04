# Supported Messaging-Framework × Broker Matrix

This is the recorded-knowledge record of **which messaging-framework / message-broker
combinations actually work in this system, which fail, and which are not wired** — so the
broker seam's constraints stop being tribal knowledge.

It is the checklist for the manual end-to-end verification of the broker seam, and it doubles
as the **spec for a future automated contract test** (out of scope — see [Out of scope](#out-of-scope)).

> Authoritative decisions live in:
> - **[ADR-0001 — message broker selection](../adr/0001-message-broker-selection.md)** (broker-alignment rule & interop bridge design).
> - **[ADR-0003 — native Wolverine flow & framework alignment](../adr/0003-native-wolverine-flow-and-framework-alignment.md)** (framework-alignment rule & native W→W design).
> 
> Requirements and `O(frameworks²)` interop rationale live in
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

Both services now support a **`MessagingFramework` switch**:

1. **`Scheduling.WebApi`** — `MassTransit` (default) or `Wolverine` (switch added in PRD #24).
2. **`Billing.WebApi`** — `MassTransit` (default) or `Wolverine`.

The cross-context flows in the system are:

```
Scheduling [MassTransit]  --PatientCreatedIntegrationEvent-->  Billing [MassTransit native] ✅ (default)
Scheduling [MassTransit]  --PatientCreatedIntegrationEvent-->  Billing [Wolverine interop]  ✅ (legacy reference, RabbitMQ-only)
Scheduling [Wolverine]    --PatientCreatedIntegrationEvent-->  Billing [Wolverine native]   ✅ (verified on RabbitMQ; both on Wolverine)
```

The **framework-alignment rule** constrains realizable flows: all services on a single flow must run 
the same framework (both MassTransit native, or both Wolverine native). The MT→W interop bridge is 
RabbitMQ-only and is demoted to reference (ADR-0003).

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

Both services now carry a `MessagingFramework` switch (PRD #24), so the publisher is no longer
fixed. Of the four conceptual framework pairings (MT→MT, MT→W, W→MT, W→W), the **two
framework-aligned native flows are realizable** today — MT→MT (default) and W→W (verified on
RabbitMQ) — plus the **MT→W interop bridge** kept as a RabbitMQ-only legacy reference. **W→MT is
not wired.**

| Flow (publisher → consumer) | RabbitMq                                | Azure Service Bus                                                       |
| --------------------------- | --------------------------------------- | ---------------------------------------------------------------------- |
| **MT → Wolverine** (interop)| ✅ **works — legacy path** *(MT→W interop bridge is RabbitMQ-only; demoted to reference in ADR-0003)* | ❌ **fails by design** (guard throws at startup — both emulator AND real namespace) |
| **MT → MassTransit** (native)| ✅ works                                | ✅ works *(ASB demo config; emulator wiring confirmed in code, live topology-creation pending)* |
| **Wolverine → Wolverine** (native) | ✅ **works** *(verified end-to-end; framework-alignment rule applies — see below)* | ⚠️ **blocked by WolverineFx 4.12.2** (AutoProvision cannot reach emulator's port-5300 management plane; valid on real namespace; upgrade to 6+ unblocks — see notes) |
| **Wolverine → MassTransit** | 🚫 **not wired** (never pursued) | 🚫 **not wired** |

### MT → Wolverine interop: legacy reference — and the deliberately-dead cell on ASB

- **On RabbitMq** this is a **legacy reference path — no longer the default.** Since PRD #24
  Billing's `MessagingFramework` defaults to `MassTransit` (so the out-of-box flow is MT→MT
  native), and the interop listener `ListenToMassTransitQueue<PatientCreatedIntegrationEvent>(...)`
  is **commented out** in `Program.cs`. Re-enabling it (with Billing on `Wolverine`) restores the
  MT→W bridge: a Wolverine queue bound to MassTransit's `Namespace:TypeName` RabbitMQ exchange,
  using MassTransit envelope interop. Left in place so the bridge can be re-tested without
  re-deriving it; Billing-on-Wolverine otherwise consumes **natively** via conventional routing.
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
  emulator-ness. And because the interop listener (when uncommented) is a property of Billing's
  Wolverine configuration on ASB, the dead cell depends on that opt-in, not on what the publisher
  happens to be.

### Wolverine → Wolverine: native conventional routing

Native W→W **is now supported and verified end-to-end on RabbitMQ** via Wolverine's `UseConventionalRouting()` — 
the implementation shipped in [ADR-0003](../adr/0003-native-wolverine-flow-and-framework-alignment.md) and
[docs/requirements/native-framework-flows.md](../requirements/native-framework-flows.md). The patient → BillingProfile 
flow has been confirmed working when both services run `MessagingFramework=Wolverine`: the publisher's exchange and 
the listener's queue/binding share the same convention-derived `IntegrationEvents.Scheduling.PatientCreatedIntegrationEvent` name.

**Framework-alignment rule (mandatory for native flows):** All services on a **single cross-context flow** must run 
the same messaging framework — either both MassTransit (native MT→MT) or both Wolverine (native W→W). **No mixed-framework 
*native* configuration is runnable** (it silently drops messages, unguardable at startup because services cannot see each 
other's framework choice — unlike the broker's self-describing connection string). The one deliberate exception is the 
**MT→W interop bridge** (RabbitMQ-only, commented-out legacy reference above): it crosses frameworks on purpose via 
MassTransit envelope interop rather than native routing. This mirrors ADR-0001's broker-alignment rule. See [native-framework-flows.md § "Default & alignment"](../requirements/native-framework-flows.md#default--alignment) 
for the AppHost `messaging-framework` knob that fans out a single aligned value to both services.

**On Azure Service Bus:** native W→W on the **emulator is blocked** by WolverineFx 4.12.2, which cannot inject a 
separate management-plane client — its `AzureServiceBusTransport` has no `ManagementConnectionString` property, 
so `AutoProvision` tries to reach `localhost:443` and fails. This is **confirmed empirically (2026-06-04)** via a 
build probe. Current WolverineFx (6.4.3+) adds the property, feedable from the AppHost's existing `messaging-admin` 
string — but reaching it requires a **major 4→6 upgrade with breaking-change risk across the Wolverine surface**, 
deferred to [issue #28](https://github.com/Pieter-1337/DDD/issues/28). Native W→W on a **real Azure namespace** 
is unaffected (no emulator management-plane workaround needed). RabbitMQ remains fully unaffected and fully 
verified.

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

## System-wide constraints: broker and framework alignment

In **production** there is no central enforcement — broker and framework are **per-service
config by design** (ADR-0001: each service is configured individually from its own config
files / secrets). **Under the AppHost (local dev only)** both axes are centralized: it reads
one value for each (`Parameters:messaging-broker`, `Parameters:messaging-framework`) and
**fans it out to every service** via `WithEnvironment(...)`, so they cannot drift locally.
This is not a competing production mechanism — the env injection happens **only when the
AppHost launches the services**; a non-AppHost deployment still reads per-service config.

The consequence is a **system-wide alignment requirement** for every cross-context flow:

1. **Same broker across all services on the flow** (broker-alignment rule, ADR-0001).
   If Scheduling runs RabbitMq and Billing runs AzureServiceBus (or vice-versa), cross-context 
   events stop flowing. This fails **fast**: `BrokerSelector`'s connection-string **format guard** 
   throws at startup when a service's `MessageBroker` value does not match the connection string 
   it received (expects `amqp://` for RabbitMq, `Endpoint=sb://` for AzureServiceBus), with an 
   error message naming the misalignment and the fix. **Under the AppHost**, the single
   `messaging-broker` knob (default `RabbitMq`) provisions the container **and** is fanned out to
   both services' `MessageBroker`, so one local value aligns container + services and the guard
   is a backstop rather than the primary mechanism.

2. **Same framework across all services on the flow** (framework-alignment rule, ADR-0003).
   All services on a **single cross-context flow** must run the same messaging framework — either 
   both MassTransit (native MT→MT) or both Wolverine (native W→W). There is **no runnable mixed 
   configuration**. If Scheduling publishes on MassTransit and Billing listens on Wolverine with 
   native conventional routing (not the MT→W interop bridge), events silently stop flowing. Unlike 
   the broker alignment guard (which fails fast via connection-string format mismatch), framework 
   misalignment **cannot be caught at startup** — a service cannot see another service's 
   `MessagingFramework` choice. Alignment is enforced by:
   - Both services defaulting to **`MassTransit`** (the out-of-box flow is MT→MT, which works on 
     all brokers).
   - The **AppHost exposing one `messaging-framework` parameter** (default `MassTransit`) and 
     fanning it out via `WithEnvironment(...)` to both services, so a single local knob keeps 
     them aligned and cannot drift — see [native-framework-flows.md § "Default & alignment"](../requirements/native-framework-flows.md#default--alignment).

Alignment is **trusted to developers now** and is **checkable by a CI/release pipeline later**
(out of scope — ADR-0001 and ADR-0003).

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

See **[../SETUP.md](../SETUP.md)** for the full fresh-machine guide. Under the AppHost, switching
is done with **two knobs**, each set on the AppHost only and **fanned out to both services**:

| Knob (AppHost) | Default | Effect |
| --- | --- | --- |
| `Parameters:messaging-broker` / `ASPIRE_MESSAGING_BROKER` | `RabbitMq` | provisions the chosen broker container **and** sets every service's `MessageBroker` (+ injects `messaging`, and emulator-only `messaging-admin`, connection strings) |
| `Parameters:messaging-framework` / `ASPIRE_MESSAGING_FRAMEWORK` | `MassTransit` | sets every service's `MessagingFramework` |

Examples: native **W→W on RabbitMQ** → set `messaging-framework=Wolverine` (broker stays
`RabbitMq`). **ASB emulator** (MT→MT) → set `messaging-broker=AzureServiceBus` (framework stays
`MassTransit`; the MT→W interop bridge is RabbitMQ-only). Because the AppHost fans both values
out, a single value moves the container and both services together — the per-service
`MessageBroker`/`MessagingFramework` settings are **not** needed for a local AppHost run (they
remain the mechanism for non-AppHost / production deployments). The startup guards stay as a
backstop if a non-AppHost run is configured inconsistently.

The emulator has **no management UI** (and is incompatible with the community Service Bus
Explorer tools). Message-flow observability comes from the **OpenTelemetry traces in the Aspire
dashboard**.

---

## Out of scope

- Porting the Wolverine↔MassTransit interop bridge to Azure Service Bus topics/subscriptions
  (the constrained matrix is the deliberate alternative — ADR-0001).
- The CI/release pipeline check that all services' `MessageBroker` and `MessagingFramework` 
  values align (feasible, deliberately deferred).
- **WolverineFx 4→6 upgrade for native W→W on ASB emulator** — deferred to [issue #28](https://github.com/Pieter-1337/DDD/issues/28).
  The 4.12.2 limitation (no `ManagementConnectionString` on `AzureServiceBusTransport`, blocking 
  `AutoProvision` on the emulator) is lifted in WolverineFx 6.4.3+, but the major-version upgrade 
  carries breaking-change risk across the whole Wolverine surface and is not pursued here.
  Real Azure namespaces are unaffected and work today.
- **Automated contract tests.** This matrix doubles as the spec for a future automated contract
  test that would assert each cell's startup behavior (works / throws-with-message / not-wired)
  without a human running the system.

---

> Related: [ADR-0001 — message broker selection](../adr/0001-message-broker-selection.md) ·
> [native-framework-flows requirement](../requirements/native-framework-flows.md) ·
> [01-event-driven-overview.md](./01-event-driven-overview.md)
