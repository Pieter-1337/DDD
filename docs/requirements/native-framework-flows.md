# Requirement: Native same-framework flows + constrained cross-framework interop

**Status:** Proposed (future work — not part of PRD #1 "Azure Service Bus broker seam")
**Relates to:** PRD #1 (broker seam), [ADR-0001](../adr/0001-message-broker-selection.md)

## Context

PRD #1 made the message broker a switchable seam (RabbitMQ ↔ Azure Service Bus). It deliberately **constrained** the cross-framework interop matrix rather than generalising it: the Wolverine↔MassTransit interop bridge stays RabbitMQ-only, and configuring it on Azure Service Bus fails fast (the guard shipped in issue #4).

Working through the consequences surfaced a scaling principle worth recording as a requirement for any future multi-domain / multi-framework expansion.

## The principle (why we do not generalise the bridge)

Cross-framework interop works by making the **consumer replicate the producer framework's wire dialect** — its envelope format *and* its topology naming. That couples a consumer not just to the *contract* but to *which framework sent it*. The integration surface then grows with the number of framework **pairs**, not services — `O(frameworks²)`, a spider web. Each new framework must bridge to every existing one.

The escape (if ever needed) is a **framework-neutral Published Language**: every service translates between its internal framework and one shared wire format (e.g. CloudEvents) owned by the shared kernel (`Shared/IntegrationEvents`), via an Anti-Corruption Layer at each context edge. That collapses `O(frameworks²)` bridges into `O(frameworks)` adapters-to-the-standard. This is explicitly **out of scope** here — recorded as the known generalisation, not a commitment.

## Decision / target strategy

1. **Same-framework flows run native on any broker.**
   - MassTransit → MassTransit: native MassTransit (works today; verified on RabbitMQ and the ASB emulator).
   - Wolverine → Wolverine: native Wolverine (the new capability this requirement enables).
2. **Exactly one cross-framework bridge — MassTransit → Wolverine — stays as today: RabbitMQ-only**, via `ListenToMassTransitQueue<T>`. It is the existing learning artifact; it is not ported to Azure Service Bus and is not generalised to other framework pairs.
3. **Every other cross-framework-on-ASB combination fails fast** with the descriptive guard (per ADR-0001). Wolverine → MassTransit is not wired and not pursued.
4. **A channel carries one wire format.** A consumer must not detect the producer's framework at runtime (that re-introduces the spider web). Native-vs-interop is a **configuration/alignment decision**, mirroring the broker-alignment rule: *if all services on a flow run the same framework → native; the MT→W bridge is the one documented mixed case.*

## What "make Wolverine → Wolverine work" requires

W→W is not blocked solely by the ASB emulator. Two independent blockers exist today:

- **Not wired:** `Scheduling.WebApi` is hardcoded to MassTransit (no `MessagingFramework` switch), so it can never be a Wolverine publisher. `Billing.WebApi`'s Wolverine path is hardcoded to the MassTransit-interop listener, not a native Wolverine listener.
- **ASB emulator only:** even if wired, WolverineFx 4.12.2 cannot inject a separate admin client, so its `AutoProvision` cannot reach the emulator's separate management port. A real Azure namespace removes this; RabbitMQ is unaffected.

| Broker | W→W blocker |
| --- | --- |
| RabbitMQ | wiring only → works with host changes, no ASB needed |
| Azure Service Bus (real namespace) | wiring only → emulator blocker does not apply |
| Azure Service Bus (emulator) | wiring + admin-plane limitation → hardest case |

Work to enable it:

1. `Scheduling.WebApi`: add a `MessagingFramework` switch (mirroring `Billing.WebApi`) with a Wolverine branch that publishes `PatientCreatedIntegrationEvent` **natively** (not via `PublishToMassTransitExchange`).
2. `Billing.WebApi`: add a **native** Wolverine listener branch alongside the existing interop branch.
3. Run both services on Wolverine → native W→W, no interop envelope.

## Acceptance criteria

- [ ] With both services on `MessagingFramework=Wolverine`, the Patient → BillingProfile flow runs end-to-end via **native Wolverine** messaging (no MassTransit-interop envelope), verified on RabbitMQ.
- [ ] The same native W→W flow runs on a **real Azure Service Bus namespace**.
- [ ] MT→MT and the existing MT→W (RabbitMQ-only) flows are unchanged; the ASB guards still fire for unsupported combinations.
- [ ] Native-vs-interop selection is driven by configuration/alignment, not runtime detection of the producer's framework.
- [ ] The supported-combinations matrix (issue #5) is updated to reflect native W→W once implemented.

## Out of scope

- CloudEvents / framework-neutral published language (the `O(frameworks)` generalisation).
- Porting the MT↔W interop bridge to Azure Service Bus topics/subscriptions (ADR-0001 descoped this).
- Wolverine → MassTransit interop in any broker.
- Real-namespace production hardening (managed identity, IaC provisioning, topology management).
- W→W on the ASB **emulator** specifically (blocked by WolverineFx 4.12.2; revisit when the package supports a separate admin client).
