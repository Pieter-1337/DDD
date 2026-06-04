# Message broker is per-service config, with a deliberately constrained interop matrix

> **Note:** the Wolverine-path/interop framing below is superseded by [ADR-0003](0003-native-wolverine-flow-and-framework-alignment.md) — the supported Wolverine topology is now native Wolverine→Wolverine, and the MT→W interop bridge is demoted to reference. The broker-seam, two-plane emulator wiring, and broker-alignment decisions in this ADR still stand.

The system supports two message brokers (RabbitMQ, Azure Service Bus) underneath the two messaging frameworks (MassTransit, Wolverine). The broker is selected by a per-service `MessageBroker` config value (default `RabbitMq`) read inside the framework extensions — **not** enforced centrally by the Aspire AppHost — and the Wolverine↔MassTransit interop bridge (`ListenToMassTransitQueue` / `PublishToMassTransitExchange`) is RabbitMQ-only and fails fast at startup when the broker is Azure Service Bus.

## Why per-service config instead of AppHost-enforced

The broker must match across all services or cross-BC events silently stop flowing — that argues for one orchestrator-owned knob. We chose per-service config anyway: production (non-Aspire) environments configure each service individually regardless, so an AppHost-propagated value would create a second selection mechanism that only exists in local dev. One mechanism everywhere beats two; alignment is trusted to devs now and is checkable in a CI/release pipeline later. Misalignment between a service's `MessageBroker` value and the provisioned broker fails fast at startup via a connection-string format guard (`amqp://` vs `Endpoint=sb://`) with an error message naming the misalignment.

## Why the interop bridge stays RabbitMQ-only

The bridge is built on RabbitMQ exchange semantics (`BindExchange`, `ToRabbitExchange`, MassTransit's `Namespace:TypeName` exchange naming), which have no direct equivalent on Azure Service Bus (topics/subscriptions). Porting it was deliberately descoped. Consequence: on Azure Service Bus, a service receiving MassTransit-published events must itself run MassTransit (`MessagingFramework=MassTransit`); `ListenToMassTransitQueue` throws at startup on ASB rather than silently receiving nothing. Wolverine on ASB remains valid for publishing and native Wolverine↔Wolverine flows.

## Supporting decisions

- One connection-string name (`messaging`) for both brokers; the AppHost swaps which resource it provisions (RabbitMQ container vs ASB emulator) via its own parameter.
- Local dev uses the ASB emulator (`RunAsEmulator()`); a real Azure namespace is a user-secrets connection-string override away, used when real broker behaviour (e.g. dead-lettering, portal Service Bus Explorer) is needed. The emulator has no RabbitMQ-style management UI — message-flow observability comes from OpenTelemetry traces in the Aspire dashboard.
- Hosts' `Program.cs` files are untouched by broker selection: the switch lives entirely inside `AddMassTransitEventBus` / `AddWolverineEventBus`, with broker names as constants in `BuildingBlocks.Application.Messaging`.
- Verification is manual end-to-end (Aspire run, create Patient, observe BillingProfile + connected trace) against a documented supported-combinations matrix; that matrix doubles as the spec for a future automated contract test.

## Azure Service Bus emulator: two connection-string planes (`messaging` + `messaging-admin`)

The single-connection-string supporting decision above holds for **real Azure namespaces** (one endpoint serves both the AMQP data plane and the HTTP management plane). It does **not** hold for the **local emulator**, which splits the two planes onto different ports: the AMQP data plane on **5672** and the HTTP management plane on **5300** (`EMULATOR_HTTP_PORT`). MassTransit's startup topology creation goes through the Azure `ServiceBusAdministrationClient`, which speaks HTTP to the management plane — so on the emulator the administration client needs a connection string pointing at 5300, while the data-plane client uses the `messaging` string (5672). One connection string carries one `host:port`, so a single string cannot serve both planes on the emulator.

This was discovered during PR #8 live verification: startup connected the AMQP data plane fine, but `GetTopicAsync`/`CreateTopicAsync` fell back to `https://localhost:443` (connection refused), faulting the receive transport in a retry loop forever.

Decision — keep the single string for real namespaces; add a **second** string for the emulator only:

- The AppHost injects a second connection string named **`messaging-admin`** into both API services **only on the emulator path** (`RunAsEmulator`). It is built from the emulator container's `emulatorhealth` endpoint (Aspire's name for the port-5300 mapping), interpolating the dynamically allocated host-mapped port and reusing the emulator's fixed well-known SAS key, with `UseDevelopmentEmulator=true`.
- The MassTransit extension consumes `ConnectionStrings:messaging-admin` directly (not through `BrokerSelector` — the broker-selection module's contract and its tests stay untouched). When present, it builds an explicit `ServiceBusClient` (data plane, from `messaging`) and `ServiceBusAdministrationClient` (management plane, from `messaging-admin`) and hands both to MassTransit via the `Host(Uri, ServiceBusClient, ServiceBusAdministrationClient)` overload. When **absent** (real Azure namespace, or RabbitMQ), it keeps the original single-`messaging` `Host(connectionString)` path — **zero behaviour change for real namespaces**, preserving the user-secrets override criterion. Retry intervals and the EF Core outbox stay in the shared `ConfigureCommon` and are identical across both sub-paths.

Two version floors are required and pinned for this to work; both are emulator-only concerns with no effect on real namespaces:

- **Emulator image `2.0.0`** (released 2026-01-16) — the first image whose port-5300 plane exposes the Service Bus **management protocol** (Administration Client support). Aspire 13.1.1 pins the image to `1.1.2`, whose 5300 endpoint only serves a `/health` API, so the AppHost overrides it with `WithImageTag("2.0.0")`.
- **`Azure.Messaging.ServiceBus` >= `7.20.1`** (released 2025-06-12) — fixes a bug where `ServiceBusAdministrationClient` **reset the custom emulator port** (e.g. `:5300`). MassTransit.Azure.ServiceBus.Core 8.3.5 pulls this transitively at 7.18.2 (which has the bug), so we add an explicit `PackageReference` in the MassTransit project to make the Central-Package-Management `7.20.1` floor actually apply (a `PackageVersion` entry alone does not pin a transitive dependency).

Why not the fallback (predeclare entities in `Config.json` + disable MassTransit topology provisioning on the emulator path): it forks emulator-vs-real behaviour permanently, requires mirroring MassTransit's type-derived `Namespace:TypeName` entity naming by hand, and disabling topology provisioning on only the emulator path threatens the "retry/outbox identical across both branches" constraint. The two-plane wiring keeps provisioning on for both, so parity is structural.

Residual live-only risk: whether MassTransit 8.3.5 successfully *creates* its type-derived topic + subscription against the 2.0.0 admin endpoint (not just reads them) can only be confirmed by a live emulator run. The re-verification must run emulator **2.0.0** — watch for `CreateTopic`/`CreateSubscription` success, not just `GetTopic`.
