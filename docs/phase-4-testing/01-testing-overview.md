# Testing Overview

## Why Testing Matters in DDD

Testing is critical in Domain-Driven Design because:

1. **Domain logic is the core** - Your business rules need to be verified
2. **Aggregates enforce invariants** - Tests ensure invariants are maintained
3. **Refactoring confidence** - Tests let you evolve the domain safely
4. **Documentation** - Tests document expected behavior

---

## Test Types

This project uses three types of tests:

| Test Type | What It Tests | Speed | Database |
|-----------|--------------|-------|----------|
| **Domain Tests** | Entity behavior, state transitions | Fastest | None |
| **Validator Tests** | Input validation rules | Fast | Mocked |
| **Handler Tests** | Full command/query pipeline | Medium | SQLite |

---

## Two-Tier Test Base Hierarchy

```
                    ┌─────────────────────┐
                    │  ValidatorTestBase  │  ← Mocked IUnitOfWork
                    │  (Unit Tests)       │
                    └──────────┬──────────┘
                               │
                               │ inherits
                               ▼
                    ┌─────────────────────┐
                    │  TestBase<TContext> │  ← Real SQLite database
                    │  (Integration Tests)│
                    └─────────────────────┘
```

Each bounded context extends these:

```
ValidatorTestBase
       │
       └── SchedulingValidatorTestBase  ← For Scheduling validator tests

TestBase<TContext>
       │
       └── SchedulingDbTestBase         ← For Scheduling handler tests
```

---

## Project Structure

```
BuildingBlocks.Tests/
├── ValidatorTestBase.cs          <- Unit tests with mocked IUnitOfWork
└── TestBase.cs                   <- Integration tests (inherits ValidatorTestBase)

Core/Scheduling/Scheduling.Domain.Tests/
├── SchedulingValidatorTestBase.cs   <- Validator unit tests
├── SchedulingDbTestBase.cs          <- Handler integration tests
├── ApplicationTests/
│   ├── HandlerTests/
│   │   ├── CreatePatientCommandHandlerTests.cs
│   │   ├── GetAllPatientsQueryHandlerTests.cs
│   │   └── GetPatientQueryHandlerTests.cs
│   └── ValidatorTests/
│       ├── CreatePatientCommandValidatorTests.cs
│       ├── GetPatientQueryValidatorTests.cs
│       ├── GetAllPatientsQueryValidatorTests.cs
│       ├── SuspendPatientCommandValidatorTests.cs
│       └── ActivatePatientCommandValidatorTests.cs
└── DomainTests/
    └── Patients/
        └── PatientTests.cs
```

---

## When to Use Which Base Class

| Test Type | Base Class | Database | Speed | Use For |
|-----------|------------|----------|-------|---------|
| Validator unit tests | `SchedulingValidatorTestBase` | Mocked | Fast | Testing validation rules |
| Handler integration tests | `SchedulingDbTestBase` | SQLite | Medium | Testing full command/query pipeline |
| Domain entity tests | None | None | Fastest | Testing entity behavior |

### Decision Flow

```
What are you testing?
│
├── Domain entity behavior (Create, Suspend, Activate, etc.)
│   └── No base class needed - pure unit test
│
├── Validator rules (required fields, format, existence)
│   └── Use SchedulingValidatorTestBase (mocked DB)
│
└── Full command/query handler
    └── Use SchedulingDbTestBase (real SQLite DB)
```

---

## Test Naming Convention

```csharp
[TestMethod]
public async Task MethodName_Should_ExpectedBehavior_When_Condition()
{
    // ...
}
```

Examples:
- `Create_ShouldCreatePatientWithCorrectValues()`
- `Suspend_ShouldChangeStatusToSuspended()`
- `Activate_ShouldChangeStatusToActive()`
- `Invalid_When_PatientIsNull()`
- `Handle_Should_CreatePatient_ForValidRequest()`

---

## Verification Checklist

- [x] `BuildingBlocks.Tests` project created
- [x] `ValidatorTestBase` with mocked `IUnitOfWork` and validation constants
- [x] `TestBase<TContext>` inheriting from `ValidatorTestBase`
- [x] `ShouldContainValidation()` extension method
- [x] `SchedulingValidatorTestBase` for validator unit tests
- [x] `SchedulingDbTestBase` for integration tests
- [x] Transaction-based test isolation
- [x] SQLite in-memory database for integration tests
- [x] Validator tests for all commands/queries
- [x] Handler tests for commands/queries
- [x] Domain tests for entity behavior

---

## Docs in This Phase

1. **01-testing-overview.md** - This file
2. **02-test-infrastructure.md** - BuildingBlocks.Tests setup
3. **03-validator-tests.md** - Writing validator unit tests
4. **04-handler-tests.md** - Writing handler integration tests
5. **05-domain-tests.md** - Writing domain entity tests

→ Next: [02-test-infrastructure.md](./02-test-infrastructure.md)
