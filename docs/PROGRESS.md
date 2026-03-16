# Learning Progress Tracker

## Overall Status

| Phase | Status | Started | Completed |
|-------|--------|---------|-----------|
| Phase 1: DDD Fundamentals | Complete | 2026-01-05 | 2026-01-05 |
| Phase 2: EF Core Persistence | Complete | 2026-01-05 | 2026-01-07 |
| Phase 3: CQRS Pattern | Complete | 2026-01-07 | 2026-01-16 |
| Phase 4: Testing | Complete | 2026-01-09 | 2026-01-23 |
| Phase 5: Event-Driven Architecture | Complete | 2026-01-23 | 2026-02-13 |
| Phase 6: Integration | Complete | 2026-03-09 | 2026-03-16 |
| Phase 7: Frontend (Blazor + Angular) | In Progress | 2026-03-12 | - |
| Phase 8: API Gateway & BFF | Not Started | - | - |
| Phase 9: Authentication & Authorization | Not Started | - | - |

---

## Phase 1: DDD Fundamentals

### Concepts Learned

- [x] Why DDD? - Understanding the problem it solves
- [x] Entities - Objects with identity (Patient)
- [x] Aggregates & Aggregate Roots - Consistency boundaries
- [x] Domain Events - Internal decoupling via MediatR (see Phase 5 for integration events)
- [x] Repositories - Persistence abstraction (interface)
- [x] Value Objects - Discussed, using primitives pragmatically

### Implementation Complete

- [x] Project structure setup (Clean Architecture)
- [x] Patient Entity / Aggregate Root
- [x] PatientStatus enum
- [x] Entity base class
- [x] Generic repository interface

### Key Decisions Made

1. **Using Guid directly** - No PatientId wrapper, pragmatic choice
2. **Primitives for simple values** - Email as string, not value object
3. **Complex values get IDs** - If it needs its own table, make it an entity
4. **Generic repository base** - `IRepository<T>` and `IUnitOfWork.RepositoryFor<T>()`
5. **Domain events** - Internal via MediatR, integration events via MassTransit (see Phase 5)

---

## Phase 2: EF Core Persistence

### Concepts Learned

- [x] EF Core DbContext setup
- [x] Fluent API entity configuration
- [x] Repository implementation with EF Core
- [x] Unit of Work pattern
- [x] Database migrations
- [x] Domain event dispatching (via MediatR in UnitOfWork)
- [x] Integration event publishing (via MassTransit in UnitOfWork)

### Implementation Complete

- [x] Add EF Core packages to Infrastructure
- [x] Create SchedulingDbContext
- [x] Create PatientConfiguration (Fluent API)
- [x] Implement generic Repository<TContext, TEntity>
- [x] Implement UnitOfWork<TContext> with RepositoryFor<T>()
- [x] Create database migrations
- [x] Add domain event dispatching via MediatR
- [x] Add integration event publishing after SaveChangesAsync
- [x] Test with API endpoint

### Key Decisions Made

1. **BuildingBlocks split** - Separated into `BuildingBlocks.Domain` (pure abstractions) and `BuildingBlocks.Infrastructure` (EF Core implementations)
2. **Generic repository** - `UnitOfWork.RepositoryFor<T>()` instead of entity-specific repositories
3. **Two-tier event system**:
   - Domain events: dispatched via MediatR for internal decoupling
   - Integration events: queued via `QueueIntegrationEvent()` and published to RabbitMQ after save

### Docs Available

- `phase-2-ef-core/01-setup-efcore.md` - DbContext and configuration
- `phase-2-ef-core/02-repository-implementation.md` - Repository pattern
- `phase-2-ef-core/03-database-migrations.md` - Creating the database
- `phase-2-ef-core/04-domain-event-dispatching.md` - Domain events via MediatR, integration events via MassTransit

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
7. **ErrorCode with SmartEnum** - Consistent error codes with auto-prefixing (`ERR_` for errors, `WRN_` for warnings)
8. **SmartEnum as string in DTOs** - Use `string` type in DTOs, validate with `TryFromName`, convert in handler with `FromName`
9. **MediatR for CQRS only** - MediatR used for commands/queries, NOT for events

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

- [x] Two-tier test base hierarchy (ValidatorTestBase -> TestBase<TContext>)
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
- [x] Write Billing bounded context tests (validators, handlers, domain, event handlers)

### Key Decisions Made

1. **Two-tier test base hierarchy** - ValidatorTestBase for unit tests, TestBase for integration tests
2. **Mocked IUnitOfWork for validators** - Fast unit tests without database
3. **Transaction rollback** - Each integration test starts fresh without recreating database
4. **`ShouldContainValidation()` with `nameof()`** - Type-safe validation assertions
5. **MSTest for consistency** - Out of the box compatibility
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

### Concepts Learned

- [x] Domain events for internal decoupling (via MediatR)
- [x] Integration events for cross-bounded-context communication (via MassTransit)
- [x] Intra-domain messaging for async processing
- [x] RabbitMQ setup with Docker
- [x] MassTransit for .NET integration
- [x] Event publishing via `_uow.QueueIntegrationEvent()`
- [x] `IntegrationEventHandler<T>` base class for automatic logging
- [x] Idempotent message handlers (documented, patterns available)
- [x] Error handling and dead letter queues (documented)
- [x] Saga patterns for distributed workflows (documented, not implemented)
- [x] Event versioning and schema evolution (documented, not implemented)

### Implementation Progress

- [x] Documentation created (all 6 documents)
- [x] `BuildingBlocks.Application/Messaging` created (IEventBus abstraction)
- [x] `BuildingBlocks.Infrastructure.MassTransit` project created
- [x] `IntegrationEventHandler<T>` base class (automatic start/success/error logging)
- [x] `Shared/IntegrationEvents` project created
- [x] `IIntegrationEvent` marker interface
- [x] `IntegrationEventBase` base class
- [x] RabbitMQ Docker setup (docker-compose.yml)
- [x] MassTransit configuration in Infrastructure
- [x] First integration event (`PatientCreatedIntegrationEvent`)
- [x] Event publisher implementation (`MassTransitEventBus`)
- [x] Event consumer implementation (`PatientCreatedIntegrationEventHandler`)
- [x] `IUnitOfWork.QueueIntegrationEvent()` pattern
- [x] Integration event logging (publish + consume)
- [x] End-to-end tested manually (API → RabbitMQ → Consumer)

### Key Decisions Made

1. **Two-tier event system**:
   - **Domain events**: Internal via MediatR for decoupling within bounded context
   - **Integration events**: External via MassTransit/RabbitMQ for cross-BC communication
   - Domain event handlers can queue integration events when crossing boundaries

2. **Event publishing flow**:
   ```
   Entity raises domain event
       ↓
   SaveChangesAsync()
       ↓
   DispatchDomainEventsAsync() → MediatR → Domain event handlers (internal)
       ↓
   PublishQueuedIntegrationEventsAsync() → MassTransit → RabbitMQ (external)
   ```

3. **Project structure**:
   - `BuildingBlocks.Domain` - IDomainEvent, IHasDomainEvents interfaces
   - `BuildingBlocks.Application/Messaging` - IEventBus abstraction
   - `BuildingBlocks.Infrastructure.MassTransit` - MassTransitEventBus, IntegrationEventHandler<T>
   - `BC.Domain/Entity/Events/` - Domain events (internal)
   - `Shared/IntegrationEvents/` - Integration event contracts (external)
   - `BC.Infrastructure/Consumers` - Integration event handlers

4. **IntegrationEventHandler<T> base class**:
   - All integration event handlers inherit from this base class
   - Provides automatic logging: start, success, error
   - Special cases use `IConsumer<T>` directly (internal messages, request/response, fault handlers)

5. **Naming convention**: `{EventName}Handler` for both domain and integration event handlers

### Docs Available

- `phase-5-event-driven/01-event-driven-overview.md` - Domain events vs integration events, full architecture
- `phase-5-event-driven/02-rabbitmq-masstransit-setup.md` - Infrastructure setup, project structure
- `phase-5-event-driven/03-integration-events.md` - Publishing and consuming integration events
- `phase-5-event-driven/04-idempotency-error-handling.md` - DLQ, retries, idempotent handlers
- `phase-5-event-driven/05-sagas-orchestration.md` - Saga pattern for distributed workflows
- `phase-5-event-driven/06-event-versioning.md` - Schema evolution and backwards compatibility

**See also:**
- `phase-1-ddd-fundamentals/04-domain-events.md` - Domain events concept and implementation
- `phase-2-ef-core/04-domain-event-dispatching.md` - How domain events are dispatched via MediatR

---

## Phase 6: Integration

*Complete*

### Concepts to Learn

- [x] .NET Aspire for distributed app orchestration
- [x] AppHost and ServiceDefaults projects
- [x] RabbitMQ with Aspire (replacing manual docker-compose)
- [x] Multiple bounded contexts (Billing) with separate APIs
- [x] Cross-context communication via integration events
- [x] Observability with Aspire Dashboard (logs, traces, metrics)

### Implementation Progress

- [x] Documentation created (all 5 documents)
- [x] Add Aspire AppHost project
- [x] Add ServiceDefaults project
- [x] Migrate RabbitMQ to Aspire orchestration
- [x] Add Billing bounded context (BillingProfile aggregate, PaymentMethod entity)
- [x] Billing database migration (BillingProfiles table)
- [x] Integration event handlers (PatientCreatedIntegrationEventHandler)
- [x] Shared IntegrationEvents project (Scheduling events)
- [x] Billing tests - 11 tests passing (validator, handler, domain, event handler tests)
- [x] Test cross-context event flow end-to-end
- [x] Verify observability in Aspire Dashboard

### Docs Available

- `phase-6-integration/01-aspire-introduction.md` - What is Aspire, why use it
- `phase-6-integration/02-aspire-setup.md` - AppHost and ServiceDefaults setup
- `phase-6-integration/03-rabbitmq-with-aspire.md` - Moving RabbitMQ to Aspire
- `phase-6-integration/04-billing-bounded-context.md` - Adding second bounded context
- `phase-6-integration/05-observability.md` - Logs, traces, metrics with Aspire Dashboard

---

## Phase 7: Frontend

*In progress - documentation created*

### Concepts Learned

- [x] Frontend overview and architecture (framework-agnostic)
- [x] Blazor Server with FluentUI components
- [x] Angular with Angular Material
- [x] Component architecture and routing
- [x] Consuming backend APIs with typed HttpClient / Angular HttpClient
- [x] State management patterns (Blazor scoped services, Angular signals)
- [x] Forms and validation (EditForm + FluentValidation / Reactive Forms)

### Implementation Progress

- [x] Documentation created (11 documents: 1 overview + 5 Blazor + 5 Angular)
- [ ] Blazor Server project setup with FluentUI and Aspire
- [ ] Angular project setup with Angular Material
- [ ] Patient management UI (list, create, detail, suspend)
- [ ] API integration end-to-end

### Key Decisions Made

1. **Two-track approach** - Blazor Server and Angular, same topics mirrored. Building both enables BFF pattern in Phase 8
2. **FluentUI for Blazor** - Microsoft's component library for Blazor Server
3. **Angular Material for Angular** - Google's component library for Angular
4. **Typed HttpClient** - Blazor uses typed HttpClient with Aspire service discovery
5. **Proxy config for Angular** - Angular uses proxy.conf.json for API calls during development
6. **Signals for Angular state** - Angular uses signals instead of BehaviorSubject for state management

### Docs Available

- `phase-7-frontend/00-frontend-overview.md` - What we build, API contract, track comparison
- `phase-7-frontend/blazor/01-blazor-project-setup.md` - Blazor Server + FluentUI + Aspire
- `phase-7-frontend/blazor/02-blazor-components-and-routing.md` - Components, routing, FluentDataGrid
- `phase-7-frontend/blazor/03-blazor-consuming-apis.md` - Typed HttpClient, service discovery
- `phase-7-frontend/blazor/04-blazor-state-management.md` - Component state, scoped services
- `phase-7-frontend/blazor/05-blazor-forms-and-validation.md` - EditForm, FluentValidation
- `phase-7-frontend/angular/01-angular-project-setup.md` - Angular CLI + Material
- `phase-7-frontend/angular/02-angular-components-and-routing.md` - Standalone components, Router
- `phase-7-frontend/angular/03-angular-consuming-apis.md` - HttpClient, RxJS, proxy config
- `phase-7-frontend/angular/04-angular-state-management.md` - Signals, MatSnackBar
- `phase-7-frontend/angular/05-angular-forms-and-validation.md` - Reactive Forms, validation

---

## Phase 8: API Gateway & BFF

*Not started*

### Planned Topics

- API Gateway with YARP (optional - single entry point for multiple APIs)
- BFF pattern (optional - frontend-specific backend)

### Docs Available

- `phase-8-api-gateway-bff/01-api-gateway.md` - YARP API Gateway (optional)
- `phase-8-api-gateway-bff/02-bff-pattern.md` - BFF pattern (optional)

---

## Phase 9: Authentication & Authorization

*Not started*

### Planned Topics

- Cookie-based authentication (all frontends — no tokens in the browser)
- Token mediating backend pattern (BFF handles OAuth flow server-side)
- ASP.NET Core Identity integration
- Authorization policies and requirements
- Role-based vs claims-based authorization
- Securing pages and components
- AuthenticationStateProvider
- User context in domain layer
- API Gateway authentication setup (referenced from Phase 8 gateway doc)
- BFF authentication setup (referenced from Phase 8 BFF doc)
- Backend API internal auth (managed identity, private VNet trust)
- DefaultAzureCredential for local development
- Multi-tenant considerations (optional)

### Docs Available

- `phase-9-auth/` - (to be created)
