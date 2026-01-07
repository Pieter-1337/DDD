# Learning Progress Tracker

## Overall Status

| Phase | Status | Started | Completed |
|-------|--------|---------|-----------|
| Phase 1: DDD Fundamentals | Complete | 2026-01-05 | 2026-01-05 |
| Phase 2: EF Core Persistence | In Progress | 2026-01-05 | - |
| Phase 3: CQRS Pattern | Not Started | - | - |
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
- [x] IPatientRepository interface

### Key Decisions Made

1. **Using Guid directly** - No PatientId wrapper, pragmatic choice
2. **Primitives for simple values** - Email as string, not value object
3. **Complex values get IDs** - If it needs its own table, make it an entity
4. **Generic repository base** - Will implement IRepository<T> and IUnitOfWork

---

## Phase 2: EF Core Persistence

### Concepts to Learn

- [ ] EF Core DbContext setup
- [ ] Fluent API entity configuration
- [ ] Repository implementation with EF Core
- [ ] Unit of Work pattern
- [ ] Database migrations
- [ ] Domain event dispatching

### Implementation Progress

- [ ] Add EF Core packages to Infrastructure
- [ ] Create SchedulingDbContext
- [ ] Create PatientConfiguration (Fluent API)
- [ ] Implement PatientRepository
- [ ] Implement UnitOfWork
- [ ] Create database migrations
- [ ] Add domain event dispatcher
- [ ] Test with API endpoint

### Docs Available

- `phase-2-ef-core/01-setup-efcore.md` - DbContext and configuration
- `phase-2-ef-core/02-repository-implementation.md` - Repository pattern
- `phase-2-ef-core/03-database-migrations.md` - Creating the database
- `phase-2-ef-core/04-domain-event-dispatching.md` - Publishing events

---

## Phase 3: CQRS Pattern

*Not started*

---

## Phase 4: Event-Driven Architecture

*Not started*

---

## Phase 5: Integration

*Not started*
