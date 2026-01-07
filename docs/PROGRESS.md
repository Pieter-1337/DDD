# Learning Progress Tracker

## Overall Status

| Phase | Status | Started | Completed |
|-------|--------|---------|-----------|
| Phase 1: DDD Fundamentals | Complete | 2026-01-05 | 2026-01-05 |
| Phase 2: EF Core Persistence | Complete | 2026-01-05 | 2026-01-07 |
| Phase 3: CQRS Pattern | In Progress | 2026-01-07 | - |
| Phase 4: Event-Driven | Not Started | - | - |
| Phase 5: Integration | Not Started | - | - |

---

## Phase 1: DDD Fundamentals ✓

### Concepts Learned

- [x] Why DDD? - Understanding the problem it solves
- [x] Entities - Objects with identity (Patient)
- [x] Aggregates & Aggregate Roots - Consistency boundaries
- [x] Domain Events - Communicating state changes
- [x] Repositories - Persistence abstraction (interface)
- [x] Value Objects - Discussed, using primitives pragmatically

### Implementation Complete

- [x] Project structure setup (Clean Architecture)
- [x] Patient Entity / Aggregate Root
- [x] PatientStatus enum
- [x] Domain events (PatientCreated, PatientSuspended)
- [x] Entity base class with event support
- [x] Generic repository interface

### Key Decisions Made

1. **Using Guid directly** - No PatientId wrapper, pragmatic choice
2. **Primitives for simple values** - Email as string, not value object
3. **Complex values get IDs** - If it needs its own table, make it an entity
4. **Generic repository base** - `IRepository<T>` and `IUnitOfWork.RepositoryFor<T>()`

---

## Phase 2: EF Core Persistence ✓

### Concepts Learned

- [x] EF Core DbContext setup
- [x] Fluent API entity configuration
- [x] Repository implementation with EF Core
- [x] Unit of Work pattern
- [x] Database migrations
- [x] Domain event dispatching

### Implementation Complete

- [x] Add EF Core packages to Infrastructure
- [x] Create SchedulingDbContext
- [x] Create PatientConfiguration (Fluent API)
- [x] Implement generic Repository<TContext, TEntity>
- [x] Implement UnitOfWork<TContext> with RepositoryFor<T>()
- [x] Create database migrations
- [x] Add domain event dispatcher
- [x] Test with API endpoint

### Key Decisions Made

1. **BuildingBlocks split** - Separated into `BuildingBlocks.Domain` (pure abstractions) and `BuildingBlocks.Infrastructure` (EF Core implementations)
2. **Generic repository** - `UnitOfWork.RepositoryFor<T>()` instead of entity-specific repositories
3. **Event dispatching after save** - Events fire only after successful database commit

### Docs Available

- `phase-2-ef-core/01-setup-efcore.md` - DbContext and configuration
- `phase-2-ef-core/02-repository-implementation.md` - Repository pattern
- `phase-2-ef-core/03-database-migrations.md` - Creating the database
- `phase-2-ef-core/04-domain-event-dispatching.md` - Publishing events

---

## Phase 3: CQRS Pattern

### Concepts to Learn

- [ ] Commands and Command Handlers (write side)
- [ ] Queries and Query Handlers (read side)
- [ ] DTOs for query responses
- [ ] Command validation with FluentValidation
- [ ] MediatR pipeline behaviors

### Implementation Progress

- [ ] Create Command/Query folder structure
- [ ] Implement CreatePatientCommand and handler
- [ ] Implement SuspendPatientCommand and handler
- [ ] Implement GetPatientByIdQuery and handler
- [ ] Implement GetAllPatientsQuery and handler
- [ ] Add PatientDto and PatientListDto
- [ ] Create command validators
- [ ] Implement ValidationBehavior
- [ ] Implement LoggingBehavior
- [ ] Implement PerformanceBehavior
- [ ] Update controller to use MediatR
- [ ] Add exception handling middleware

### Docs Available

- `phase-3-cqrs/01-cqrs-introduction.md` - What is CQRS and why
- `phase-3-cqrs/02-commands-and-handlers.md` - Write side implementation
- `phase-3-cqrs/03-queries-and-handlers.md` - Read side implementation
- `phase-3-cqrs/04-validation.md` - FluentValidation integration
- `phase-3-cqrs/05-pipeline-behaviors.md` - MediatR pipeline behaviors

---

## Phase 4: Event-Driven Architecture

*Not started*

### Planned Topics

- Domain Events vs Integration Events
- RabbitMQ setup with Docker
- MassTransit for .NET integration
- Event publishing and subscribing
- Saga patterns
- Idempotent message handlers

---

## Phase 5: Integration

*Not started*

### Planned Topics

- Multiple bounded contexts
- Event-driven communication
- CQRS in each service
- API Gateway pattern
- Health checks and observability
