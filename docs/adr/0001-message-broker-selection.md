# Message broker is per-service config, with a deliberately constrained interop matrix

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
