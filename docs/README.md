# DDD Learning Documentation

This directory contains learning notes, explanations, and progress tracking for the DDD & Event-Driven Architecture learning project.

## Structure

```
docs/
+-- README.md                    # This file
+-- PROGRESS.md                  # Overall progress tracker
+-- phase-1-ddd-fundamentals/    # DDD tactical patterns
+-- phase-2-ef-core/             # Persistence with EF Core
+-- phase-3-cqrs/                # CQRS pattern
+-- phase-4-testing/             # Integration testing setup
+-- phase-5-event-driven/        # Event-driven architecture (planned)
+-- phase-6-integration/         # Complete system integration (planned)
```

## How to Use

Each phase directory contains:
- Concept explanations (the "why")
- Code examples with annotations
- Decisions made and reasoning
- Questions and answers from learning sessions

## Current Phase

**Phase 4: Integration Testing** - In Progress

- Generic `TestBase<TContext>` in BuildingBlocks.Tests
- Bounded context test bases (e.g., `SchedulingTestBase`)
- Transaction-based test isolation
- MSTest with Shouldly and NBuilder

See [PROGRESS.md](./PROGRESS.md) for detailed status.
