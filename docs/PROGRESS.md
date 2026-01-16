# Learning Progress Tracker

## Overall Status

| Phase | Status | Started | Completed |
|-------|--------|---------|-----------|
| Phase 1: DDD Fundamentals | Complete | 2026-01-05 | 2026-01-05 |
| Phase 2: EF Core Persistence | Complete | 2026-01-05 | 2026-01-07 |
| Phase 3: CQRS Pattern | Complete | 2026-01-07 | 2026-01-16 |
| Phase 4: Testing | In Progress | 2026-01-09 | - |
| Phase 5: Event-Driven Architecture | Not Started | - | - |
| Phase 6: Integration | Not Started | - | - |

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
- [x] MediatR pipeline behaviors

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
- [x] Implement ValidationBehavior
- [x] Implement LoggingBehavior
- [x] Implement PerformanceBehavior
- [x] Implement TransactionBehavior
- [x] Implement UnhandledExceptionBehavior
- [x] Add ExceptionToJsonFilter for error responses
- [x] Implement SmartEnum for PatientStatus
- [x] Implement ErrorCode with SmartEnum pattern

### Key Decisions Made

1. **Validators inline with commands/queries** - Using `#region Validators` in same file
2. **UserValidator base class** - All validators extend `UserValidator<T>` for future role-based validation
3. **ExistsAsync for entity validation** - Efficient existence check without loading entire entity
4. **EmailValidationMode.AspNetCoreCompatible** - Use ASP.NET Core compatible email validation (not obsolete regex)
5. **SuppressAsyncSuffixInActionNames = false** - Keep "Async" suffix in action names for `nameof()` compatibility
6. **SmartEnum for enumerations** - Using Ardalis.SmartEnum instead of C# enums for type safety
7. **ErrorCode with SmartEnum** - Consistent error codes with machine-readable values and human-readable messages

### Docs Available

- `phase-3-cqrs/01-cqrs-introduction.md` - What is CQRS and why
- `phase-3-cqrs/02-commands-and-handlers.md` - Write side implementation
- `phase-3-cqrs/03-queries-and-handlers.md` - Read side implementation
- `phase-3-cqrs/04-validation.md` - FluentValidation integration
- `phase-3-cqrs/05-pipeline-behaviors.md` - MediatR pipeline behaviors

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
// Program.cs - Serialize SmartEnums as their name strings
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new SmartEnumJsonConverterFactory());
});
```

**Note:** We use `SmartEnumJsonConverterFactory` instead of `JsonStringEnumConverter` because all enums in this project use Ardalis.SmartEnum.

---

## Phase 4: Testing

### Concepts Learned

- [x] Two-tier test base hierarchy (ValidatorTestBase → TestBase<TContext>)
- [x] Validator unit tests with mocked IUnitOfWork
- [x] Integration tests with SQLite in-memory database
- [x] Transaction-based test isolation (rollback after each test)
- [x] MSTest with Shouldly, Moq, and NBuilder

### Implementation Progress

- [x] Create `BuildingBlocks.Tests` project
- [x] Create `ValidatorTestBase` for validator unit tests with mocked IUnitOfWork
- [x] Create `TestBase<TContext>` inheriting from ValidatorTestBase
- [x] Add `ShouldContainValidation()` extension for type-safe assertions
- [x] Add FluentValidation error code constants
- [x] Create `SchedulingValidatorTestBase` for Scheduling validator tests
- [x] Create `SchedulingDbTestBase` for Scheduling integration tests
- [x] Implement transaction-based isolation
- [x] Write validator tests (CreatePatientCommand, GetPatientQuery, GetAllPatientsQuery, SuspendPatientCommand)
- [x] Write handler tests (CreatePatientCommand, GetPatientQuery, GetAllPatientsQuery)
- [x] Write domain tests (Patient entity behavior)

### Key Decisions Made

1. **Two-tier test base hierarchy** - ValidatorTestBase for unit tests, TestBase for integration tests
2. **Mocked IUnitOfWork for validators** - Fast unit tests without database
3. **Transaction rollback** - Each integration test starts fresh without recreating database
4. **`ShouldContainValidation()` with `nameof()`** - Type-safe validation assertions
5. **MSTest for consistency** - Matches reference architecture conventions
6. **Generic TestBase in BuildingBlocks** - Reusable across all bounded contexts

### Test Organization

| Test Type | Base Class | Database |
|-----------|------------|----------|
| Validator unit tests | `SchedulingValidatorTestBase` | Mocked |
| Handler integration tests | `SchedulingDbTestBase` | SQLite |
| Domain entity tests | None | None |

### Docs Available

- `phase-4-testing/01-testing-overview.md` - Why testing, test types, when to use which
- `phase-4-testing/02-test-infrastructure.md` - BuildingBlocks.Tests, base classes
- `phase-4-testing/03-validator-tests.md` - Writing validator unit tests
- `phase-4-testing/04-handler-tests.md` - Writing handler integration tests
- `phase-4-testing/05-domain-tests.md` - Writing domain entity tests

---

## Phase 5: Event-Driven Architecture

*Not started*

### Docs Available

- `phase-5-event-driven/` - (to be created)

### Planned Topics

- Domain Events vs Integration Events
- RabbitMQ setup with Docker
- MassTransit for .NET integration
- Event publishing and subscribing
- Saga patterns
- Idempotent message handlers

---

## Phase 6: Integration

*Not started*

### Docs Available

- `phase-6-integration/` - (to be created)

### Planned Topics

- Multiple bounded contexts
- Event-driven communication
- CQRS in each service
- API Gateway pattern
- Health checks and observability
