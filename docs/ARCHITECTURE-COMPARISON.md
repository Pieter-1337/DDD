# Architecture Comparison — DDD vs Layered Monolith

**Last reviewed**: 2026-03-12

## Purpose

This document captures the architectural differences between this DDD learning project and the AXI `ref-arch-dotnet-core`, to inform a potential future learning track for the layered monolith approach.

The two architectures serve different purposes and solve different problems. Understanding their differences helps in choosing the right approach for a given context and provides a foundation for exploring both paradigms.

## Layered Monolith vs Modular Monolith — Clarification

It's important to distinguish between two architectural styles that are often confused:

### Layered Monolith (What the Ref-Arch Is)

Organized by **technical concern**. All entities in one Domain project, all handlers in one Handling project, all repositories in one Repositories project.

**Key characteristic**: To find everything related to "School", you jump across 8+ projects (Domain, Commands, Queries, Dtos, Repositories, Mapping, BusinessManagers, Handling, Validation).

**Boundaries**: Technical layers are enforced. You cannot call the database from the Web project directly. But nothing prevents a School handler from touching any other entity.

### Modular Monolith (What the Ref-Arch Is NOT)

Organized by **business capability**. A "School" module contains its own domain, handlers, repositories, and DTOs with enforced boundaries.

**Key characteristic**: To find everything related to "School", you open the `School` module folder.

**Boundaries**: Module boundaries are enforced. Modules communicate through explicit public contracts (integration events, shared APIs), not by importing each other's entities. You cannot access another module's entities or database directly.

### Why This Matters

The ref-arch has clean layers and good patterns (CQRS, pipeline behaviors, Unit of Work, validation), but no module boundaries. One `DbContext`, one Domain project, one Handling project. Nothing prevents a handler from touching any entity in the system.

This is not a criticism — it's a design decision. Layered monoliths are simpler to build and understand. Module boundaries add complexity that's only valuable when you have genuinely independent business capabilities.

## Why the Ref-Arch Is Not DDD

The ref-arch follows a **data-centric** approach, while DDD is **behavior-centric**. Specific indicators:

### 1. Database-First Entity Generation

The ref-arch uses the `efg8` tool to generate entities from database tables. The database schema is the source of truth.

**Opposite of**: Domain-first modeling, where you model behavior first and derive the database schema from aggregates.

### 2. Anemic Domain Model

Entities are data containers with audit fields (`CreatedOn`, `UpdatedOn`, `IsDeleted`). Business logic lives in `BusinessManagers` (service layer).

Example:
```csharp
// Ref-arch entity
public class School : IAuditable, ISoftDeletable
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Address { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime? UpdatedOn { get; set; }
    public bool IsDeleted { get; set; }
}

// Business logic in manager
public class SchoolManager
{
    public async Task<SchoolDto> InsertOrUpdateAsync(SchoolDto dto)
    {
        // Validation, mapping, repository call
    }
}
```

**Opposite of**: Rich domain models where business logic lives inside the aggregate.

### 3. No Aggregate Boundaries

Nothing prevents reaching through `School` to modify a `Student` directly. All entities are first-class citizens with their own repositories.

**Opposite of**: Aggregate boundaries where you can only access `Student` through the `School` aggregate root.

### 4. No Domain Events

State changes don't raise events. If you want to notify other parts of the system, you do it explicitly in the handler or manager.

**Opposite of**: Domain events raised when invariants change, decoupling side effects from the primary operation.

### 5. No Value Objects

Everything is primitives or DTOs. `Address` is `string Address`, not `Address` value object with validation.

**Opposite of**: Value objects for concepts like `Address`, `DateRange`, `Money` with validation rules and equality semantics.

### 6. AutoMapper Bidirectional Mapping

DTOs map straight to/from entities with `CreateMap<School, SchoolDto>().ReverseMap()`. The boundary between domain and presentation is porous.

**Opposite of**: Manual mapping or projections that enforce a clear boundary between domain model and external representation.

---

**Note**: This is not a criticism. The ref-arch is a perfectly valid architecture for many enterprise applications — it's just data-centric rather than behavior-centric. It's optimized for CRUD operations, rapid development, and teams familiar with traditional n-tier architecture.

## Side-by-Side Comparison

| Aspect | Ref-Arch (Layered Monolith) | DDD Track (This Project) |
|--------|---------------------------|--------------------------|
| **Starting point** | Database schema | Domain behavior |
| **Entity design** | Data containers + managers | Rich domain models |
| **Business logic location** | BusinessManagers (service layer) | Inside aggregates |
| **State changes** | Manager calls repo directly | Domain events |
| **Validation** | Pipeline only (FluentValidation pre-processor) | Domain invariants + pipeline validation |
| **Organization** | By technical layer | By bounded context |
| **Mapping** | AutoMapper bidirectional | Manual mapping / projections |
| **Database approach** | Database-first (code generation) | Code-first (EF Core migrations) |
| **Entity generation** | `efg8` tool from tables | Hand-crafted domain models |
| **Cross-cutting** | Soft deletes, audit fields on all entities | Per-aggregate decisions |
| **IoC container** | LightInject with CompositionRoot pattern | Built-in .NET DI |
| **Communication between concerns** | Direct project references, shared Domain | Integration events (MassTransit/RabbitMQ) |
| **Deployment** | Single deployable | Multiple services (microservices) |
| **Orchestration** | N/A (single process) | .NET Aspire |
| **Testing approach** | In-memory database | SQLite in-memory + mocked UoW |
| **Module boundaries** | None (all handlers can access all entities) | Enforced (bounded contexts) |
| **DbContext** | One shared context | One per bounded context |
| **Consistency** | ACID transactions within monolith | Eventual consistency across contexts |

## Ref-Arch Project Structure

```
Solution Structure:

0. Database/
   - RefArch.DotNetCore.Database (SQL project - SSDT)
   - RefArch.DotNetCore.Database.Sdk (entity generation)
   - RefArch.DotNetCore.Database.Migration (migrations)

1. Core/
   - RefArch.DotNetCore.Domain (entities, interfaces)
   - RefArch.DotNetCore.Commands (CQRS write-side)
   - RefArch.DotNetCore.Queries (CQRS read-side)
   - RefArch.DotNetCore.Dtos (data transfer objects)

2. Infrastructure/
   - RefArch.DotNetCore.Repositories (EF Core, UoW)
   - RefArch.DotNetCore.Mapping (AutoMapper profiles)
   - RefArch.DotNetCore.CrossCutting (utilities, exceptions)
   - RefArch.DotNetCore.ServiceAgents (external integrations)

3. Application/
   - RefArch.DotNetCore.BusinessManagers (business logic)
   - RefArch.DotNetCore.Handling (MediatR handlers)
   - RefArch.DotNetCore.Validation (FluentValidation)

4. Web Applications/
   - RefArch.DotNetCore.Web.Api (ASP.NET Core API)

Common/
   - RefArch.DotNetCore.Common (shared utilities)
   - RefArch.DotNetCore.Resources (localization)
   - RefArch.DotNetCore.DependencyResolution (LightInject composition)
```

**Key observations**:
- One Domain project for all entities
- One Handling project for all handlers
- One Repositories project with one `DbContext`
- All business logic in BusinessManagers
- Separation by technical concern, not business capability

## DDD Track Project Structure (This Project)

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

Aspire.AppHost/                        # .NET Aspire orchestrator
Aspire.ServiceDefaults/                # Shared OpenTelemetry, health checks, resilience
```

**Key observations**:
- Organized by bounded context (Scheduling, Billing)
- Each context has its own Domain, Application, Infrastructure
- Each context has its own `DbContext`
- Communication via integration events
- BuildingBlocks for shared infrastructure (not shared domain)

## Request Flow Comparison

### Ref-Arch Flow: Save School

```
POST /api/schools (Controller)
  |
  ├─> SaveSchoolCommand dispatched (MediatR)
  |
  ├─> TransactionBehavior wraps in transaction
  |
  ├─> ValidationPreProcessor validates command
  |
  ├─> SaveSchoolHandler.Handle()
  |     |
  |     ├─> SchoolManager.InsertOrUpdateAsync(SchoolDto)
  |     |     |
  |     |     ├─> AutoMapper maps DTO -> Entity
  |     |     |
  |     |     ├─> IEntityMapper.Map<School>(entity) (audit fields, soft delete)
  |     |     |
  |     |     ├─> Uow.RepositoryFor<School>().Insert(school)
  |     |     |
  |     |     └─> AutoMapper maps Entity -> DTO for response
  |     |
  |     └─> Return DTO
  |
  ├─> Transaction commits
  |
  └─> Response returned
```

**Characteristics**:
- Entity is passive data container
- Manager orchestrates mapping, validation, repository
- No domain events
- AutoMapper for bidirectional mapping
- Audit fields applied via `IEntityMapper`

### DDD Track Flow: Create Patient

```
POST /api/patients (Controller)
  |
  ├─> CreatePatientCommand dispatched (MediatR)
  |
  ├─> ValidationBehavior validates command (FluentValidation)
  |
  ├─> TransactionBehavior wraps in transaction
  |
  ├─> CreatePatientCommandHandler.Handle()
  |     |
  |     ├─> Patient.Create() — domain logic
  |     |     |
  |     |     ├─> Validates invariants (name, DOB, MRN unique)
  |     |     |
  |     |     └─> Raises PatientCreatedDomainEvent
  |     |
  |     ├─> Uow.RepositoryFor<Patient>().Insert(patient)
  |     |
  |     └─> Return PatientResponse (manual mapping)
  |
  ├─> SaveChangesAsync()
  |
  ├─> DispatchDomainEventsAsync() (MediatR, in-process)
  |     |
  |     └─> Domain event handlers execute
  |           |
  |           └─> Queue integration events
  |
  ├─> PublishQueuedIntegrationEventsAsync() (MassTransit, cross-service)
  |     |
  |     └─> PatientRegisteredIntegrationEvent published to RabbitMQ
  |
  ├─> Transaction commits
  |
  └─> Response returned
```

**Characteristics**:
- Entity actively decides and raises events
- Handler coordinates but doesn't contain business logic
- Domain events decouple side effects
- Manual mapping for response
- Integration events for cross-context communication

**Key difference**: In the ref-arch, the entity is a passive data container that gets things done TO it. In DDD, the entity actively decides and raises events.

## Shared Patterns

Despite the architectural differences, both approaches share several patterns:

- **CQRS via MediatR**: Commands and queries are separated
- **Unit of Work + Repository**: Abstraction over data access
- **FluentValidation**: Command/query validation in pipeline
- **Pipeline Behaviors**: Transaction, validation, logging
- **Structured Project Organization**: Clear separation of concerns
- **Dependency Injection**: Both use DI containers (LightInject vs built-in)
- **EF Core**: Both use Entity Framework Core for persistence
- **ASP.NET Core Web API**: Both expose RESTful APIs

This shows that the architectural differences are in the application of these patterns, not in the patterns themselves.

## When to Use Which

### Use Ref-Arch / Layered Monolith When:

- **CRUD-heavy domain**: Most operations are create, read, update, delete with minimal business logic
- **Rapid scaffolding**: Need to build quickly from existing database schema
- **Data-centric design**: Database schema drives the application design
- **Simple business rules**: Validation is mostly structural (required fields, formats)
- **Familiar team**: Team is experienced with traditional n-tier architecture
- **Single deployment**: No need for independent deployment or scaling
- **Stable domain**: Business rules change infrequently

### Use DDD / Bounded Contexts When:

- **Complex business rules**: Domain has rich invariants and behaviors
- **Evolving domain**: Business rules change frequently
- **Multiple bounded contexts**: Clear business capabilities that should be isolated
- **Event-driven requirements**: Need to react to state changes asynchronously
- **Independent scaling**: Different parts of the system have different load characteristics
- **Behavior matters more than data shape**: How things change is more important than what they are
- **Domain expertise available**: Subject matter experts can collaborate on modeling

### Neither Is Inherently Better

These architectures solve different problems:

- **Layered monolith** is simpler, faster to build, easier to understand, and perfectly adequate for many business applications.
- **DDD** is more complex but handles evolving business logic better and provides clearer boundaries for scaling.

The choice depends on your context, team, and domain complexity.

## Potential Future Learning Track

A future learning track for the layered monolith approach could cover:

### Core Concepts
- Layered architecture with CQRS (Commands, Queries, Handling)
- Database-first entity generation (`efg8` workflow)
- BusinessManager pattern (service layer for business logic)
- AutoMapper-driven mapping strategies
- Soft deletes and audit infrastructure
- Transaction management via pipeline behaviors

### Infrastructure
- LightInject with CompositionRoot pattern
- Unit of Work and Repository pattern (layered approach)
- EF Core in a layered architecture
- Validation PreProcessor vs Validation Behavior

### Comparison Points
- When to use layered vs DDD
- Trade-offs: simplicity vs flexibility
- Migration path from layered to modular
- Converting anemic models to rich models

### Reference
This document would serve as the comparison baseline for that track.

---

**Related Documents**:
- [C:\projects\DDD\DDD\CLAUDE.md](C:\projects\DDD\DDD\CLAUDE.md) — Project overview and learning path
- [C:\projects\DDD\DDD\docs\PROGRESS.md](C:\projects\DDD\DDD\docs\PROGRESS.md) — Current progress and phase status
