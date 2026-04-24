# DDD & Event-Driven Architecture Learning Project

## Overview
This is a comprehensive learning project to master Domain-Driven Design (DDD) and Event-Driven Architecture patterns. The project builds incrementally, implementing each concept step-by-step.

## Developer Context
- **Name**: Pieter
- **Tech Stack**: C# .NET 9, Blazor Server
- **Current Role**: .NET Developer
- **Azure Experience**: .NET, C#, Cosmos DB, SQL Server, Azure Service Bus, Azure App Configuration
- **Goal**: Deep dive into DDD, event-driven architecture, RabbitMQ, SQL Server/EF Core, CQRS

## Project Structure & Learning Path

### Phase 1: DDD Fundamentals
**Goal**: Build a solid foundation in tactical DDD patterns

**Concepts to implement**:
- Entities vs Value Objects
- Aggregates and Aggregate Roots
- Domain Events
- Repositories
- Domain Services
- Bounded Contexts

**Practice Domain**: Healthcare appointment scheduling
- Patient aggregate
- Appointment aggregate
- Doctor aggregate
- Bounded contexts: Scheduling, Billing, Medical Records

### Phase 2: Persistence with EF Core
**Goal**: Implement DDD patterns with SQL Server and Entity Framework Core

**Concepts to implement**:
- Repository pattern with EF Core
- Unit of Work pattern
- Mapping aggregates to tables
- Owned entities for value objects
- Domain event handling with EF interceptors
- Optimistic concurrency
- Aggregate persistence strategies

**Database**: SQL Server (LocalDB for development)

### Phase 3: CQRS Pattern
**Goal**: Separate read and write models

**Concepts to implement**:
- Command handlers (write side)
- Query handlers (read side)
- Separate read and write database models
- Command validation
- Query optimization
- MediatR for CQRS implementation

### Phase 4: Testing
**Goal**: Establish test infrastructure for confidence and regression prevention

**Concepts to implement**:
- Two-tier test base hierarchy (ValidatorTestBase, TestBase<TContext>)
- Validator unit tests with mocked IUnitOfWork
- Handler integration tests with SQLite in-memory
- Domain entity unit tests
- Transaction-based test isolation
- MSTest, Shouldly, Moq, NBuilder

### Phase 5: Event-Driven Architecture
**Goal**: Implement asynchronous communication between bounded contexts

**Concepts to implement**:
- Domain events vs Integration events
- RabbitMQ for message bus
- MassTransit for .NET integration
- Event publishing and subscribing
- Saga patterns for distributed transactions
- Event versioning and schema evolution
- Idempotent message handlers
- Dead letter queues and error handling

**Message Broker**: RabbitMQ (running in Docker)

> **Note**: Docker container orchestration has been migrated to .NET Aspire in Phase 6. The `docker-compose.yml` is kept as a CI/CD fallback.

### Phase 6: Integration
**Goal**: Build a cohesive system integrating all concepts

**System features**:
- Multiple microservices/bounded contexts
- Event-driven communication
- CQRS in each service
- Shared kernel for common domain concepts
- Health checks and observability

### Phase 7: Frontend
**Goal**: Build a client application that consumes the WebApis

**Concepts implemented (Angular track)**:
- Angular 21 standalone components with `inject()` DI
- Signals for state (`signal`, `computed`) and zoneless change detection
- Feature routing with lazy-loaded components and functional guards (`authGuard`, `roleGuard`)
- HTTP interceptor adding `withCredentials` + `X-Requested-With` and handling 401 → login redirect
- Angular Material navbar with `mat-menu` user controls and role-based `@if` blocks
- Patient list/detail features against the Scheduling API

**Tracks**:
- Angular track — complete
- Blazor Server track — deferred (docs written, implementation not pursued)

### Phase 8: Authentication & Authorization
**Goal**: Secure the APIs and SPA with standards-based auth while keeping authorization testable

**Concepts implemented**:
- Duende IdentityServer 7.4 as the OIDC/OAuth2 authority with EF Core config + operational stores
- ASP.NET Core Identity for user/role storage, seeded with Admin/Doctor/Nurse users
- Cookie auth + OIDC client per WebApi (BFF pattern — SPA never sees tokens)
- Shared Data Protection keys so cookies decrypt across both WebApis
- `id_token`-only cookie storage via `OnTokenValidated` — needed as `id_token_hint` for Duende logout to populate `PostLogoutRedirectUri`
- Claim hydration via `GetClaimsFromUserInfoEndpoint = true` + explicit `MapJsonKey` entries (Duende's lean `id_token` default + .NET 9 `JsonWebTokenHandler` no-remapping behavior)
- `ICurrentUser` abstraction in `BuildingBlocks.Application.Auth`, implemented by `HttpContextCurrentUser`
- `UserValidator<T>` base that auto-registers a FluentValidation role rule from constructor role groups (AND/OR via nested arrays)
- `ERR_FORBIDDEN` mapped to HTTP 403 by `ValidationErrorWrapper` — same response body as 400, only status differs
- Angular role-gated UI via `AuthService.hasRole(...)` + computed signals (`canDelete`, `canSuspend`)

## Coding Standards

### C# Conventions
- Use C# 12 features (primary constructors, collection expressions)
- Prefer records for value objects
- Use required properties where applicable
- Follow async/await best practices
- Use nullable reference types

### Project Organization
```
BuildingBlocks/
├── BuildingBlocks.Domain/              # Base entities, value objects, domain event interfaces
├── BuildingBlocks.Application/         # CQRS behaviors, pipeline, interfaces
├── BuildingBlocks.Infrastructure.EfCore/  # Unit of Work, repository base
├── BuildingBlocks.Infrastructure.MassTransit/  # Event bus, integration event consumers
├── BuildingBlocks.Infrastructure.Wolverine/  # Wolverine event bus, MIT-licensed alternative
├── BuildingBlocks.WebApplications/     # API filters, JSON config, OpenAPI
├── BuildingBlocks.Enumerations/        # Smart enum base
└── BuildingBlocks.Tests/               # Shared test base classes
Core/
├── Scheduling/
│   ├── Scheduling.Domain/             # Patient aggregate, domain events
│   ├── Scheduling.Application/        # Commands, queries, handlers, validators
│   ├── Scheduling.Infrastructure/     # EF Core DbContext, migrations, configs
│   └── Scheduling.Domain.Tests/       # Domain & handler tests
├── Billing/
│   ├── Billing.Domain/               # BillingProfile aggregate
│   ├── Billing.Application/          # Commands, handlers
│   ├── Billing.Infrastructure/       # DbContext, migrations, consumers
│   └── Billing.Tests/                # Billing tests
Shared/
└── IntegrationEvents/                 # Cross-bounded-context integration events
WebApplications/
├── Scheduling.WebApi/                 # Scheduling API host
└── Billing.WebApi/                    # Billing API host
Frontend/
├── Blazor/
│   └── Scheduling.BlazorApp/         # Blazor Server frontend
└── Angular/
    └── Scheduling.AngularApp/        # Angular SPA frontend
Aspire.AppHost/                        # .NET Aspire orchestrator
Aspire.ServiceDefaults/                # Shared OpenTelemetry, health checks, resilience
```

### DDD Rules to Enforce
- Aggregates accessed only through root
- No public setters on domain entities (encapsulation)
- Domain events for state changes
- Value objects are immutable
- Business logic in domain, not services
- Repositories only for aggregate roots

### Naming Conventions
- Commands: `CreateAppointmentCommand`, `CancelAppointmentCommand`
- Events: `AppointmentCreatedEvent`, `AppointmentCancelledEvent`
- Handlers: `CreateAppointmentCommandHandler`, `AppointmentCreatedEventHandler`
- Queries: `GetAppointmentByIdQuery`, `GetPatientAppointmentsQuery`

## Technology Decisions

### Core Stack
- **.NET 9** - Latest LTS
- **C# 12** - Latest language features
- **EF Core 9** - ORM for SQL Server
- **SQL Server** - Primary database
- **RabbitMQ** - Message broker
- **MassTransit** - .NET service bus abstraction (default messaging framework)
- **Wolverine** - MIT-licensed messaging alternative (switchable via `MessagingFramework` config)
- **MediatR** - CQRS implementation
- **FluentValidation** - Command/query validation
- **Polly** - Resilience and transient fault handling
- **Ardalis.SmartEnum** - Smart enumerations
- **.NET Aspire** - Orchestration and service defaults

### Testing
- **MSTest** - Test framework
- **Shouldly** - Primary assertion library
- **FluentAssertions** - Additional assertions
- **Moq** - Mocking framework
- **NBuilder** - Test data generation

### Development Tools
- **.NET Aspire** - Container orchestration, service discovery, observability dashboard
- **Docker** - RabbitMQ container (managed by Aspire for local dev, docker-compose kept as fallback)
- **Scalar** - API documentation (OpenAPI)

## Docker Services

RabbitMQ is managed by .NET Aspire for local development. The `docker-compose.yml` is kept as a fallback for CI/CD or running without Aspire. The MSBuild docker check target in `Directory.Build.targets` is currently commented out since Aspire handles container orchestration.

```yaml
# Aspire.AppHost/AppHost.cs manages:
# - RabbitMQ with management plugin
# - Scheduling.WebApi
# - Billing.WebApi
# SQL Server connection is via user secrets (not managed by Aspire)
```

## Current Phase
**Current**: Phase 9 - API Gateway with YARP & BFF pattern (optional, not yet started)

### Completed Phases
- ✅ Phase 1: DDD Fundamentals
- ✅ Phase 2: Persistence with EF Core
- ✅ Phase 3: CQRS Pattern
- ✅ Phase 4: Testing
- ✅ Phase 5: Event-Driven Architecture (MassTransit, RabbitMQ, Integration Events)
- ✅ Phase 6: Integration (Aspire orchestration, Billing BC, Observability)
- ✅ Phase 7: Frontend — Angular track (patient list/detail, routing, guards, interceptors, navbar)
- ✅ Phase 8: Authentication & Authorization (Duende IdentityServer, OIDC+cookies, role-based `UserValidator<T>`, Angular role-gated UI)

### Phase 7 Status
- **Angular track**: Complete
- **Blazor track**: Deferred — docs written but implementation not pursued after the Angular track covered the frontend scope

## Learning Resources Referenced
- Pluralsight: "Domain-Driven Design Fundamentals" (Julie Lerman & Steve Smith)
- Pluralsight: "Domain-Driven Design in Practice" (Vladimir Khorikov)
- Pluralsight: "CQRS in Practice" (Vladimir Khorikov)
- Pluralsight: "Building Event-Driven Microservices with MassTransit" (Roland Guijt)
- Microsoft eBook: ".NET Microservices: Architecture for Containerized .NET Applications"

## Next Steps
1. (Optional) Implement Phase 9 — API Gateway with YARP and the BFF pattern in front of the two WebApis
2. (Optional) Resume the Blazor track of Phase 7 if a second frontend is needed
3. (Optional) Additional hardening — integration tests hitting the real IdentityServer, production-hardened Data Protection key storage, token refresh flows

## Notes for Claude Code
- When suggesting code, follow the DDD tactical patterns strictly
- Prioritize domain modeling over technical concerns
- Ask clarifying questions about domain concepts
- Suggest tests for domain logic
- Point out violations of DDD principles
- Reference learning materials when explaining concepts