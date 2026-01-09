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

## Phase 1: DDD Fundamentals

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

## Phase 2: EF Core Persistence

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

### Concepts Learned

- [x] Commands and Command Handlers (write side)
- [x] Queries and Query Handlers (read side)
- [x] DTOs for query responses
- [x] Command validation with FluentValidation
- [ ] MediatR pipeline behaviors (documented, not implemented)

### Implementation Progress

- [x] Create Command/Query folder structure
- [x] Implement CreatePatientCommand and handler
- [x] Implement SuspendPatientCommand and handler
- [x] Implement GetPatientByIdQuery and handler
- [x] Implement GetAllPatientsQuery and handler
- [x] Add PatientDto
- [x] Create command validators (inline with commands)
- [x] Create query validators (inline with queries)
- [x] Update controller to use MediatR
- [ ] Implement ValidationBehavior (optional)
- [ ] Implement LoggingBehavior (optional)
- [ ] Implement PerformanceBehavior (optional)
- [ ] Add exception handling middleware (optional)

### Key Decisions Made

1. **Validators inline with commands/queries** - Using `#region Validators` in same file
2. **UserValidator base class** - All validators extend `UserValidator<T>` for future role-based validation
3. **ExistsAsync for entity validation** - Efficient existence check without loading entire entity
4. **EmailValidationMode.AspNetCoreCompatible** - Use ASP.NET Core compatible email validation (not obsolete regex)
5. **SuppressAsyncSuffixInActionNames = false** - Keep "Async" suffix in action names for `nameof()` compatibility

### Docs Available

- `phase-3-cqrs/01-cqrs-introduction.md` - What is CQRS and why
- `phase-3-cqrs/02-commands-and-handlers.md` - Write side implementation
- `phase-3-cqrs/03-queries-and-handlers.md` - Read side implementation
- `phase-3-cqrs/04-validation.md` - FluentValidation integration
- `phase-3-cqrs/05-pipeline-behaviors.md` - MediatR pipeline behaviors

---

## Testing

### Testing Approach

Following RefArch integration testing patterns:

- **Framework**: MSTest (`[TestClass]`, `[TestMethod]`, `[TestInitialize]`, `[TestCleanup]`)
- **Assertions**: Shouldly
- **Test Data**: NBuilder (FizzWare.NBuilder)
- **Database**: SQLite in-memory
- **Isolation**: Transaction-based (rollback after each test)

### Test Project Structure

```
Core/Scheduling/Scheduling.Domain.Tests/
+-- Common/
|   +-- TestBase.cs                    <- Base class with DI, transactions, helpers
+-- ApplicationTests/
|   +-- HandlerTests/
|       +-- CreatePatientCommandHandlerTests.cs
|       +-- GetPatientQueryHandlerTests.cs
|       +-- GetAllPatientsQueryHandlerTests.cs
+-- DomainTests/
    +-- Patients/
        +-- PatientTests.cs
```

### TestBase Features

- SQLite in-memory database (connection kept open)
- Full DI container with real Mediator, UnitOfWork, Validators
- Transaction-based isolation (begin in TestInitialize, rollback in TestCleanup)
- Helper methods: `GetMediator()`, `ValidatorFor<T>()`, `Uow`, `DbContext`
- Stopwatch helpers for performance assertions
- FluentValidation error code constants
- NBuilder configuration for entity ID handling

### Key Testing Decisions

1. **Integration tests over unit tests** - Test full pipeline with real dependencies
2. **Transaction rollback** - Each test starts fresh without recreating database
3. **Real validators in pipeline** - Tests exercise actual validation logic
4. **MSTest for consistency** - Matches reference architecture conventions

---

## Configuration

### ASP.NET Core Settings

```csharp
// Program.cs - Keep Async suffix in action names
builder.Services.AddControllers(options =>
{
    options.SuppressAsyncSuffixInActionNames = false;
})
```

This allows using `nameof(GetPatientAsync)` in `CreatedAtAction` calls.

### JSON Serialization

```csharp
// Program.cs - Serialize enums as strings
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
```

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
