---
name: 'ddd-backend-integration-test'
description: >
  Add handler integration tests for a bounded context in Core/<Context>/<Context>.Tests.
  Tests derive from <Context>DbTestBase (which derives TestBase<TContext>): a real
  SQLite in-memory database, the full MediatR pipeline, and per-test isolation via a
  transaction that is rolled back in cleanup. Dispatch through GetMediator().Send,
  seed and assert via Uow.RepositoryFor<T>(). MSTest, Shouldly, NBuilder.
paths: Core/**, BuildingBlocks/**
---

# DDD Backend Integration Test

Exercises a command/query end-to-end through MediatR (validation behavior + handler) against a **real SQLite in-memory** database. Each test runs inside a transaction opened in `TestInitialize` and rolled back in `TestCleanup`, so tests are isolated without recreating the schema.

`TestBase<TContext>` (`BuildingBlocks.Tests`) provides:
- `GetMediator()` — the scoped `IMediator` (runs the full pipeline: Transaction → Logging → Validation → Performance → handler).
- `Uow` — the scoped `IUnitOfWork` for seeding/asserting.
- `DbContext` — the scoped `TContext` if you need raw access.
- `StartStopwatch()` / `StopStopwatch()` / `ElapsedSeconds()` for perf assertions.
- It also disables NBuilder auto-naming for `Entity.Id`.

> SQLite-in-memory keeps the connection open for the test's lifetime and calls `EnsureCreated()` (not `Migrate()`), so the model is materialized from the configurations — migrations and `HasData` seeds are NOT applied here. Seed anything a test needs via `Uow`.

## Conventions
- Test project: `Core/<Context>/<Context>.Tests`; one class per command/query, named `<Operation><Aggregate>CommandHandlerTests` / `...QueryHandlerTests`.
- MSTest (`[TestClass]`, `[TestMethod]`); Shouldly assertions.
- Derive from `<Context>DbTestBase` (below). Never new-up a DbContext directly.
- Validation failures surface as a thrown `FluentValidation.ValidationException` from the pipeline (there's no HTTP layer here) — assert with `Should.ThrowAsync<ValidationException>(...)`.
- Seed with `Uow.RepositoryFor<T>().Add(Entity.Create(...)); await Uow.SaveChangesAsync();` inside the test (or a private helper). Use the per-aggregate `Builder` for entities.
- Run with `dotnet test DDD.sln`.

## Step 1 — Per-context DB test base (one-time per context)

If it doesn't exist, create `Core/<Context>/<Context>.Tests/<Context>DbTestBase.cs`:

```csharp
using BuildingBlocks.Tests;
using Microsoft.Extensions.DependencyInjection;
using <Context>.Application;
using <Context>.Infrastructure.Persistence;

namespace <Context>.Tests;

public class <Context>DbTestBase : TestBase<<Context>DbContext>
{
    protected override void RegisterBoundedContextServices(IServiceCollection services)
    {
        services.Add<Context>Application(); // MediatR handlers + validators
    }
}
```

`TestBase<TContext>` already registers `AddDbContext<TContext>(UseSqlite(...))` and `AddScoped<IUnitOfWork, EfCoreUnitOfWork<TContext>>()`, so the context just adds its application services. (Billing's live equivalent is `BillingDbTestBase`.)

## Step 2 — Handler test

`ApplicationTests/HandlerTests/Create<Aggregate>CommandHandlerTests.cs`:

```csharp
using FluentValidation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using <Context>.Application.<Aggregates>.Commands;
using <Context>.Domain.<Aggregates>;

namespace <Context>.Tests.ApplicationTests.HandlerTests;

[TestClass]
public class Create<Aggregate>CommandHandlerTests : <Context>DbTestBase
{
    [TestMethod]
    public async Task Handle_CreatesEntity_ForValidRequest()
    {
        // Arrange — give the caller an allowed role so the command validator passes
        SetupUserRoles(Auth.AppRoles.Admin);
        var command = new Create<Aggregate>Command(new Create<Aggregate>Request
        {
            Name = "Acme",
            Status = "Active",
        });

        // Act
        StartStopwatch();
        var response = await GetMediator().Send(command);
        StopStopwatch();

        // Assert response
        response.ShouldNotBeNull();
        response.Success.ShouldBeTrue();
        response.<Aggregate>Id.ShouldNotBe(Guid.Empty);

        // Assert persisted
        var entity = await Uow.RepositoryFor<<Aggregate>>().GetByIdAsync(response.<Aggregate>Id);
        entity.ShouldNotBeNull();
        entity!.Name.ShouldBe("Acme");

        ElapsedSeconds().ShouldBeLessThan(1M);
    }

    [TestMethod]
    public async Task Handle_Throws_WhenValidationFails()
    {
        SetupUserRoles(Auth.AppRoles.Admin);
        var command = new Create<Aggregate>Command(new Create<Aggregate>Request { Name = "", Status = "Active" });

        await Should.ThrowAsync<ValidationException>(() => GetMediator().Send(command));
    }
}
```

> `SetupUserRoles(...)` comes from `ValidatorTestBase` and configures the mocked `ICurrentUser`. The DB test base inherits it. Commands whose validator inherits `UserValidator<T>` will throw `ValidationException` (`ERR_FORBIDDEN`) unless the caller is given an allowed role.

## Step 3 — Seeding existing rows

For state-change/query handlers, seed first:

```csharp
private async Task<Guid> Seed<Aggregate>Async()
{
    var entity = <Aggregate>.Create("Seeded");
    Uow.RepositoryFor<<Aggregate>>().Add(entity);
    await Uow.SaveChangesAsync();
    return entity.Id;
}

[TestMethod]
public async Task Suspend_SetsStatus()
{
    SetupUserRoles(Auth.AppRoles.Doctor);
    var id = await Seed<Aggregate>Async();

    await GetMediator().Send(new Suspend<Aggregate>Command { Id = id });

    var entity = await Uow.RepositoryFor<<Aggregate>>().GetByIdAsync(id);
    entity!.Status.ShouldBe(<Aggregate>Status.Suspended);
}
```

Everything happens inside the one test transaction, which `TestCleanup` rolls back — no cross-test bleed, no manual purge needed.

## Step 4 — Query handlers

Seed rows, send the query, assert the projected DTO(s):

```csharp
var id = await Seed<Aggregate>Async();
var dto = await GetMediator().Send(new Get<Aggregate>Query { Id = id });
dto.ShouldNotBeNull();
dto!.Id.ShouldBe(id);
```

Query validators that check existence run in the pipeline, so a missing id throws `ValidationException` (assert it the same way as Step 2).

## Step 5 — Run

```
dotnet test Core\<Context>\<Context>.Tests
```

## When you need real migrations / provider-specific SQL

These tests use SQLite + `EnsureCreated()`, so they don't cover migrations, `HasData` seeds, or SQL-Server-specific behavior. Those are out of scope here — verify them by running the WebApi against LocalDB (see `ddd-ef-migration` / `ddd-ef-seed`).
