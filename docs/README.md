# DDD Learning Documentation

This directory contains learning notes, explanations, and progress tracking for the DDD & Event-Driven Architecture learning project.

## Structure

```
docs/
+-- README.md                    # This file
+-- PROGRESS.md                  # Overall progress tracker
+-- IMPROVEMENTS.md              # Identified gaps and future improvements
+-- ARCHITECTURE-COMPARISON.md   # DDD vs Layered Monolith (ref-arch) comparison
+-- phase-1-ddd-fundamentals/    # DDD tactical patterns
+-- phase-2-ef-core/             # Persistence with EF Core
+-- phase-3-cqrs/                # CQRS pattern
+-- phase-4-testing/             # Integration testing setup
+-- phase-5-event-driven/        # Event-driven architecture
+-- phase-6-integration/         # Complete system integration
+-- phase-7-frontend/            # Frontend (Blazor + Angular)
+-- phase-8-auth/                # Authentication & Authorization (planned)
+-- phase-9-api-gateway-bff/     # API Gateway (YARP) & BFF pattern (optional)
```

## How to Use

Each phase directory contains:
- Concept explanations (the "why")
- Code examples with annotations
- Decisions made and reasoning
- Questions and answers from learning sessions

## Current Phase

**Phase 7: Frontend** - In Progress

Building user interfaces for the DDD learning project:
- Blazor Server with FluentUI components
- Angular SPA with Angular Material
- Patient management UI (list, create, detail, suspend, activate)
- API integration for both frontends

### Event Architecture (Established in Phase 5)

| Type | Purpose | Transport |
|------|---------|-----------|
| **Domain Events** | Internal decoupling within bounded context | MediatR (in-memory) |
| **Integration Events** | Cross-bounded-context communication | MassTransit/RabbitMQ |

**Event Flow**: Entity raises domain event → SaveChangesAsync() → DispatchDomainEventsAsync() (MediatR) → PublishQueuedIntegrationEventsAsync() (MassTransit/RabbitMQ)

See [PROGRESS.md](./PROGRESS.md) for detailed status.
