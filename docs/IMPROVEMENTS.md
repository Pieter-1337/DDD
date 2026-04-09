# DDD Learning Track - Improvements & Gaps

**Last Reviewed**: 2026-03-12

**Purpose**: This document tracks potential improvements and gaps in the DDD learning project. These are not immediate tasks, but areas worth revisiting to deepen understanding or address architectural risks.

---

## High Priority

### 1. Transactional Outbox Pattern

**Status**: Completed (2026-03-27)

**Description**: Current event flow has a potential consistency gap. The system saves changes to SQL Server, dispatches domain events, then publishes integration events to RabbitMQ. If the process crashes between database commit and RabbitMQ publish, the database state is persisted but downstream systems never receive notification.

**Why It Matters**:
- Teaches atomic publish pattern (database + message broker transaction)
- Addresses real-world distributed system failure scenario
- MassTransit's Entity Framework outbox provides production-ready implementation
- Critical for reliable event-driven systems

**Related Phases**: Phase 5 (Event-Driven Architecture), Phase 6 (Integration)

**Related Documentation**:
- `docs/phase-5-event-driven/01-event-driven-overview.md` (domain vs integration events)
- `docs/phase-5-event-driven/08-transactional-outbox.md` (message reliability)

**Effort**: Medium (MassTransit has built-in support, requires configuration and migration)

**Implementation Notes**:
- Add MassTransit.EntityFrameworkCore package
- Configure outbox in both Scheduling and Billing bounded contexts
- Create migrations for outbox tables
- Update `UnitOfWork` to coordinate with outbox
- Test failure scenarios (process crash after commit, before publish)

---

### 2. Saga Implementation

**Status**: Documented only

**Description**: Saga pattern for distributed transactions is documented but not implemented. Even a simple saga (e.g., "appointment scheduling requires billing verification before confirmation") would demonstrate orchestration of long-running processes across bounded contexts.

**Why It Matters**:
- Core pattern for managing distributed transactions in microservices
- Teaches compensation logic (rollback across services)
- MassTransit state machines provide natural implementation
- Bridges theory and practice for event-driven workflows

**Related Phases**: Phase 5 (Event-Driven Architecture), Phase 6 (Integration)

**Related Documentation**: `docs/phase-5-event-driven/06-sagas-orchestration.md`

**Effort**: Large (requires state machine design, persistence, compensation handlers)

**Implementation Notes**:
- Design saga state machine (e.g., AppointmentBookingSaga)
- Implement state persistence (MassTransit saga repository)
- Add compensation commands (CancelAppointment, RefundBilling)
- Handle timeout scenarios
- Test happy path and failure scenarios
- Document saga lifetime and state transitions

**Example Scenarios**:
- Appointment booking requires payment authorization
- Patient registration requires billing profile creation
- Appointment cancellation triggers refund and schedule update

---

### 3. Eventual Consistency in the UI

**Status**: Not started

**Description**: When a patient is created in the Scheduling API, a `PatientCreatedIntegrationEvent` is published. The Billing API consumes this event and creates a billing profile asynchronously. If the frontend patient detail page fetches the billing profile immediately after patient creation, the event may not have been processed yet.

**Why It Matters**:
- Real-world challenge in event-driven systems
- Teaches UI patterns for handling asynchronous operations
- Multiple solution approaches (polling, optimistic UI, notifications)
- Critical user experience consideration

**Related Phases**: Phase 7 (Frontend), Phase 5 (Event-Driven Architecture), Phase 6 (Integration)

**Related Documentation**:
- `docs/phase-5-event-driven/01-event-driven-overview.md` (domain vs integration events)
- `docs/phase-7-frontend/` (frontend documentation)

**Effort**: Medium

**Possible Solutions**:
- **Polling with Backoff**: Frontend polls billing endpoint until profile exists
- **Optimistic UI**: Show "Billing profile creating..." state
- **SignalR Notifications**: Billing API pushes notification when profile created
- **Synchronous Fallback**: API waits for critical operations (anti-pattern for learning)
- **Event Store Query**: Frontend queries event log to confirm processing

**Implementation Notes**:
- Add loading states to Blazor components
- Implement retry with exponential backoff
- Consider SignalR hub for real-time notifications
- Add telemetry to measure event processing latency
- Document trade-offs for each approach

---

## Medium Priority

### 4. Value Objects

**Status**: Not started

**Description**: Currently using primitives pragmatically (e.g., `Email` as `string`). Implementing even one value object (e.g., `Email` with validation, equality by value, immutability) demonstrates core DDD tactical pattern.

**Why It Matters**:
- Teaches encapsulation of validation logic
- Demonstrates equality by value vs reference
- Enforces immutability at type level
- Small refactor with high conceptual value

**Related Phases**: Phase 1 (DDD Fundamentals)

**Related Documentation**:
- `docs/phase-1-ddd-fundamentals/02-value-objects.md`
- `BuildingBlocks.Domain` project

**Effort**: Small

**Candidate Value Objects**:
- `Email` (validation regex, case-insensitive equality)
- `PhoneNumber` (format validation, normalization)
- `Address` (composite value object with Street, City, State, ZipCode)
- `DateRange` (validation that start < end)

**Implementation Notes**:
- Extend `ValueObject` base class in BuildingBlocks.Domain
- Override `GetEqualityComponents()`
- Add validation in constructor
- Configure as owned entity in EF Core
- Update existing entities to use value object
- Add unit tests for equality and validation

---

### 5. Aggregate Design Depth

**Status**: Partially implemented

**Description**: Patient aggregate is well-developed. Appointment and Doctor aggregates are mentioned in Phase 1 documentation but not implemented. Multi-aggregate scenarios (e.g., "can this doctor accept an appointment at this time?") demonstrate aggregate boundary design challenges.

**Why It Matters**:
- Deepens understanding of aggregate boundary decisions
- Teaches coordination between aggregates via domain services
- Demonstrates consistency boundary trade-offs
- Real-world scheduling domain complexity

**Related Phases**: Phase 1 (DDD Fundamentals), Phase 2 (Persistence with EF Core)

**Related Documentation**: `docs/phase-1-ddd-fundamentals/03-aggregates-roots.md`

**Effort**: Large

**Design Considerations**:
- Should Appointment and Doctor be separate aggregates or part of scheduling aggregate?
- How to enforce "doctor availability" invariant across appointments?
- Domain service vs aggregate method for scheduling logic
- Eventual consistency vs transactional consistency for availability

**Implementation Notes**:
- Design Appointment aggregate (root, entities, value objects)
- Design Doctor aggregate (availability, schedule, specializations)
- Create domain service for scheduling coordination
- Handle double-booking prevention
- Add repository implementations
- Test invariant enforcement

---

### 6. Read Model Separation (CQRS)

**Status**: Partially implemented

**Description**: Current queries project from the same EF Core entities used for writes. True CQRS separates read models (flattened projections, denormalized views) from write models. This could be implemented with a read-only `DbContext`, `AsNoTracking()` queries, or even a separate read database.

**Why It Matters**:
- Demonstrates full CQRS pattern (not just command/query separation)
- Optimizes read performance independently from write model
- Teaches denormalization strategies
- Doesn't require separate database (can use same SQL Server)

**Related Phases**: Phase 3 (CQRS Pattern)

**Related Documentation**: `docs/phase-3-cqrs-pattern/` (entire section)

**Effort**: Medium

**Possible Approaches**:
- **Separate DbContext**: Read-only context with different entity configurations
- **Projections**: Use EF Core's `Select()` to flatten aggregates
- **Materialized Views**: SQL Server views for complex queries
- **Separate Database**: Read replicas or different store (advanced)

**Implementation Notes**:
- Create `SchedulingReadDbContext` with `AsNoTracking()` default
- Design flattened DTOs for common queries (e.g., PatientListDto)
- Update query handlers to use read context
- Consider event handlers to update read models
- Measure query performance improvements

---

## Low Priority / Future Considerations

### 7. Event Versioning in Practice

**Status**: Documented only

**Description**: Event versioning strategies are documented but not implemented. Worth implementing when adding a second version of an integration event contract (e.g., `PatientCreatedIntegrationEventV2` with additional fields).

**Why It Matters**:
- Critical for evolving distributed systems
- Teaches backward compatibility strategies
- Real-world microservices maintenance scenario

**Related Phases**: Phase 5 (Event-Driven Architecture)

**Related Documentation**: `docs/phase-5-event-driven/07-event-versioning.md`

**Effort**: Small (once a v2 event is needed)

**Implementation Notes**:
- Add new event version with additional fields
- Update consumers to handle both v1 and v2
- Use MassTransit message headers for version routing
- Test mixed-version scenarios
- Document migration strategy

---

### 8. Idempotent Message Handlers in Practice

**Status**: Documented only

**Description**: Idempotency is documented but not enforced. Adding an idempotency check (e.g., deduplication table, inbox pattern) to at least one consumer ensures at-least-once delivery doesn't cause duplicate side effects.

**Why It Matters**:
- Essential for reliable message processing
- Prevents duplicate operations (e.g., double-billing)
- Teaches defensive programming for distributed systems

**Related Phases**: Phase 5 (Event-Driven Architecture)

**Related Documentation**: `docs/phase-5-event-driven/05-idempotency-error-handling.md`

**Effort**: Small-Medium

**Implementation Approaches**:
- **Inbox Pattern**: Store processed message IDs in database table
- **Natural Idempotency**: Design operations to be naturally idempotent
- **Deduplication Window**: Track message IDs for recent time window

**Implementation Notes**:
- Add `ProcessedMessages` table with MessageId and ProcessedAt
- Check table in consumer before processing
- Use database transaction to ensure atomic check+process
- Add metrics for duplicate detection rate
- Test redelivery scenarios

---

### 9. Architecture Tests

**Status**: Not started

**Description**: Add architecture tests that enforce layer dependency rules at build/test time. For example, ensure Domain layer never references Infrastructure, Application never references WebApi, etc. Milan Jovanovic's Clean Architecture template includes these as a standard practice using `NetArchTest`.

**Why It Matters**:
- Prevents accidental layer violations during development
- Catches architectural drift early (in CI, not code review)
- Low effort, high value safety net
- Industry best practice for Clean Architecture projects

**Related Phases**: Phase 4 (Testing)

**Effort**: Small

**Implementation Notes**:
- Add `NetArchTest.Rules` NuGet package to a new `ArchitectureTests` project
- Write rules: Domain must not depend on Application, Infrastructure, or WebApi
- Write rules: Application must not depend on Infrastructure or WebApi
- Write rules: Infrastructure must not depend on WebApi
- Verify entity classes are sealed or follow expected patterns
- Run as part of CI pipeline

---

### 10. Result Pattern for Error Handling

**Status**: Not started

**Description**: Currently the solution uses `ValidationException` and `ExceptionToJsonFilter` for error handling (exception-driven control flow). The `Result<T>` pattern (as used in Milan Jovanovic's template) makes success/failure explicit in the return type, avoiding exceptions for expected business failures. Exceptions should be reserved for truly exceptional situations.

**Why It Matters**:
- Makes error paths explicit in method signatures
- Avoids using exceptions for control flow (performance + clarity)
- Command handlers return `Result<T>` instead of throwing
- More functional, composable error handling
- Industry trend in modern .NET architecture

**Related Phases**: Phase 3 (CQRS Pattern)

**Effort**: Medium-Large (touches all command handlers and pipeline behaviors)

**Implementation Notes**:
- Create `Result<T>` and `Error` types in BuildingBlocks.Domain
- Refactor command handlers to return `Result<TResponse>` instead of throwing `ValidationException`
- Update `ValidationBehavior` to return `Result.Failure` instead of throwing
- Update API controllers/filters to map `Result.Failure` to appropriate HTTP status codes
- Consider keeping exceptions for truly unexpected errors only
- Migrate incrementally (one handler at a time)

**Reference**: Milan Jovanovic's approach — domain errors as a dictionary of typed errors returned via `Result<T>`

---

### 11. Dapper for Read-Side Queries (True CQRS Split)

**Status**: Not started

**Description**: Current query handlers use EF Core with DTO projections (`IEntityDto<T>.Project`) for the read side. While this works well, a true CQRS implementation uses a different data access technology for reads. Dapper provides raw SQL performance without EF Core's change tracking overhead, making it ideal for complex read queries.

**Why It Matters**:
- Demonstrates the full CQRS pattern (different read/write stacks)
- Better performance for complex queries (no change tracker overhead)
- Teaches writing optimized SQL for read models
- Complements item #6 (Read Model Separation)

**Related Phases**: Phase 3 (CQRS Pattern)

**Related Improvements**: #6 (Read Model Separation)

**Effort**: Medium

**Implementation Notes**:
- Add `Dapper` NuGet package to Infrastructure projects
- Create `ISqlConnectionFactory` interface in Application layer
- Implement with `SqlConnection` in Infrastructure layer
- Refactor one or two query handlers to use Dapper (e.g., `GetAllPatientsQueryHandler`)
- Compare performance with EF Core DTO projection approach
- Keep EF Core for command handlers (write side)
- Consider: start with complex queries where Dapper benefits are most visible

---

## Low Priority / Future Considerations

### 7. Event Versioning in Practice

**Status**: Documented only

**Description**: Event versioning strategies are documented but not implemented. Worth implementing when adding a second version of an integration event contract (e.g., `PatientCreatedIntegrationEventV2` with additional fields).

**Why It Matters**:
- Critical for evolving distributed systems
- Teaches backward compatibility strategies
- Real-world microservices maintenance scenario

**Related Phases**: Phase 5 (Event-Driven Architecture)

**Related Documentation**: `docs/phase-5-event-driven/07-event-versioning.md`

**Effort**: Small (once a v2 event is needed)

**Implementation Notes**:
- Add new event version with additional fields
- Update consumers to handle both v1 and v2
- Use MassTransit message headers for version routing
- Test mixed-version scenarios
- Document migration strategy

---

### 8. Idempotent Message Handlers in Practice

**Status**: Documented only

**Description**: Idempotency is documented but not enforced. Adding an idempotency check (e.g., deduplication table, inbox pattern) to at least one consumer ensures at-least-once delivery doesn't cause duplicate side effects.

**Why It Matters**:
- Essential for reliable message processing
- Prevents duplicate operations (e.g., double-billing)
- Teaches defensive programming for distributed systems

**Related Phases**: Phase 5 (Event-Driven Architecture)

**Related Documentation**: `docs/phase-5-event-driven/05-idempotency-error-handling.md`

**Effort**: Small-Medium

**Implementation Approaches**:
- **Inbox Pattern**: Store processed message IDs in database table
- **Natural Idempotency**: Design operations to be naturally idempotent
- **Deduplication Window**: Track message IDs for recent time window

**Implementation Notes**:
- Add `ProcessedMessages` table with MessageId and ProcessedAt
- Check table in consumer before processing
- Use database transaction to ensure atomic check+process
- Add metrics for duplicate detection rate
- Test redelivery scenarios

---

### 12. MediatR Replacement Strategy

**Status**: Monitoring

**Description**: MediatR is transitioning to a commercial model. Milan Jovanovic has already removed MediatR from his Clean Architecture template. While MediatR still works under its current license, it's worth having a migration plan. Alternatives include building a lightweight in-house mediator, using Wolverine's built-in mediator capabilities, or adopting another open-source library.

**Why It Matters**:
- MediatR commercial licensing may affect future updates
- Reduces dependency on a single library for a core architectural pattern
- Wolverine already provides mediator-like capabilities (used in Billing BC)
- Good exercise in understanding what MediatR actually does under the hood

**Related Phases**: Phase 3 (CQRS Pattern), Phase 5 (Event-Driven Architecture)

**Effort**: Large (MediatR is deeply integrated — pipeline behaviors, notification handlers, DI registration)

**Possible Alternatives**:
- **Wolverine**: Already in the project, has built-in mediator pattern
- **Custom lightweight mediator**: Simple `IMediator` implementation (~100 lines)
- **Microsoft.Extensions.DependencyInjection**: Direct handler resolution without mediator
- **Other OSS libraries**: Mediator (source-generated, high performance)

**Implementation Notes**:
- Monitor MediatR licensing changes
- Evaluate Wolverine's mediator capabilities as a natural replacement
- If migrating: abstract behind own `IMediator` interface first
- Consider source-generated alternatives for zero-reflection performance
- No urgency — current MediatR version works fine

---

## How to Use This Document

1. **Review Regularly**: Revisit after completing major phases or milestones
2. **Prioritize Based on Context**: Priorities may shift based on learning goals
3. **Update Status**: Mark items as "In Progress" or "Completed" with date
4. **Link to Implementation**: Add references to PRs or commits when implemented
5. **Add New Items**: Append new gaps as discovered during development

---

## Version History

| Date       | Changes                                                      |
|------------|--------------------------------------------------------------|
| 2026-03-12 | Initial document creation with 8 improvement areas identified |
| 2026-03-27 | Completed #1 Transactional Outbox Pattern (MassTransit EF Core outbox) |
| 2026-04-09 | Added #9-#12: Architecture Tests, Result Pattern, Dapper for reads, MediatR replacement strategy (from Milan Jovanovic comparison) |
