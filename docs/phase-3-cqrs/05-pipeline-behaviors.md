# MediatR Pipeline Behaviors

## What Are Pipeline Behaviors?

Pipeline behaviors are **cross-cutting concerns** that run for every request. They wrap handlers like middleware:

```
Request → Behavior 1 → Behavior 2 → Behavior 3 → Behavior 4 → Behavior 5 → Handler → Response
              ↓            ↓            ↓            ↓            ↓
         Transaction   Logging    Validation   Performance  UnhandledException
```

Think of them as "middleware for MediatR."

---

## Why Use Pipeline Behaviors?

Without behaviors, you repeat code in every handler:

```csharp
// BAD - repeated in every handler
public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
{
    _logger.LogInformation("Handling CreatePatientCommand");  // Logging
    var stopwatch = Stopwatch.StartNew();                     // Performance

    // Actual logic...

    stopwatch.Stop();
    _logger.LogInformation("Handled in {Ms}ms", stopwatch.ElapsedMilliseconds);
    return patient.Id;
}
```

With behaviors, cross-cutting concerns are centralized:

```csharp
// GOOD - handler is clean
public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
{
    var patient = Patient.Create(...);
    _unitOfWork.RepositoryFor<Patient>().Add(patient);
    await _unitOfWork.SaveChangesAsync(ct);
    return patient.Id;
}

// Logging and performance handled by behaviors automatically
```

---

## Where Do Behaviors Live?

Pipeline behaviors are **cross-cutting concerns** - they apply identically to all bounded contexts. They live in `BuildingBlocks.Application/Behaviors/`.

```
BuildingBlocks.Application/
├── Behaviors/                       <- Cross-cutting pipeline behaviors
│   ├── LoggingBehavior.cs
│   ├── PerformanceBehavior.cs
│   ├── TransactionBehavior.cs       <- Uses IUnitOfWork + Command<T> type checking
│   ├── UnhandledExceptionBehavior.cs
│   └── ValidationBehavior.cs        <- Uses IValidator<T> (generic)
├── Interfaces/
│   ├── IUnitOfWork.cs
│   └── IRepository.cs
├── Cqrs/                            <- Base types for CQRS
│   ├── Command.cs                   <- Base record for commands (wrapped in transaction)
│   ├── Query.cs                     <- Base record for queries (no transaction)
│   └── OrchestrationCommand.cs      <- Base record for orchestrators (no transaction)
├── Validators/
│   └── UserValidator.cs
└── BuildingBlocksServiceCollectionExtensions.cs  <- AddBoundedContext() + AddDefaultPipelineBehaviors()

Scheduling.Application/              <- Bounded context handlers only
├── Patients/
│   ├── Commands/
│   └── Queries/
└── ServiceCollectionExtensions.cs   <- Just calls AddBoundedContext()
```

**Why in BuildingBlocks.Application?**
- Behaviors use abstractions (`IUnitOfWork`, `ILogger`) that are already in this project
- Bounded contexts reference `BuildingBlocks.Application` anyway, so they get behaviors transitively
- Web apps don't need additional project references - `AddDefaultPipelineBehaviors()` is available through the transitive dependency
- Keeps the solution simpler (fewer projects)

**Rule of thumb:**
- All cross-cutting concerns (behaviors, interfaces, base classes) → `BuildingBlocks.Application`
- Bounded contexts contain only their handlers, validators, and DTOs

---

## What You Need To Do

### Prerequisites

Ensure `BuildingBlocks.Application.csproj` has the logging abstractions package:

```xml
<ItemGroup>
    <PackageReference Include="FluentValidation" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
    <PackageReference Include="MediatR" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />  <!-- For ILogger<T> -->
</ItemGroup>
```

This provides `ILogger<T>` without pulling in the full ASP.NET Core dependency. The actual logging implementation (console, Serilog, etc.) is configured in the WebApi project.

---

### Behavior Summary

| Behavior | Purpose | Applies To | Overridable Methods |
|----------|---------|------------|---------------------|
| `TransactionBehavior` | Wraps in DB transaction | Commands only | `ShouldApplyTransaction` |
| `LoggingBehavior` | Logs request start/end/error | All requests (skips ValidationException) | `OnHandling`, `OnHandled`, `OnError` |
| `ValidationBehavior` | Validates input, throws on failure | All requests | `ValidateAsync`, `OnValidationFailure` |
| `PerformanceBehavior` | Warns on slow requests (>500ms) | All requests | `ThresholdMilliseconds`, `OnSlowRequest` |
| `UnhandledExceptionBehavior` | Catches & logs exceptions | All requests (skips ValidationException) | `OnException` |

---

**Note:** `TransactionBehavior` is the outermost behavior (first in the pipeline) but is covered in its own detailed section below because it requires the `Command<T>`/`Query<T>` base types.

### Step 1: Create TransactionBehavior

See the dedicated **Transaction Behavior** section below - it includes the base types (`Command<T>`, `Query<T>`, `OrchestrationCommand<T>`) that are required for type-safe command/query distinction.

### Step 2: Create LoggingBehavior

**What it does:** Logs when a request starts and when it completes (or fails).

**Why it's useful:**
- Provides visibility into what the application is doing
- Helps trace request flow in production logs
- Logs exceptions with the request name for easier debugging
- Enables correlation of logs across distributed systems (when combined with correlation IDs)

**Example log output:**
```
[INF] Handling CreatePatientCommand
[INF] Handled CreatePatientCommand
```

On failure (system error):
```
[INF] Handling CreatePatientCommand
[ERR] Error handling CreatePatientCommand - System.InvalidOperationException: ...
```

**Note:** `ValidationException` is NOT logged as an error - it's an expected client input error, not a system error. Only unexpected exceptions are logged.

Location: `BuildingBlocks/BuildingBlocks.Application/Behaviors/LoggingBehavior.cs`

```csharp
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Application.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    protected readonly ILogger Logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        Logger = logger;
    }

    public virtual async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        OnHandling(requestName, request);

        try
        {
            var response = await next();
            OnHandled(requestName, request);
            return response;
        }
        catch (Exception ex)
        {
            OnError(requestName, request, ex);
            throw;
        }
    }

    protected virtual void OnHandling(string requestName, TRequest request)
        => Logger.LogInformation("Handling {RequestName}", requestName);

    protected virtual void OnHandled(string requestName, TRequest request)
        => Logger.LogInformation("Handled {RequestName}", requestName);

    protected virtual void OnError(string requestName, TRequest request, Exception ex)
    {
        // ValidationException is expected (client input error), not a system error
        if (ex is ValidationException) return;

        Logger.LogError(ex, "Error handling {RequestName}", requestName);
    }
}
```

**Extensibility:** All methods are `virtual` and `Logger` is `protected`, so web apps can override specific hooks or the entire `Handle` method.

### Step 3: Create ValidationBehavior

**What it does:** Runs all FluentValidation validators for the request before the handler executes. Throws `ValidationException` if any validation fails.

**Why it's useful:**
- Validates input BEFORE any business logic runs
- Catches bad data early (fail fast)
- Returns user-friendly validation errors
- Validators are automatically discovered and injected

**How it works:**
```
Request arrives
  → ValidationBehavior
    → Runs all IValidator<TRequest> instances
      → Valid? → Call next() to continue to handler
      → Invalid? → Throw ValidationException (caught by ExceptionToJsonFilter)
```

Location: `BuildingBlocks/BuildingBlocks.Application/Behaviors/ValidationBehavior.cs`

```csharp
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace BuildingBlocks.Application.Behaviors;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    protected readonly IEnumerable<IValidator<TRequest>> Validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        Validators = validators;
    }

    public virtual async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (Validators.Any())
        {
            await ValidateAsync(request, cancellationToken);
        }

        return await next();
    }

    protected virtual async Task ValidateAsync(TRequest request, CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);
        var failures = new List<ValidationFailure>();

        foreach (var validator in Validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors.Where(f => f is not null));
        }

        if (failures.Count > 0)
        {
            OnValidationFailure(request, failures);
        }
    }

    protected virtual void OnValidationFailure(TRequest request, List<ValidationFailure> failures)
        => throw new ValidationException(failures);
}
```

**Extensibility:** Override `ValidateAsync` to customize validation logic, or override `OnValidationFailure` to customize error handling.

### Step 4: Create PerformanceBehavior

**What it does:** Measures how long each request takes to execute. Logs a warning if it exceeds a threshold (default: 500ms).

**Why it's useful:**
- Identifies slow queries/commands that need optimization
- Provides early warning of performance degradation
- Helps find N+1 query problems, missing indexes, or expensive operations
- Only logs when threshold exceeded (no noise for fast requests)

**Example log output (only when slow):**
```
[WRN] Long running request: GetAllPatientsQuery (1523ms)
```

**Tip:** You can make the threshold configurable via `IOptions<PerformanceSettings>` for different environments.

Location: `BuildingBlocks/BuildingBlocks.Application.Behaviors/PerformanceBehavior.cs`

```csharp
using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Application.Behaviors;

public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    protected readonly ILogger Logger;
    protected readonly Stopwatch Timer;

    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    {
        Logger = logger;
        Timer = new Stopwatch();
    }

    protected virtual int ThresholdMilliseconds => 500;

    public virtual async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        Timer.Start();

        var response = await next();

        Timer.Stop();

        var elapsedMilliseconds = Timer.ElapsedMilliseconds;

        if (elapsedMilliseconds > ThresholdMilliseconds)
        {
            var requestName = typeof(TRequest).Name;
            OnSlowRequest(requestName, request, elapsedMilliseconds);
        }

        return response;
    }

    protected virtual void OnSlowRequest(string requestName, TRequest request, long elapsedMilliseconds)
        => Logger.LogWarning(
            "Long running request: {RequestName} ({ElapsedMilliseconds}ms)",
            requestName,
            elapsedMilliseconds);
}
```

**Extensibility:** Override `ThresholdMilliseconds` to change the slow request threshold, or override `OnSlowRequest` to customize the logging.

### Step 5: Create UnhandledExceptionBehavior

**What it does:** Catches any unhandled exception, logs it with full request details, then re-throws it.

**Why it's useful:**
- Ensures ALL exceptions are logged with context (request name + request data)
- Uses structured logging (`{@Request}`) to serialize the entire request object
- Acts as a safety net - even if other error handling fails, this captures it

**Example log output:**
```
[ERR] Unhandled exception for request CreatePatientCommand: {"Patient":{"FirstName":"John","LastName":"Doe",...}}
      System.InvalidOperationException: Patient already exists
         at Scheduling.Application.Patients.Commands.CreatePatientCommandHandler.Handle(...)
```

**Note:**
- `ValidationException` is NOT logged - it's an expected client input error, not a system error
- This behavior re-throws the exception after logging. The `ExceptionToJsonFilter` (in WebApplications) then converts it to an HTTP response.

Location: `BuildingBlocks/BuildingBlocks.Application/Behaviors/UnhandledExceptionBehavior.cs`

```csharp
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Application.Behaviors;

public class UnhandledExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    protected readonly ILogger Logger;

    public UnhandledExceptionBehavior(ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger)
    {
        Logger = logger;
    }

    public virtual async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex)
        {
            var requestName = typeof(TRequest).Name;
            OnException(requestName, request, ex);
            throw;
        }
    }

    protected virtual void OnException(string requestName, TRequest request, Exception ex)
    {
        // ValidationException is expected (client input error), not unhandled
        if (ex is ValidationException) return;

        Logger.LogError(ex,
            "Unhandled exception for request {RequestName}: {@Request}",
            requestName,
            request);
    }
}
```

**Extensibility:** Override `OnException` to customize exception logging (e.g., add correlation IDs, sanitize sensitive data).

---

### Step 6: Register Behaviors

Both methods live in `BuildingBlocks/BuildingBlocks.Application/BuildingBlocksServiceCollectionExtensions.cs`:

```csharp
using BuildingBlocks.Application.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace BuildingBlocks.Application;

public static class BuildingBlocksServiceCollectionExtensions
{
    private static bool _fluentValidationDefaultsConfigured;

    /// <summary>
    /// Registers a bounded context's MediatR handlers and FluentValidation validators.
    /// Does NOT register pipeline behaviors - call AddDefaultPipelineBehaviors() separately.
    /// </summary>
    public static IServiceCollection AddBoundedContext(this IServiceCollection services, Assembly boundedContextAssembly)
    {
        SetFluentValidationDefaults();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(boundedContextAssembly);
        });

        services.AddValidatorsFromAssembly(boundedContextAssembly, includeInternalTypes: true);

        return services;
    }

    /// <summary>
    /// Registers the default pipeline behaviors from BuildingBlocks.
    /// Call this after AddMediatR to add cross-cutting behaviors.
    /// Order matters: behaviors execute in the order they are registered (first = outermost).
    /// </summary>
    public static IServiceCollection AddDefaultPipelineBehaviors(this IServiceCollection services)
    {
        // Order: Transaction -> Logging -> Validation -> Performance -> UnhandledException -> Handler
        // Transaction is first so validators run inside the transaction (if they need DB access)
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehavior<,>));

        return services;
    }

    private static void SetFluentValidationDefaults()
    {
        if (_fluentValidationDefaultsConfigured) return;

        // Use property names as-is (no PascalCase to "Display Name" conversion)
        ValidatorOptions.Global.DisplayNameResolver = (type, member, expression) => member?.Name;

        _fluentValidationDefaultsConfigured = true;
    }
}
```

**Pipeline execution order:**
```
Request
  → TransactionBehavior (starts transaction for commands)
    → LoggingBehavior (logs start/end)
      → ValidationBehavior (validates input)
        → PerformanceBehavior (measures time)
          → UnhandledExceptionBehavior (catches exceptions)
            → Handler (actual logic)
```

### Step 7: Bounded Context Registration

Each bounded context registers handlers and validators only:

Location: `Core/Scheduling/Scheduling.Application/ServiceCollectionExtensions.cs`

```csharp
using BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingApplication(this IServiceCollection services)
    {
        // Registers MediatR handlers and validators only (no behaviors)
        services.AddBoundedContext(typeof(ServiceCollectionExtensions).Assembly);

        // Add any Scheduling-specific services here

        return services;
    }
}
```

### Step 8: Web App Registration

The web application decides which behaviors to use. WebApi already gets `BuildingBlocks.Application` transitively through Scheduling references, so no additional project references are needed for behaviors.

**WebApi/WebApi.csproj:**
```xml
<ItemGroup>
  <ProjectReference Include="..\Core\Scheduling\Scheduling.Infrastructure\Scheduling.Infrastructure.csproj" />
  <ProjectReference Include="..\BuildingBlocks\BuildingBlocks.WebApplications\BuildingBlocks.WebApplications.csproj" />
</ItemGroup>
```

**WebApi/Program.cs - Using defaults:**
```csharp
using BuildingBlocks.Application;
using BuildingBlocks.WebApplications.Filters;
using BuildingBlocks.WebApplications.Json;
using Scheduling.Application;
using Scheduling.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Register the exception filter globally (converts ValidationException to proper JSON response)
builder.Services.AddControllers(options =>
{
    options.Filters.Add<ExceptionToJsonFilter>();
}).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new SmartEnumJsonConverterFactory());
});

// Register bounded contexts
builder.Services.AddSchedulingApplication();

// Register default pipeline behaviors
builder.Services.AddDefaultPipelineBehaviors();
```

**Important:** The `ExceptionToJsonFilter` is required to convert `ValidationException` (thrown by `ValidationBehavior`) into proper 400 responses. Without it, validation failures return 500 errors.

**AdminApi/Program.cs - Using custom behaviors:**
```csharp
using BuildingBlocks.Application.Behaviors;
using MediatR;

// Register bounded contexts
builder.Services.AddSchedulingApplication();

// Register custom behaviors (instead of defaults)
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AdminLoggingBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

### Step 9: Override Behaviors in Web Apps (Optional)

Create a custom behavior that inherits from the base and overrides specific methods:

Location: `AdminApi/Behaviors/AdminLoggingBehavior.cs`

```csharp
using BuildingBlocks.Application.Behaviors;
using Microsoft.Extensions.Logging;

namespace AdminApi.Behaviors;

public class AdminLoggingBehavior<TRequest, TResponse> : LoggingBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public AdminLoggingBehavior(ILogger<AdminLoggingBehavior<TRequest, TResponse>> logger)
        : base(logger) { }

    protected override void OnHandling(string requestName, TRequest request)
        => Logger.LogInformation("ADMIN: Starting {RequestName} by {User} at {Time}",
            requestName, "admin-user", DateTime.UtcNow);

    protected override void OnHandled(string requestName, TRequest request)
        => Logger.LogInformation("ADMIN: Completed {RequestName}", requestName);

    protected override void OnError(string requestName, TRequest request, Exception ex)
        => Logger.LogError(ex, "ADMIN: Failed {RequestName} - notifying admin team", requestName);
}
```

**What you can override:**

| Base Class | Overridable Members |
|------------|---------------------|
| `TransactionBehavior` | `Handle`, `ShouldApplyTransaction` |
| `LoggingBehavior` | `Handle`, `OnHandling`, `OnHandled`, `OnError` |
| `ValidationBehavior` | `Handle`, `ValidateAsync`, `OnValidationFailure` |
| `PerformanceBehavior` | `Handle`, `ThresholdMilliseconds`, `OnSlowRequest` |
| `UnhandledExceptionBehavior` | `Handle`, `OnException` |

---

## Transaction Behavior

**What it does:** Wraps command execution in a database transaction. Commits on success or ValidationException (expected client error), rolls back on any other exception.

**Why it's useful:**
- Ensures atomicity: either all database changes succeed, or none do
- Prevents partial updates when a command makes multiple changes
- Uses type-based checking (`Command<T>`) not string-based naming conventions
- Commits on `ValidationException` because validators may have modified state (e.g., checked existence)
- Uses `IUnitOfWork` so it works with any bounded context's DbContext
- **Ensures integration events are only published after successful commit**

**When to use it:**
- Commands that modify multiple aggregates
- Commands where you need "all or nothing" behavior
- When handlers call `SaveChangesAsync()` multiple times

**When you might NOT need it:**
- If your handlers only modify one aggregate and call `SaveChangesAsync()` once
- EF Core already wraps `SaveChangesAsync()` in an implicit transaction
- For read-only queries (behavior skips these automatically based on type)

**Flow (including event publishing):**
```
Command arrives (inherits from Command<T>)
  |
  +-- BeginTransactionAsync()
  |
  +-- Handler executes
  |     |
  |     +-- SaveChangesAsync()
  |           +-- DispatchDomainEventsAsync() [MediatR, BEFORE DB save]
  |           +-- _context.SaveChangesAsync() [DB save, in transaction]
  |           +-- [integration events queued, NOT published yet]
  |
  +-- CloseTransactionAsync()
        |
        +-- Success? --> CommitAsync()
        |                 +-- PublishAndClearIntegrationEventsAsync() [AFTER commit]
        |
        +-- ValidationException? --> CommitAsync() + Re-throw
        |                             +-- PublishAndClearIntegrationEventsAsync() [AFTER commit]
        |
        +-- Other Exception? --> RollbackAsync() + Re-throw
                                  +-- _queuedIntegrationEvents.Clear() [events DISCARDED]
```

**Key insight:** Integration events are deferred until after the transaction commits. This guarantees that:
1. Events are never published for rolled-back operations
2. Consumers only see events for data that is actually persisted
3. If publishing fails after commit, the data is still saved (at-least-once delivery)

Location: `BuildingBlocks/BuildingBlocks.Application/Behaviors/TransactionBehavior.cs`

```csharp
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Cqrs;
using FluentValidation;
using MediatR;

namespace BuildingBlocks.Application.Behaviors;

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    protected readonly IUnitOfWork UnitOfWork;

    public TransactionBehavior(IUnitOfWork unitOfWork)
    {
        UnitOfWork = unitOfWork;
    }

    public virtual async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!ShouldApplyTransaction(request))
        {
            return await next();
        }

        await UnitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var response = await next();
            await UnitOfWork.CloseTransactionAsync(cancellationToken: cancellationToken);
            return response;
        }
        catch (ValidationException)
        {
            // Commit on ValidationException - validators may have side effects
            await UnitOfWork.CloseTransactionAsync(cancellationToken: cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            await UnitOfWork.CloseTransactionAsync(ex, cancellationToken);
            throw;
        }
    }

    protected virtual bool ShouldApplyTransaction(TRequest request)
    {
        // Type-based checking using Command<T> base record
        if (request is Command<TResponse> command)
        {
            return !command.SkipTransaction;
        }
        return false;
    }
}
```

**Extensibility:** Override `ShouldApplyTransaction` to customize transaction logic. The default checks:
1. Is the request a `Command<T>`? (queries never get transactions)
2. Does the command have `SkipTransaction = false`? (orchestration commands skip)

---

## Nested Transaction Handling

When a command handler dispatches another command via MediatR, both commands go through the `TransactionBehavior`. The `EfCoreUnitOfWork` detects if a transaction is already active and handles nesting correctly using a depth counter.

### How It Works

The `EfCoreUnitOfWork` tracks transaction depth using a counter. When a nested command tries to begin a transaction:

1. **Outer command** calls `BeginTransactionAsync()` - starts the actual database transaction, depth becomes 1
2. **Nested command** calls `BeginTransactionAsync()` - increments depth to 2, reuses existing transaction
3. **Nested command** completes and calls `CloseTransactionAsync()` - decrements depth to 1, does nothing else
4. **Outer command** completes and calls `CloseTransactionAsync()` - decrements depth to 0, commits/rollbacks the transaction
5. **Integration events** are published only after the outer command's transaction commits (when depth reaches 0)

### Example: Nested Command Execution

```csharp
// Outer command handler
public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    private readonly IUnitOfWork _uow;
    private readonly IMediator _mediator;

    public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
    {
        var patient = Patient.Create(cmd.FirstName, cmd.LastName, cmd.Email, cmd.DateOfBirth);
        _uow.RepositoryFor<Patient>().Add(patient);

        // This nested command reuses the same transaction!
        await _mediator.Send(new CreateAuditLogCommand
        {
            Action = "PatientCreated",
            EntityId = patient.Id
        }, ct);

        await _uow.SaveChangesAsync(ct);
        return patient.Id;
    }
}
// Both commands share ONE transaction
// Commit happens after outer command completes
```

### Implementation in EfCoreUnitOfWork

The key is the `_transactionDepth` counter that tracks how many layers have called `BeginTransactionAsync()`:

```csharp
// In EfCoreUnitOfWork
private IDbContextTransaction? _transaction;
private int _transactionDepth;

public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
{
    if (_transaction is not null)
    {
        // Transaction already exists - reuse it, increment depth
        _transactionDepth++;
        return;
    }

    // Start new transaction
    _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    _transactionDepth = 1;
}

public async Task CloseTransactionAsync(Exception? exception = null, CancellationToken cancellationToken = default)
{
    if (_transaction is null) return;

    // Decrement depth - only commit/rollback when we reach 0 (outermost caller)
    _transactionDepth--;
    if (_transactionDepth > 0) return;

    if (exception is not null)
    {
        await _transaction.RollbackAsync(cancellationToken);
        _queuedIntegrationEvents.Clear();
    }
    else
    {
        await _transaction.CommitAsync(cancellationToken);
        await PublishAndClearIntegrationEventsAsync(cancellationToken);
    }

    await _transaction.DisposeAsync();
    _transaction = null;
}
```

**Why a depth counter instead of a boolean flag?**

The original boolean `_ownsTransaction` approach had a bug: when a nested command called `BeginTransactionAsync()`, it would set `_ownsTransaction = false`, overwriting the outer command's ownership. If there were multiple nesting levels (A calls B calls C), only the immediate caller's ownership was tracked.

The depth counter fixes this by:
- Incrementing on each `BeginTransactionAsync()` call
- Decrementing on each `CloseTransactionAsync()` call
- Only committing/rolling back when depth reaches 0 (the outermost caller)

### Execution Flow with Nested Commands

```
TransactionBehavior (Outer: CreatePatientCommand)
  |
  +-- BeginTransactionAsync()
  |     +-- _transaction = new  [STARTED]
  |     +-- _transactionDepth = 1
  |
  +-- CreatePatientCommandHandler.Handle()
        |
        +-- Patient.Create(...)
        |
        +-- _mediator.Send(CreateAuditLogCommand)
        |     |
        |     +-- TransactionBehavior (Inner: CreateAuditLogCommand)
        |           |
        |           +-- BeginTransactionAsync()
        |           |     +-- _transaction already exists
        |           |     +-- _transactionDepth = 2  [INCREMENTED]
        |           |
        |           +-- CreateAuditLogCommandHandler.Handle()
        |           |     +-- ... audit logic ...
        |           |     +-- _uow.SaveChangesAsync()
        |           |
        |           +-- CloseTransactionAsync()
        |                 +-- _transactionDepth = 1  [DECREMENTED]
        |                 +-- [DOES NOTHING - depth > 0]
        |
        +-- _uow.SaveChangesAsync()
        |
  +-- CloseTransactionAsync()
        +-- _transactionDepth = 0  [DECREMENTED]
        +-- CommitAsync()  [COMMITTED - depth reached 0]
        +-- PublishAndClearIntegrationEventsAsync()  [EVENTS PUBLISHED]
```

### Key Rules

| Rule | Behavior |
|------|----------|
| First `BeginTransactionAsync()` | Starts transaction, depth = 1 |
| Subsequent `BeginTransactionAsync()` | Reuses transaction, depth++ |
| `CloseTransactionAsync()` when depth > 1 | Decrements depth, does nothing else |
| `CloseTransactionAsync()` when depth = 1 | Decrements to 0, commits/rollbacks, publishes events |
| Rollback | Discards all queued integration events |
| Integration events | Published only when depth reaches 0 (outermost caller) |

### When to Use Nested Commands

**Good use cases:**
- Audit logging that must succeed with the main operation
- Creating related entities in the same transaction
- Operations that must be atomic with the parent operation

**Avoid for:**
- Independent operations that should have their own transaction boundaries
- Long-running operations that could cause lock contention
- Operations that might need to succeed even if the parent fails

For truly independent operations, use `OrchestrationCommand<T>` instead, which lets each child command have its own transaction.

---

## Command/Query Base Types

The framework provides base record types for type-safe CQRS:

### Command<TResponse>

Commands are write operations wrapped in a database transaction by default.

```csharp
using MediatR;
using System.Text.Json.Serialization;

namespace BuildingBlocks.Application.Cqrs;

public abstract record Command<TResponse> : IRequest<TResponse>
{
    /// <summary>
    /// If true, the command will NOT be wrapped in a transaction.
    /// Used by OrchestrationCommand or when you need manual transaction control.
    /// </summary>
    [JsonIgnore]
    public bool SkipTransaction { get; init; }
}
```

### Query<TResponse>

Queries are read-only operations that never get wrapped in a transaction.

```csharp
using MediatR;

namespace BuildingBlocks.Application.Cqrs;

public abstract record Query<TResponse> : IRequest<TResponse>;
```

### OrchestrationCommand<TResponse>

Orchestration commands coordinate multiple child commands but do NOT get wrapped in a transaction themselves. Each child command dispatched via `IMediator.Send()` gets its own transaction.

```csharp
namespace BuildingBlocks.Application.Cqrs;

public abstract record OrchestrationCommand<TResponse> : Command<TResponse>
{
    /// <summary>
    /// Always true for orchestration commands - they never get wrapped in a transaction.
    /// </summary>
    public new bool SkipTransaction => true;
}
```

**Use OrchestrationCommand for:**
- Long-running workflows
- Saga/Process Manager patterns
- Operations involving external services
- When you need independent transaction boundaries per step

**IMPORTANT:** Orchestration handlers should ONLY dispatch other commands, never perform direct database operations.

---

## Using Base Types in Commands/Queries

### Commands

```csharp
using BuildingBlocks.Application.Cqrs;

// Simple command (wrapped in transaction)
public record SuspendPatientCommand : Command<SuspendPatientCommandResponse>
{
    public Guid Id { get; init; }
}

// Command with primary constructor
public record CreatePatientCommand(CreatePatientRequest Patient) : Command<CreatePatientCommandResponse>;
```

### Queries

```csharp
using BuildingBlocks.Application.Cqrs;

// Simple query (no transaction)
public record GetPatientQuery : Query<PatientDto?>
{
    public Guid Id { get; init; }
}

// Query with enum filter
public record GetAllPatientsQuery : Query<IEnumerable<PatientDto>>
{
    public PatientStatus Status { get; init; }
}
```

### Orchestration Commands

```csharp
using BuildingBlocks.Application.Cqrs;

// Coordinates multiple commands - no transaction wrapper
public record TransferPatientCommand : OrchestrationCommand<TransferPatientResponse>
{
    public Guid PatientId { get; init; }
    public Guid FromDoctorId { get; init; }
    public Guid ToDoctorId { get; init; }
}

// Handler only dispatches other commands
public class TransferPatientCommandHandler : IRequestHandler<TransferPatientCommand, TransferPatientResponse>
{
    private readonly IMediator _mediator;

    public async Task<TransferPatientResponse> Handle(TransferPatientCommand request, CancellationToken ct)
    {
        // Each command gets its own transaction
        await _mediator.Send(new RemovePatientFromDoctorCommand(request.PatientId, request.FromDoctorId), ct);
        await _mediator.Send(new AssignPatientToDoctorCommand(request.PatientId, request.ToDoctorId), ct);

        return new TransferPatientResponse { Success = true };
    }
}
```

---

## Behavior Execution Flow

Visual representation of the pipeline:

```
┌──────────────────────────────────────────────────────────────────┐
│                        HTTP Request                               │
└────────────────────────────┬─────────────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────────────┐
│                      Controller                                   │
│                  await _mediator.Send(command)                    │
└────────────────────────────┬─────────────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────────────┐
│                TransactionBehavior                                │
│    Begin transaction → next() → Commit/Rollback                  │
│    (Only for Command<T>, skips Query<T> and OrchestrationCommand) │
└────────────────────────────┬─────────────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────────────┐
│                  LoggingBehavior                                  │
│    Log("Handling...") → next() → Log("Handled")                  │
└────────────────────────────┬─────────────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────────────┐
│                ValidationBehavior                                 │
│    Validate request → throw if invalid → next() if valid         │
└────────────────────────────┬─────────────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────────────┐
│               PerformanceBehavior                                 │
│    Start timer → next() → Stop timer → Log if slow               │
└────────────────────────────┬─────────────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────────────┐
│              UnhandledExceptionBehavior                           │
│    try { next() } catch { log & rethrow }                        │
└────────────────────────────┬─────────────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────────────┐
│                      Handler                                      │
│           CreatePatientCommandHandler.Handle()                    │
│                    (actual business logic)                        │
└──────────────────────────────────────────────────────────────────┘
```

---

## Verification Checklist

- [ ] `TransactionBehavior` created with `virtual` methods in BuildingBlocks.Application/Behaviors
- [ ] `LoggingBehavior` created with `virtual` methods in BuildingBlocks.Application/Behaviors
- [ ] `ValidationBehavior` created with `virtual` methods in BuildingBlocks.Application/Behaviors
- [ ] `PerformanceBehavior` created with `virtual` methods in BuildingBlocks.Application/Behaviors
- [ ] `UnhandledExceptionBehavior` created with `virtual` methods in BuildingBlocks.Application/Behaviors
- [ ] `AddBoundedContext()` in BuildingBlocks.Application registers handlers and validators only
- [ ] `AddDefaultPipelineBehaviors()` in BuildingBlocks.Application registers the default behaviors
- [ ] BuildingBlocks.Application.csproj has Microsoft.Extensions.Logging.Abstractions package
- [ ] WebApi references BuildingBlocks.WebApplications for the exception filter
- [ ] WebApi registers `ExceptionToJsonFilter` in controller options
- [ ] WebApi calls both `AddSchedulingApplication()` and `AddDefaultPipelineBehaviors()`
- [ ] Validation failures return proper 400 responses with structured JSON
- [ ] Slow requests are logged as warnings
- [ ] (Optional) Custom behaviors can inherit and override specific methods

---

## Folder Structure After This Step

```
BuildingBlocks/
├── BuildingBlocks.Application/
│   ├── Behaviors/                              <- Cross-cutting behaviors (in pipeline order)
│   │   ├── TransactionBehavior.cs              <- ShouldApplyTransaction (type-based)
│   │   ├── LoggingBehavior.cs                  <- OnHandling, OnHandled, OnError
│   │   ├── ValidationBehavior.cs               <- ValidateAsync, OnValidationFailure
│   │   ├── PerformanceBehavior.cs              <- ThresholdMilliseconds, OnSlowRequest
│   │   └── UnhandledExceptionBehavior.cs       <- OnException
│   ├── Dtos/
│   │   └── SuccessOrFailureDto.cs
│   ├── Interfaces/
│   │   ├── IRepository.cs
│   │   └── IUnitOfWork.cs
│   ├── Cqrs/                                   <- Base types for CQRS
│   │   ├── Command.cs                          <- Base record (wrapped in transaction)
│   │   ├── Query.cs                            <- Base record (no transaction)
│   │   └── OrchestrationCommand.cs             <- Base record (no transaction, orchestrates)
│   ├── Validators/
│   │   └── UserValidator.cs
│   └── BuildingBlocksServiceCollectionExtensions.cs  <- AddBoundedContext() + AddDefaultPipelineBehaviors()
│
└── BuildingBlocks.WebApplications/
    └── Filters/
        ├── ExceptionToJsonFilter.cs
        └── ValidationErrorWrapper.cs

Core/Scheduling/
└── Scheduling.Application/
    ├── Patients/
    │   ├── Commands/
    │   ├── Queries/
    │   └── EventHandlers/
    └── ServiceCollectionExtensions.cs          <- Calls AddBoundedContext()

WebApi/
├── WebApi.csproj                               <- References Scheduling.Infrastructure (gets BuildingBlocks.Application transitively)
└── Program.cs                                  <- Calls AddDefaultPipelineBehaviors()

AdminApi/                                       <- Optional: Custom behaviors
├── Behaviors/
│   └── AdminLoggingBehavior.cs                 <- Inherits + overrides
└── Program.cs                                  <- Registers custom behaviors
```

---

## Phase 3 Complete!

You now have a complete CQRS implementation:

- **Commands** - Write operations with domain logic
- **Queries** - Read operations with DTOs
- **Validation** - FluentValidation for input validation
- **Pipeline Behaviors** - Cross-cutting concerns (transactions, logging, validation, performance, exception handling)

**Next: Phase 4 - Testing**

We'll implement:
- Test infrastructure with base classes
- Validator unit tests
- Handler integration tests
- Domain entity tests
