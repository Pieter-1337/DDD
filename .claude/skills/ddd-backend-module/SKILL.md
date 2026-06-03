---
name: 'ddd-backend-module'
description: >
  Scaffold a new aggregate inside an existing bounded context (Core/<Context>/):
  a DDD entity in <Context>.Domain (Entity base, private setters, static Create
  factory, named mutators, domain events), a SmartEnum status if needed, an
  IEntityDto in <Context>.Application, an IEntityTypeConfiguration in
  <Context>.Infrastructure (auto-discovered), a controller shell in the matching
  WebApi, and a test builder. No DbSet plumbing — all access via IUnitOfWork /
  IRepository. Use once per new domain concept before adding slices. Includes a
  heavier variant for standing up a whole new bounded context.
paths: Core/**, WebApplications/**, BuildingBlocks/**
---

# DDD Backend Module (new aggregate)

One-time scaffold for a new aggregate (entity + persistence + DTO + endpoints shell) inside an **existing** bounded context. Run this first, then use `ddd-backend-slice` to add operations. `Patient` (in `Core/Scheduling`) is the canonical live example to mirror; `BillingProfile` (in `Core/Billing`) is the simpler one.

## Architecture recap

Each bounded context is four projects under `Core/<Context>/`:

| Project | Holds | References |
|---|---|---|
| `<Context>.Domain` | Aggregates, domain events, SmartEnums | only `BuildingBlocks.Domain` |
| `<Context>.Application` | Commands/Queries/Handlers/Validators/DTOs/EventHandlers | Domain, `BuildingBlocks.Application`, `Auth`, `IntegrationEvents` |
| `<Context>.Infrastructure` | DbContext, `IEntityTypeConfiguration<T>`, consumers, migrations | Application, `BuildingBlocks.Infrastructure.*` |
| `<Context>.Tests` (or `.Domain.Tests`) | Domain/handler/validator/event-handler tests | the three above + `BuildingBlocks.Tests` |

Persistence is generic: there are **no `DbSet<T>` properties**. The DbContext calls `modelBuilder.ApplyConfigurationsFromAssembly(...)`, and everything is reached through `IUnitOfWork.RepositoryFor<T>()` → `context.Set<T>()`. So adding an aggregate is: write the entity, write its configuration, write its DTO, generate a migration. No DbContext edit, no DI edit (handlers/validators are assembly-scanned by `AddBoundedContext`).

## Conventions
- Entity inherits `Entity` (`BuildingBlocks.Domain`) which supplies `Guid Id`, `AddDomainEvent`, `DomainEvents`, `ClearDomainEvents`.
- Writable state has **private setters**; construction via `static Create(...)`; mutation via named methods (`Suspend`, `Activate`, `UpdateContactInfo`, …). Each meaningful transition raises a domain event.
- A private parameterless ctor exists for EF (`private Patient() { }`).
- Status / closed sets are SmartEnums inheriting `SmartEnumBase<T>` (`BuildingBlocks.Enumerations`) — gives `IsInEnum`, `FromName`, `TryFromName`.
- DTOs inherit `DtoBase` (provides `Id`) and implement `IEntityDto<TEntity, TDto>` (static `Project` expression for EF projection + static `ToDto` for in-memory mapping).
- EF config implements `IEntityTypeConfiguration<T>` in `<Context>.Infrastructure/Persistence/Configurations/` — auto-picked-up. Always `builder.Ignore(x => x.DomainEvents);`.
- Cross-context references are by `Guid` FK only — no EF navigation across contexts (contexts have separate DbContexts/databases). Integrity is checked in a slice validator via the other context, or via integration events.
- Build with `dotnet build DDD.sln`.

## Step 1 — Clarify scope
- Which **existing** bounded context does this aggregate belong to (`Scheduling`, `Billing`, …)? (If it's a genuinely new context, jump to the "New bounded context" section below.)
- Aggregate name (PascalCase). What fields does it have, and which are mutable after creation?
- Is there a closed status/lifecycle set → SmartEnum?
- What domain events fire on create and on each state change?

## Step 2 — Entity

`Core/<Context>/<Context>.Domain/<Aggregates>/<Aggregate>.cs`:

```csharp
using BuildingBlocks.Domain;
using <Context>.Domain.<Aggregates>.Events;

namespace <Context>.Domain.<Aggregates>;

public class <Aggregate> : Entity
{
    public string Name { get; private set; }
    public <Aggregate>Status Status { get; private set; }
    // additional properties with private setters

    private <Aggregate>() { } // EF

    public static <Aggregate> Create(string name, <Aggregate>Status? status = null)
    {
        var entity = new <Aggregate>
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Status = status ?? <Aggregate>Status.Active,
        };

        entity.AddDomainEvent(new <Aggregate>CreatedEvent(entity.Id, entity.Name));
        return entity;
    }

    public void Rename(string name) => Name = name.Trim();

    public void Suspend()
    {
        if (Status == <Aggregate>Status.Suspended) return; // idempotent
        Status = <Aggregate>Status.Suspended;
        AddDomainEvent(new <Aggregate>SuspendedEvent(Id));
    }
}
```

**Do not** expose public setters — handlers mutate via named methods so invariants live on the entity. Make mutators idempotent (guard the no-op transition) so re-raising events is avoided.

## Step 3 — SmartEnum (if there's a closed status set)

`Core/<Context>/<Context>.Domain/<Aggregates>/<Aggregate>Status.cs`:

```csharp
using BuildingBlocks.Enumerations;

namespace <Context>.Domain.<Aggregates>;

public sealed class <Aggregate>Status : SmartEnumBase<<Aggregate>Status>
{
    public static readonly <Aggregate>Status Active = new(nameof(Active), 1);
    public static readonly <Aggregate>Status Suspended = new(nameof(Suspended), 2);
    public static readonly <Aggregate>Status Deleted = new(nameof(Deleted), 3);

    private <Aggregate>Status(string name, int value) : base(name, value) { }
}
```

## Step 4 — Domain events

`Core/<Context>/<Context>.Domain/<Aggregates>/Events/<Aggregate>CreatedEvent.cs`:

```csharp
using BuildingBlocks.Domain.Events;

namespace <Context>.Domain.<Aggregates>.Events;

public record <Aggregate>CreatedEvent(Guid <Aggregate>Id, string Name) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
```

> Domain events are dispatched by `EfCoreUnitOfWork.SaveChangesAsync()` via MediatR **before** `SaveChanges` — there is no SaveChanges interceptor. An `INotificationHandler<<Aggregate>CreatedEvent>` in `<Context>.Application/.../EventHandlers/` can react (e.g. queue an integration event via `uow.QueueIntegrationEvent(...)`). Add those handlers with `ddd-backend-slice`.

## Step 5 — DTO

`Core/<Context>/<Context>.Application/<Aggregates>/Dtos/<Aggregate>Dto.cs`:

```csharp
using BuildingBlocks.Application.Dtos;
using BuildingBlocks.Application.Interfaces;
using <Context>.Domain.<Aggregates>;
using System.Linq.Expressions;

namespace <Context>.Application.<Aggregates>.Dtos;

public class <Aggregate>Dto : DtoBase, IEntityDto<<Aggregate>, <Aggregate>Dto>
{
    public string Name { get; set; }
    public <Aggregate>Status Status { get; set; }

    public static <Aggregate>Dto ToDto(<Aggregate> e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Status = e.Status,
    };

    public static Expression<Func<<Aggregate>, <Aggregate>Dto>> Project => e => new <Aggregate>Dto
    {
        Id = e.Id,
        Name = e.Name,
        Status = e.Status,
    };
}
```

`Project` is used by `repo.GetAllAsDtosAsync<TDto>` / `FirstOrDefaultAsDtoAsync<TDto>` (EF translates it to SQL — keep it projection-safe, no method calls EF can't translate). `ToDto` is for mapping an already-loaded entity in a handler.

## Step 6 — Error codes

Reuse the shared `ErrorCode` (`BuildingBlocks.Enumerations`) for generic cases (`NotFound`, `Conflict`, `InvalidEmail`, `Required`, …). For aggregate-specific business rules, add a smart-enum error class inheriting `ErrorCodeBase<TEnum>` (auto-prefixes `ERR_`):

```csharp
using BuildingBlocks.Enumerations;

namespace <Context>.Domain.<Aggregates>;

public sealed class <Aggregate>ErrorCode : ErrorCodeBase<<Aggregate>ErrorCode>
{
    public static readonly <Aggregate>ErrorCode AlreadySuspended =
        new("<AGG>_ALREADY_SUSPENDED", "<Aggregate> is already suspended"); // code => ERR_<AGG>_ALREADY_SUSPENDED

    private <Aggregate>ErrorCode(string code, string message) : base(code, message) { }
}
```

Use `.Value` (the `ERR_`-prefixed code) with `WithErrorCode` and `.Message` with `WithMessage` in validators (see `ddd-backend-slice`).

## Step 7 — EF configuration

`Core/<Context>/<Context>.Infrastructure/Persistence/Configurations/<Aggregate>Configuration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using <Context>.Domain.<Aggregates>;

namespace <Context>.Infrastructure.Persistence.Configurations;

public class <Aggregate>Configuration : IEntityTypeConfiguration<<Aggregate>>
{
    public void Configure(EntityTypeBuilder<<Aggregate>> builder)
    {
        builder.ToTable("<Aggregate>s");
        builder.HasKey(x => x.Id);

        builder.Ignore(x => x.DomainEvents); // never persisted

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);

        // SmartEnum -> string column
        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion(
                s => s.Name,
                v => <Aggregate>Status.FromName(v, false));

        // Value objects (same-context only): builder.OwnsOne(x => x.PaymentMethod, pm => { ... });
        // Cross-context refs: builder.Property(x => x.OtherId);  // plain Guid, no HasOne/HasForeignKey
    }
}
```

No registration needed — `<Context>DbContext.OnModelCreating` already calls `ApplyConfigurationsFromAssembly(typeof(<Context>DbContext).Assembly)`, which discovers this class.

## Step 8 — Controller shell

`WebApplications/<Context>.WebApi/Controllers/<Aggregate>sController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;

namespace <Context>.WebApi.Controllers;

[Route("api/[controller]")]
[Authorize]
[ApiController]
public class <Aggregate>sController : ControllerBase
{
    private readonly IMediator _mediator;
    public <Aggregate>sController(IMediator mediator) => _mediator = mediator;

    // Endpoints go here — add them with ddd-backend-slice.
}
```

`[Route("api/[controller]")]` yields `/api/<aggregate>s`. Role checks are NOT done here — they live in `UserValidator<T>` command validators (see `ddd-backend-slice`). `[Authorize]` only enforces authentication.

## Step 9 — Test builder

Tests build entities through the static `Create(...)` factory (private setters block NBuilder's `.With(x => x.Prop = ...)` on domain entities). Put a builder in `Core/<Context>/<Context>.Tests/Builders/<Aggregate>Builder.cs`:

```csharp
using <Context>.Domain.<Aggregates>;

namespace <Context>.Tests.Builders;

public static class <Aggregate>Builder
{
    public static <Aggregate> Build(string name = "Test name") => <Aggregate>.Create(name);

    public static <Aggregate> Suspended(this <Aggregate> e) { e.Suspend(); return e; }
    // one extension per named mutator you need in tests
}
```

For request DTOs (public setters) tests use NBuilder directly: `Builder<Create<Aggregate>Request>.CreateNew().With(r => r.Name = "X").Build()`.

## Step 10 — Migration

The new entity needs a schema. See `ddd-ef-migration` for the exact command — in short, from the repo root:

```
dotnet ef migrations add Add<Aggregate> --project Core\<Context>\<Context>.Infrastructure --startup-project WebApplications\<Context>.WebApi --output-dir Persistence\Migrations
```

`dotnet ef` discovers the entity via its `IEntityTypeConfiguration` (no `DbSet<T>` needed). Inspect the generated `Up()`, then `dotnet ef database update` (or just start the WebApi).

## Step 11 — Verify

`dotnet build DDD.sln`, fix errors, then add operations with `ddd-backend-slice` and tests with `ddd-backend-unit-test` / `ddd-backend-integration-test`.

---

## Heavier variant — a whole new bounded context

Only when the concept is a genuinely separate context (its own database, its own WebApi host). This is a much bigger job; copy `Core/Billing` (the simpler context) as the template and adapt. Beyond Steps 2–11 you also need:

1. **Four `.csproj` projects** under `Core/<Context>/` mirroring Billing's references; add them to `DDD.sln`.
2. **`<Context>DbContext`** in `<Context>.Infrastructure/Persistence/` with `ApplyConfigurationsFromAssembly(typeof(<Context>DbContext).Assembly)` plus the MassTransit outbox tables (`AddInboxStateEntity`/`AddOutboxMessageEntity`/`AddOutboxStateEntity`, prefixed `<Context>_`). Copy `BillingDbContext.cs`.
3. **DI extensions**: `Add<Context>Application()` (calls `services.AddBoundedContext(typeof(...).Assembly)`) and `Add<Context>Infrastructure(connectionString)` (`AddDbContext<<Context>DbContext>(UseSqlServer)` + `AddScoped<IUnitOfWork, EfCoreUnitOfWork<<Context>DbContext>>()`).
4. **`WebApplications/<Context>.WebApi`** host: copy `Scheduling.WebApi/Program.cs` — `AddServiceDefaults`, controllers + `ExceptionToJsonFilter`, `SmartEnumJsonConverterFactory`, OpenAPI/Scalar, health checks, `Add<Context>Infrastructure` + `Add<Context>Application` + `AddDefaultPipelineBehaviors`, `AddMassTransitEventBus<<Context>DbContext>`, `AddOidcCookieAuth`, CORS `"Angular"`.
5. **Aspire**: register the new WebApi in `Aspire.AppHost/AppHost.cs`.
6. **Connection string** via user secrets (shared `UserSecretsId`), and an **initial migration** (`ddd-ef-migration`).
