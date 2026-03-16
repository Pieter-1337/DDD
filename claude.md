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
05. Frontend/
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
- **MassTransit** - .NET service bus abstraction
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
**Current**: Phase 7 - Frontend (Blazor Server)

### Completed Phases
- ✅ Phase 1: DDD Fundamentals
- ✅ Phase 2: Persistence with EF Core
- ✅ Phase 3: CQRS Pattern
- ✅ Phase 4: Testing
- ✅ Phase 5: Event-Driven Architecture (MassTransit, RabbitMQ, Integration Events)
- ✅ Phase 6: Integration (Aspire orchestration, Billing BC, Observability)

## Learning Resources Referenced
- Pluralsight: "Domain-Driven Design Fundamentals" (Julie Lerman & Steve Smith)
- Pluralsight: "Domain-Driven Design in Practice" (Vladimir Khorikov)
- Pluralsight: "CQRS in Practice" (Vladimir Khorikov)
- Pluralsight: "Building Event-Driven Microservices with MassTransit" (Roland Guijt)
- Microsoft eBook: ".NET Microservices: Architecture for Containerized .NET Applications"

## Next Steps
1. Build Blazor Server frontend with FluentUI (Phase 7)
2. Patient management UI (list, create, detail, suspend)
3. API integration with typed HttpClient and Aspire service discovery
4. Explore API Gateway with YARP (Phase 8, optional)

## Notes for Claude Code
- When suggesting code, follow the DDD tactical patterns strictly
- Prioritize domain modeling over technical concerns
- Ask clarifying questions about domain concepts
- Suggest tests for domain logic
- Point out violations of DDD principles
- Reference learning materials when explaining concepts