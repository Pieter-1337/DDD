---
name: 'ddd-backend-slice'
description: >
  Add one operation (or a CRUD bundle) to an existing aggregate in a bounded
  context under Core/<Context>/<Context>.Application. Each operation is its own
  file under <Aggregates>/Commands or <Aggregates>/Queries: a MediatR
  Command<T>/Query<T> record, request/response DTOs, a handler implementing
  IRequestHandler, and a FluentValidation validator (UserValidator for role-gated
  commands, AbstractValidator for queries). The action is wired into the
  aggregate's controller via IMediator.Send. Use after ddd-backend-module.
paths: Core/**, WebApplications/**
---

# DDD Backend Slice

Adds a vertical slice (command or query) to an existing aggregate. Everything flows through MediatR: the controller calls `_mediator.Send(request)`, the request runs through the pipeline behaviors (`Transaction → Logging → Validation → Performance → UnhandledException`), then the handler. Handlers and validators are **assembly-scanned** by `AddBoundedContext` — never wire them by hand.

## Conventions
- One file per operation under `Core/<Context>/<Context>.Application/<Aggregates>/Commands/` or `.../Queries/`.
  - Commands: `Create<Aggregate>Command.cs` (record + request/response + validators) **and** `Create<Aggregate>CommandHandler.cs` (handler in its own file). Mirror the live `CreatePatientCommand` split.
  - Queries: `Get<Aggregate>Query.cs` (record + validator) and `Get<Aggregate>QueryHandler.cs`.
- **Commands** are `public record X(...) : Command<TResponse>` (`BuildingBlocks.Application.Cqrs`). Commands are wrapped in a DB transaction by the `TransactionBehavior` (unless `SkipTransaction`).
- **Queries** are `public record X : Query<TResponse>` with `{ get; init; }` properties. Queries are read-only, NOT transactional.
- **Handlers** are `internal class X : IRequestHandler<TRequest, TResponse>` with a single `Handle(request, CancellationToken)` method. Inject `IUnitOfWork` only.
- **Command validators** that gate by role inherit `UserValidator<TCommand>` (`BuildingBlocks.Application.Validators`) — pass `ICurrentUser` + role groups to the base ctor. A failing role check raises `ErrorCode.Forbidden` → HTTP 403.
- **Request/field validators** inherit `AbstractValidator<TRequest>`. **Query validators** inherit `AbstractValidator<TQuery>` (no role gate) and may inject `IUnitOfWork` for async checks (e.g. existence).
- Validators are `internal` (test projects see them via `InternalsVisibleTo`; `AddValidatorsFromAssembly(..., includeInternalTypes: true)` registers them).
- Error codes: `.WithErrorCode(ErrorCode.X.Value).WithMessage(ErrorCode.X.Message)`. `.Value` is already `ERR_`-prefixed. The single-error `ValidationErrorWrapper` maps category to HTTP status (Forbidden→403, else 400).
- Persistence: `_uow.RepositoryFor<T>()` for `Add`/`Remove`/`GetByIdAsync`/`ExistsAsync`/`FirstOrDefaultAsDtoAsync<TDto>`/`GetAllAsDtosAsync<TDto>`, then `await _uow.SaveChangesAsync(ct)` (one call per handler — EF wraps it; domain events dispatch inside it).
- Writes go through the entity's static `Create(...)` / named mutators — never property-bag construction.
- **Command responses** inherit `SuccessOrFailureDto` (`Success`, `Message`) plus any ids. **Query responses** are DTOs / nullable DTOs.
- Build with `dotnet build DDD.sln`.

## Step 1 — Clarify scope
- Single operation or full CRUD bundle? (For a bundle, repeat Steps 2–4 per operation.)
- Command (mutates, POST/PUT/DELETE) or query (reads, GET)?
- For a command: which roles may perform it? What does the request contain, what does it return?
- For a query: what filter, what DTO shape?

## Step 2 — Command slice

`<Aggregates>/Commands/Create<Aggregate>Command.cs`:

```csharp
using Auth;
using BuildingBlocks.Application.Auth;
using BuildingBlocks.Application.Cqrs;
using BuildingBlocks.Application.Dtos;
using BuildingBlocks.Application.Validators;
using BuildingBlocks.Enumerations;
using FluentValidation;
using FluentValidation.Validators;
using <Context>.Domain.<Aggregates>;

namespace <Context>.Application.<Aggregates>.Commands;

public record Create<Aggregate>Command(Create<Aggregate>Request <Aggregate>) : Command<Create<Aggregate>CommandResponse>;

public class Create<Aggregate>Request
{
    public string Name { get; set; }
    public string Status { get; set; }
}

public class Create<Aggregate>CommandResponse : SuccessOrFailureDto
{
    public Guid <Aggregate>Id { get; set; }
}

#region Validators
internal class Create<Aggregate>CommandValidator : UserValidator<Create<Aggregate>Command>
{
    public Create<Aggregate>CommandValidator(
        ICurrentUser currentUser,
        IValidator<Create<Aggregate>Request> requestValidator)
        // Role groups: outer = OR, inner = AND. Here: Admin OR Doctor OR Nurse.
        : base(currentUser, new[] { AppRoles.Nurse }, new[] { AppRoles.Doctor }, new[] { AppRoles.Admin })
    {
        RuleFor(c => c.<Aggregate>).Cascade(CascadeMode.Stop)
            .NotNull()
            .SetValidator(requestValidator);
    }
}

internal class Create<Aggregate>RequestValidator : AbstractValidator<Create<Aggregate>Request>
{
    public Create<Aggregate>RequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty()
            .WithErrorCode(ErrorCode.Required.Value)
            .WithMessage(ErrorCode.Required.Message);

        RuleFor(r => r.Status)
            .Must(<Aggregate>Status.IsInEnum)
            .WithErrorCode(ErrorCode.InvalidStatus.Value)
            .WithMessage(ErrorCode.InvalidStatus.Message);
    }
}
#endregion
```

Handler — `<Aggregates>/Commands/Create<Aggregate>CommandHandler.cs`:

```csharp
using BuildingBlocks.Application.Interfaces;
using MediatR;
using <Context>.Domain.<Aggregates>;

namespace <Context>.Application.<Aggregates>.Commands;

internal class Create<Aggregate>CommandHandler : IRequestHandler<Create<Aggregate>Command, Create<Aggregate>CommandResponse>
{
    private readonly IUnitOfWork _uow;
    public Create<Aggregate>CommandHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<Create<Aggregate>CommandResponse> Handle(Create<Aggregate>Command cmd, CancellationToken ct)
    {
        var req = cmd.<Aggregate>;
        var status = <Aggregate>Status.FromName(req.Status);
        var entity = <Aggregate>.Create(req.Name, status);

        _uow.RepositoryFor<<Aggregate>>().Add(entity);
        await _uow.SaveChangesAsync(ct); // dispatches domain events, then SaveChanges

        return new Create<Aggregate>CommandResponse
        {
            Success = true,
            Message = "<Aggregate> successfully created",
            <Aggregate>Id = entity.Id,
        };
    }
}
```

**State-change command** (id only) — load, mutate, save:

```csharp
public record Suspend<Aggregate>Command : Command<Suspend<Aggregate>CommandResponse>
{
    public Guid Id { get; init; }
}

// handler:
var entity = await _uow.RepositoryFor<<Aggregate>>().GetByIdAsync(cmd.Id, ct);
entity!.Suspend();                 // validator already confirmed it exists
await _uow.SaveChangesAsync(ct);
return new() { Success = true, Message = "<Aggregate> suspended" };
```

Validate existence in the validator so the handler can assume the entity is present:

```csharp
internal class Suspend<Aggregate>CommandValidator : UserValidator<Suspend<Aggregate>Command>
{
    private readonly IUnitOfWork _uow;
    public Suspend<Aggregate>CommandValidator(ICurrentUser currentUser, IUnitOfWork uow)
        : base(currentUser, new[] { AppRoles.Doctor }, new[] { AppRoles.Admin })
    {
        _uow = uow;
        RuleFor(c => c.Id)
            .MustAsync((id, ct) => _uow.RepositoryFor<<Aggregate>>().ExistsAsync(id, ct))
            .WithErrorCode(ErrorCode.NotFound.Value)
            .WithMessage(ErrorCode.NotFound.Message);
    }
}
```

## Step 3 — Query slice

`<Aggregates>/Queries/Get<Aggregate>Query.cs`:

```csharp
using BuildingBlocks.Application.Cqrs;
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Enumerations;
using FluentValidation;
using <Context>.Application.<Aggregates>.Dtos;
using <Context>.Domain.<Aggregates>;

namespace <Context>.Application.<Aggregates>.Queries;

public record Get<Aggregate>Query : Query<<Aggregate>Dto?>
{
    public Guid Id { get; init; }
}

#region Validators
internal class Get<Aggregate>QueryValidator : AbstractValidator<Get<Aggregate>Query>
{
    private readonly IUnitOfWork _uow;
    public Get<Aggregate>QueryValidator(IUnitOfWork uow)
    {
        _uow = uow;
        RuleFor(q => q.Id)
            .MustAsync((id, ct) => _uow.RepositoryFor<<Aggregate>>().ExistsAsync(id, ct))
            .WithErrorCode(ErrorCode.NotFound.Value)
            .WithMessage(ErrorCode.NotFound.Message);
    }
}
#endregion
```

Handler — `<Aggregates>/Queries/Get<Aggregate>QueryHandler.cs`:

```csharp
using BuildingBlocks.Application.Interfaces;
using MediatR;
using <Context>.Application.<Aggregates>.Dtos;
using <Context>.Domain.<Aggregates>;

namespace <Context>.Application.<Aggregates>.Queries;

internal class Get<Aggregate>QueryHandler : IRequestHandler<Get<Aggregate>Query, <Aggregate>Dto?>
{
    private readonly IUnitOfWork _uow;
    public Get<Aggregate>QueryHandler(IUnitOfWork uow) => _uow = uow;

    public Task<<Aggregate>Dto?> Handle(Get<Aggregate>Query query, CancellationToken ct) =>
        _uow.RepositoryFor<<Aggregate>>()
            .FirstOrDefaultAsDtoAsync<<Aggregate>Dto>(e => e.Id == query.Id, ct);
}
```

List query — `Get<Aggregate>sQuery : Query<IEnumerable<<Aggregate>Dto>>` with optional filter props; handler uses `GetAllAsDtosAsync<<Aggregate>Dto>(filter, ct)`. `FirstOrDefaultAsDtoAsync` / `GetAllAsDtosAsync` project via the DTO's static `Project` expression — pure reads, no entity materialization.

## Step 4 — Register the endpoint

Add an action to the aggregate's controller (`WebApplications/<Context>.WebApi/Controllers/<Aggregate>sController.cs`). The controller already injects `IMediator`. Commands and queries both dispatch via `_mediator.Send(...)`.

```csharp
[HttpGet("{id}")]
[ProducesResponseType<<Aggregate>Dto>(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<IActionResult> Get<Aggregate>Async(Guid id)
{
    var dto = await _mediator.Send(new Get<Aggregate>Query { Id = id });
    return Ok(dto);
}

[HttpPost("")]
[ProducesResponseType<Create<Aggregate>CommandResponse>(StatusCodes.Status201Created)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<IActionResult> Create<Aggregate>Async(Create<Aggregate>Request request)
{
    var response = await _mediator.Send(new Create<Aggregate>Command(request));
    return CreatedAtAction(nameof(Get<Aggregate>Async), new { id = response.<Aggregate>Id }, response);
}

[HttpPost("{id}/suspend")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<IActionResult> Suspend<Aggregate>Async(Guid id) =>
    Ok(await _mediator.Send(new Suspend<Aggregate>Command { Id = id }));
```

No try/catch and no manual validation: `ValidationException` from the pipeline is caught by `ExceptionToJsonFilter` (registered globally in `Program.cs`) and rendered as `{ errors: [{ code, message }], warnings: [] }` with status 400 (or 403 for `ERR_FORBIDDEN`). The class-level `[Authorize]` enforces authentication; per-operation **role** rules live in the command validator.

## Step 5 — If the entity/schema changed, generate a migration

If this slice added a field, index, or constraint to the entity / its `IEntityTypeConfiguration`, run `ddd-ef-migration`. Pure read slices and slices over an unchanged schema need no migration.

## Step 6 — Verify

`dotnet build DDD.sln`, then add tests: `ddd-backend-unit-test` for the validator (mocked `IUnitOfWork`) and `ddd-backend-integration-test` for the handler (real SQLite + MediatR).
