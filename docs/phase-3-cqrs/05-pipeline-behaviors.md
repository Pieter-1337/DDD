# MediatR Pipeline Behaviors

## What Are Pipeline Behaviors?

Pipeline behaviors are **cross-cutting concerns** that run for every request. They wrap handlers like middleware:

```
Request → Behavior 1 → Behavior 2 → Behavior 3 → Handler → Response
              ↓            ↓            ↓
          Logging    Validation    Performance
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

Pipeline behaviors are **cross-cutting concerns** - they apply identically to all bounded contexts. Therefore, they belong in `BuildingBlocks.Application`, not in individual bounded context projects.

```
BuildingBlocks.Application/          <- Cross-cutting concerns
├── Behaviors/
│   ├── LoggingBehavior.cs
│   ├── PerformanceBehavior.cs
│   ├── TransactionBehavior.cs       <- Uses IUnitOfWork (generic)
│   ├── UnhandledExceptionBehavior.cs
│   └── ValidationBehavior.cs        <- Uses IValidator<T> (generic)
└── BuildingBlocksServiceCollectionExtensions.cs

Scheduling.Application/              <- Bounded context handlers only
├── Patients/
│   ├── Commands/
│   └── Queries/
└── ServiceCollectionExtensions.cs   <- Just calls AddBoundedContext()
```

**Rule of thumb:**
- All pipeline behaviors use abstractions (`IUnitOfWork`, `ILogger`) → `BuildingBlocks.Application`
- Bounded contexts contain only their handlers, validators, and DTOs

---

## What You Need To Do

### Prerequisites

Add the logging abstractions package to `BuildingBlocks.Application.csproj`:

```xml
<ItemGroup>
    <PackageReference Include="FluentValidation" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
    <PackageReference Include="MediatR" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />  <!-- ADD THIS -->
</ItemGroup>
```

This provides `ILogger<T>` without pulling in the full ASP.NET Core dependency. The actual logging implementation (console, Serilog, etc.) is configured in the WebApi project.

---

### Behavior Summary

| Behavior | Purpose | Applies To | Overridable Methods |
|----------|---------|------------|---------------------|
| `UnhandledExceptionBehavior` | Catches & logs all exceptions | All requests | `OnException` |
| `LoggingBehavior` | Logs request start/end | All requests | `OnHandling`, `OnHandled`, `OnError` |
| `PerformanceBehavior` | Warns on slow requests (>500ms) | All requests | `ThresholdMilliseconds`, `OnSlowRequest` |
| `ValidationBehavior` | Validates input, throws on failure | All requests | `ValidateAsync`, `OnValidationFailure` |
| `TransactionBehavior` | Wraps in DB transaction | Commands only | `ShouldApplyTransaction` |

---

### Step 1: Create LoggingBehavior

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

Or on failure:
```
[INF] Handling CreatePatientCommand
[ERR] Error handling CreatePatientCommand - System.InvalidOperationException: ...
```

Location: `BuildingBlocks/BuildingBlocks.Application/Behaviors/LoggingBehavior.cs`

```csharp
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
        => Logger.LogError(ex, "Error handling {RequestName}", requestName);
}
```

**Extensibility:** All methods are `virtual` and `Logger` is `protected`, so web apps can override specific hooks or the entire `Handle` method.

### Step 2: Create PerformanceBehavior

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

Location: `BuildingBlocks/BuildingBlocks.Application/Behaviors/PerformanceBehavior.cs`

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

### Step 3: Create UnhandledExceptionBehavior

**What it does:** Catches any unhandled exception, logs it with full request details, then re-throws it.

**Why it's useful:**
- Ensures ALL exceptions are logged with context (request name + request data)
- Uses structured logging (`{@Request}`) to serialize the entire request object
- Acts as a safety net - even if other error handling fails, this captures it
- Registered first (outermost) so it catches exceptions from all inner behaviors

**Example log output:**
```
[ERR] Unhandled exception for request CreatePatientCommand: {"Patient":{"FirstName":"John","LastName":"Doe",...}}
      System.InvalidOperationException: Patient already exists
         at Scheduling.Application.Patients.Commands.CreatePatientCommandHandler.Handle(...)
```

**Note:** This behavior re-throws the exception after logging. The `ExceptionToJsonFilter` (in WebApplications) then converts it to an HTTP response.

Location: `BuildingBlocks/BuildingBlocks.Application/Behaviors/UnhandledExceptionBehavior.cs`

```csharp
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
        => Logger.LogError(ex,
            "Unhandled exception for request {RequestName}: {@Request}",
            requestName,
            request);
}
```

**Extensibility:** Override `OnException` to customize exception logging (e.g., add correlation IDs, sanitize sensitive data).

### Step 3b: Create ValidationBehavior

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

---

### Step 4: Register Behaviors in BuildingBlocks

The registration is split into two concerns:
1. **`AddBoundedContext()`** - Registers handlers and validators only
2. **`AddDefaultPipelineBehaviors()`** - Registers the default behaviors (web app decides)

Update `BuildingBlocks/BuildingBlocks.Application/BuildingBlocksServiceCollectionExtensions.cs`:

```csharp
using System.Reflection;
using BuildingBlocks.Application.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

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
    /// Registers the default pipeline behaviors. Call this from your web app's Program.cs.
    /// Web apps can skip this and register custom behaviors instead.
    /// </summary>
    public static IServiceCollection AddDefaultPipelineBehaviors(this IServiceCollection services)
    {
        // Order matters! First registered = outermost in pipeline
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

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
  → UnhandledExceptionBehavior (catches all exceptions)
    → LoggingBehavior (logs start/end)
      → PerformanceBehavior (measures time)
        → ValidationBehavior (validates input)
          → Handler (actual logic)
```

### Step 5: Bounded Context Registration

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

### Step 6: Web App Registration

The web application decides which behaviors to use:

**WebApi/Program.cs - Using defaults:**
```csharp
// Register bounded contexts
builder.Services.AddSchedulingApplication();
builder.Services.AddBillingApplication();  // if you have more

// Register default pipeline behaviors
builder.Services.AddDefaultPipelineBehaviors();
```

**AdminApi/Program.cs - Using custom behaviors:**
```csharp
// Register bounded contexts
builder.Services.AddSchedulingApplication();

// Register custom behaviors (instead of defaults)
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AdminLoggingBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<>));
```

### Step 7: Override Behaviors in Web Apps (Optional)

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
| `LoggingBehavior` | `Handle`, `OnHandling`, `OnHandled`, `OnError` |
| `PerformanceBehavior` | `Handle`, `ThresholdMilliseconds`, `OnSlowRequest` |
| `UnhandledExceptionBehavior` | `Handle`, `OnException` |
| `ValidationBehavior` | `Handle`, `ValidateAsync`, `OnValidationFailure` |
| `TransactionBehavior` | `Handle`, `ShouldApplyTransaction` |

---

## Advanced: Conditional Behaviors

### Query-Only Behavior (Skip for Commands)

```csharp
public class QueryCachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only apply to queries (requests that end with "Query")
        if (!typeof(TRequest).Name.EndsWith("Query"))
            return await next();

        // Caching logic here...
        return await next();
    }
}
```

### Using Marker Interfaces

```csharp
// Marker interface
public interface ICachedQuery { }

// Query that should be cached
public record GetPatientByIdQuery(Guid PatientId) : IRequest<PatientDto?>, ICachedQuery;

// Behavior only for cached queries
public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICachedQuery
{
    // Only runs for requests implementing ICachedQuery
}
```

---

## Transaction Behavior (Optional)

**What it does:** Wraps command execution in a database transaction. Commits on success, rolls back on any exception.

**Why it's useful:**
- Ensures atomicity: either all database changes succeed, or none do
- Prevents partial updates when a command makes multiple changes
- Only applies to commands (skips queries for performance)
- Uses `IUnitOfWork` so it works with any bounded context's DbContext

**When to use it:**
- Commands that modify multiple aggregates
- Commands where you need "all or nothing" behavior
- When handlers call `SaveChangesAsync()` multiple times

**When you might NOT need it:**
- If your handlers only modify one aggregate and call `SaveChangesAsync()` once
- EF Core already wraps `SaveChangesAsync()` in an implicit transaction
- For read-only queries (behavior skips these automatically)

**Flow:**
```
Command arrives
  → BeginTransactionAsync()
    → Handler executes (may call SaveChangesAsync multiple times)
      → Success? → CommitAsync()
      → Exception? → RollbackAsync() → Re-throw
```

Location: `BuildingBlocks/BuildingBlocks.Application/Behaviors/TransactionBehavior.cs`

```csharp
using BuildingBlocks.Application.Interfaces;
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
            return await next();

        await UnitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var response = await next();
            await UnitOfWork.CloseTransactionAsync(cancellationToken: cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            await UnitOfWork.CloseTransactionAsync(ex, cancellationToken);
            throw;
        }
    }

    protected virtual bool ShouldApplyTransaction(TRequest request)
        => typeof(TRequest).Name.EndsWith("Command");
}
```

**Extensibility:** Override `ShouldApplyTransaction` to change which requests get wrapped in a transaction (e.g., use a marker interface instead of naming convention).

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
│              UnhandledExceptionBehavior                           │
│    try { next() } catch { log & rethrow }                        │
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
│               PerformanceBehavior                                 │
│    Start timer → next() → Stop timer → Log if slow               │
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
│                      Handler                                      │
│           CreatePatientCommandHandler.Handle()                    │
│                    (actual business logic)                        │
└──────────────────────────────────────────────────────────────────┘
```

---

## Testing Pipeline Behaviors

```csharp
public class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_ValidRequest_CallsNext()
    {
        // Arrange
        var validators = new List<IValidator<CreatePatientCommand>>
        {
            new CreatePatientCommandValidator()
        };
        var behavior = new ValidationBehavior<CreatePatientCommand, Guid>(validators);

        var validCommand = new CreatePatientCommand(
            "John", "Doe", "john@example.com",
            DateTime.UtcNow.AddYears(-30), null);

        var expectedResult = Guid.NewGuid();
        RequestHandlerDelegate<Guid> next = () => Task.FromResult(expectedResult);

        // Act
        var result = await behavior.Handle(validCommand, next, CancellationToken.None);

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task Handle_InvalidRequest_ThrowsValidationException()
    {
        // Arrange
        var validators = new List<IValidator<CreatePatientCommand>>
        {
            new CreatePatientCommandValidator()
        };
        var behavior = new ValidationBehavior<CreatePatientCommand, Guid>(validators);

        var invalidCommand = new CreatePatientCommand(
            "", "", "not-an-email",  // All invalid
            DateTime.UtcNow.AddYears(1), null);

        RequestHandlerDelegate<Guid> next = () => Task.FromResult(Guid.NewGuid());

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(
            () => behavior.Handle(invalidCommand, next, CancellationToken.None));
    }
}
```

---

## Verification Checklist

- [ ] `LoggingBehavior` created with `virtual` methods in BuildingBlocks.Application/Behaviors
- [ ] `PerformanceBehavior` created with `virtual` methods in BuildingBlocks.Application/Behaviors
- [ ] `UnhandledExceptionBehavior` created with `virtual` methods in BuildingBlocks.Application/Behaviors
- [ ] `TransactionBehavior` created with `virtual` methods in BuildingBlocks.Application/Behaviors (optional)
- [ ] `AddBoundedContext()` registers handlers and validators only (no behaviors)
- [ ] `AddDefaultPipelineBehaviors()` registers the default behaviors
- [ ] WebApi calls both `AddSchedulingApplication()` and `AddDefaultPipelineBehaviors()`
- [ ] Validation failures return proper error responses
- [ ] Slow requests are logged as warnings
- [ ] BuildingBlocks.Application.csproj has Microsoft.Extensions.Logging.Abstractions package
- [ ] (Optional) Custom behaviors can inherit and override specific methods

---

## Folder Structure After This Step

```
BuildingBlocks/
└── BuildingBlocks.Application/
    ├── Behaviors/                              <- Base behaviors (virtual methods)
    │   ├── LoggingBehavior.cs                  <- OnHandling, OnHandled, OnError
    │   ├── PerformanceBehavior.cs              <- ThresholdMilliseconds, OnSlowRequest
    │   ├── TransactionBehavior.cs              <- ShouldApplyTransaction (optional)
    │   ├── UnhandledExceptionBehavior.cs       <- OnException
    │   └── ValidationBehavior.cs               <- ValidateAsync, OnValidationFailure
    ├── Dtos/
    │   └── SuccessOrFailureDto.cs
    ├── Interfaces/
    │   ├── IRepository.cs
    │   └── IUnitOfWork.cs
    ├── Validators/
    │   └── UserValidator.cs
    └── BuildingBlocksServiceCollectionExtensions.cs
            ├── AddBoundedContext()             <- Handlers + validators only
            └── AddDefaultPipelineBehaviors()   <- Default behaviors (opt-in)

Core/Scheduling/
└── Scheduling.Application/
    ├── Patients/
    │   ├── Commands/
    │   ├── Queries/
    │   └── EventHandlers/
    └── ServiceCollectionExtensions.cs          <- Calls AddBoundedContext()

WebApi/
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
- **Pipeline Behaviors** - Cross-cutting concerns (logging, performance, exception handling)

**Next: Phase 4 - Event-Driven Architecture**

We'll implement:
- Domain Events vs Integration Events
- RabbitMQ with MassTransit
- Event publishing between bounded contexts
- Saga patterns for distributed transactions
