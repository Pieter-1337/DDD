---
name: 'ddd-backend-unit-test'
description: >
  Add unit tests for a validator, a domain entity, or a domain-event handler in a
  bounded context's test project (Core/<Context>/<Context>.Tests). Validators use
  the <Context>ValidatorTestBase (derives ValidatorTestBase: DI container + mocked
  IUnitOfWork/IRepository + mocked ICurrentUser). Domain entities and event
  handlers are tested with no base class. MSTest, Shouldly, Moq, NBuilder. No HTTP,
  no real DB.
paths: Core/**, BuildingBlocks/**
---

# DDD Backend Unit Test

Isolated tests of a single class. Three shapes:
1. **Validator** — via `<Context>ValidatorTestBase` (mocked repos, no DB).
2. **Domain entity** — no base class; exercise `Create` + named mutators + raised events.
3. **Domain-event handler** — no base class; manual Moq mocks + `Verify`.

For handler-through-MediatR tests against a real (SQLite) DB, use `ddd-backend-integration-test` instead.

## Conventions
- Test project: `Core/<Context>/<Context>.Tests` (Scheduling's is `Scheduling.Domain.Tests`, assembly `Scheduling.Tests`).
- Layout mirrors the source:
  - `ApplicationTests/ValidatorTests/<Operation><Aggregate>CommandValidatorTests.cs`
  - `ApplicationTests/EventHandlerTests/<Event>HandlerTests.cs`
  - `DomainTests/<Aggregates>/<Aggregate>Tests.cs`
- MSTest attributes: `[TestClass]`, `[TestMethod]`, `[TestInitialize]`, `[TestCleanup]`.
- Method naming: `Scenario_Expectation` / `Invalid_When_X` / `Valid_When_X` / `Handle_Should_X`. Arrange-Act-Assert comments.
- Assertions: **Shouldly** (`result.ShouldBe(...)`, `x.ShouldBeTrue()`, `.ShouldThrowAsync<T>()`).
- Mocks: **Moq** (`Mock<IRepository<T>>`, `Mock<IUnitOfWork>`).
- Test data: entities via the per-aggregate `Builder` (calls `Create(...)`); request/response DTOs via NBuilder `Builder<T>.CreateNew().With(...).Build()` (NBuilder can't set private setters on entities).
- Validator assertions use `result.Errors.ShouldContainValidation(propertyName, errorCode)` from `ValidatorTestBase` — `errorCode` is either a built-in rule constant (`VALIDATION_NOT_NULL_VALIDATOR`, `VALIDATION_EMAIL_VALIDATOR`, …) or a custom `ErrorCode.X.Value`.
- Run with `dotnet test DDD.sln` (or the specific test project).

## Step 1 — Clarify scope
- Which class — a validator, an entity, or an event handler?
- For a validator: the meaningful scenarios (happy path, each field rule, each business/role rule).
- For an entity: which factory/mutator and what state + events it should produce.

## Step 2 — Per-context validator base (one-time per context)

Each context has a `<Context>ValidatorTestBase : ValidatorTestBase` that registers the application assembly and wires repository mocks onto `UnitOfWorkMock`. If it doesn't exist yet, create `Core/<Context>/<Context>.Tests/<Context>ValidatorTestBase.cs`:

```csharp
using System.Linq.Expressions;
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Tests;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using <Context>.Application;
using <Context>.Domain.<Aggregates>;

namespace <Context>.Tests;

public abstract class <Context>ValidatorTestBase : ValidatorTestBase
{
    protected Mock<IRepository<<Aggregate>>> <Aggregate>RepositoryMock { get; private set; } = null!;

    protected override void RegisterServices(IServiceCollection services)
    {
        services.Add<Context>Application(); // registers MediatR + validators (incl. internal)

        <Aggregate>RepositoryMock = new Mock<IRepository<<Aggregate>>>();
        UnitOfWorkMock.Setup(u => u.RepositoryFor<<Aggregate>>())
            .Returns(<Aggregate>RepositoryMock.Object);
    }

    protected void Setup<Aggregate>Exists(bool exists = true) =>
        <Aggregate>RepositoryMock
            .Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<<Aggregate>, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists);
}
```

`ValidatorTestBase` already provides `UnitOfWorkMock`, `CurrentUserMock`, `SetupUserRoles(...)`, `ValidatorFor<T>()`, and `ShouldContainValidation`.

## Step 3 — Validator test

`ApplicationTests/ValidatorTests/Create<Aggregate>CommandValidatorTests.cs`:

```csharp
using BuildingBlocks.Enumerations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using <Context>.Application.<Aggregates>.Commands;

namespace <Context>.Tests.ApplicationTests.ValidatorTests;

[TestClass]
public class Create<Aggregate>CommandValidatorTests : <Context>ValidatorTestBase
{
    [TestMethod]
    public async Task Invalid_When_RequestIsNull()
    {
        SetupUserRoles(Auth.AppRoles.Admin); // pass the role gate so we isolate the field rule

        var result = await ValidatorFor<Create<Aggregate>Command>()
            .ValidateAsync(new Create<Aggregate>Command(null!));

        result.Errors.ShouldContainValidation(
            nameof(Create<Aggregate>Command.<Aggregate>), VALIDATION_NOT_NULL_VALIDATOR);
    }

    [TestMethod]
    public async Task Invalid_When_CallerHasNoAllowedRole()
    {
        // default ICurrentUser has no roles -> role gate fails with ERR_FORBIDDEN
        var result = await ValidatorFor<Create<Aggregate>Command>()
            .ValidateAsync(new Create<Aggregate>Command(new Create<Aggregate>Request { Name = "X", Status = "Active" }));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorCode == ErrorCode.Forbidden.Value);
    }

    [TestMethod]
    public async Task Valid_When_AllFieldsAndRoleAreValid()
    {
        SetupUserRoles(Auth.AppRoles.Admin);

        var result = await ValidatorFor<Create<Aggregate>Command>()
            .ValidateAsync(new Create<Aggregate>Command(new Create<Aggregate>Request { Name = "X", Status = "Active" }));

        result.IsValid.ShouldBeTrue();
    }
}
```

Notes:
- Role-gated (`UserValidator<T>`) commands: call `SetupUserRoles(...)` in Arrange, or assert `ErrorCode.Forbidden.Value` to test the gate itself.
- Business-rule checks that hit the repo: drive them with the repository mock (`Setup<Aggregate>Exists(true)` → assert `ErrorCode.NotFound`/`Conflict`).
- Built-in rule names live as `VALIDATION_*` constants on the base (`VALIDATION_EMAIL_VALIDATOR`, `VALIDATION_NOT_EMPTY_VALIDATOR`, `VALIDATION_PREDICATE_VALIDATOR`, …); custom codes use `ErrorCode.X.Value`.

## Step 4 — Domain entity test

`DomainTests/<Aggregates>/<Aggregate>Tests.cs` — no base class, pure logic:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using <Context>.Domain.<Aggregates>;
using <Context>.Domain.<Aggregates>.Events;

namespace <Context>.Tests.DomainTests.<Aggregates>;

[TestClass]
public class <Aggregate>Tests
{
    [TestMethod]
    public void Create_SetsState_AndRaisesCreatedEvent()
    {
        var entity = <Aggregate>.Create("Acme");

        entity.Id.ShouldNotBe(Guid.Empty);
        entity.Name.ShouldBe("Acme");
        entity.Status.ShouldBe(<Aggregate>Status.Active);
        entity.DomainEvents.Count.ShouldBe(1);
        entity.DomainEvents[0].ShouldBeOfType<<Aggregate>CreatedEvent>();
    }

    [TestMethod]
    public void Suspend_IsIdempotent()
    {
        var entity = <Aggregate>.Create("Acme");
        entity.Suspend();
        entity.Suspend(); // no-op the second time

        entity.Status.ShouldBe(<Aggregate>Status.Suspended);
        entity.DomainEvents.OfType<<Aggregate>SuspendedEvent>().Count().ShouldBe(1);
    }
}
```

## Step 5 — Domain-event handler test

`ApplicationTests/EventHandlerTests/<Aggregate>CreatedEventHandlerTests.cs` — manual mocks, verify the integration event is queued:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using BuildingBlocks.Application.Interfaces;
using <Context>.Application.<Aggregates>.EventHandlers;
using <Context>.Domain.<Aggregates>.Events;

namespace <Context>.Tests.ApplicationTests.EventHandlerTests;

[TestClass]
public class <Aggregate>CreatedEventHandlerTests
{
    [TestMethod]
    public async Task Handle_QueuesIntegrationEvent()
    {
        var uow = new Mock<IUnitOfWork>();
        var handler = new <Aggregate>CreatedEventHandler(NullLogger<<Aggregate>CreatedEventHandler>.Instance, uow.Object);

        var id = Guid.NewGuid();
        await handler.Handle(new <Aggregate>CreatedEvent(id, "Acme"), CancellationToken.None);

        uow.Verify(u => u.QueueIntegrationEvent(
            It.Is<<Aggregate>CreatedIntegrationEvent>(e => e.<Aggregate>Id == id)), Times.Once);
    }
}
```

(Match the real handler's constructor — `PatientCreatedEventHandler` takes `(ILogger<...>, IUnitOfWork)`.)

## Step 6 — Run

```
dotnet test DDD.sln
```

Or just the context: `dotnet test Core\<Context>\<Context>.Tests`. Fix Shouldly/Moq compile errors, then `dotnet build DDD.sln` to confirm no regressions.
