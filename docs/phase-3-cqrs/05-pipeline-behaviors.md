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
│   └── UnhandledExceptionBehavior.cs
├── RequestValidationProcessor.cs    <- Already here (validation)
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

| Behavior | Purpose | Applies To | Logs |
|----------|---------|------------|------|
| `UnhandledExceptionBehavior` | Catches & logs all exceptions with request context | All requests | Errors only |
| `LoggingBehavior` | Logs request start/end | All requests | Info + Errors |
| `PerformanceBehavior` | Warns on slow requests (>500ms) | All requests | Warnings only (when slow) |
| `RequestValidationProcessor` | Validates input, throws on failure | All requests | None (throws exception) |
| `TransactionBehavior` | Wraps in DB transaction | Commands only | None |

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
using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Application.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        _logger.LogInformation("Handling {RequestName}", requestName);

        try
        {
            var response = await next();

            _logger.LogInformation("Handled {RequestName}", requestName);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling {RequestName}", requestName);
            throw;
        }
    }
}
```

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
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private readonly Stopwatch _timer;

    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
        _timer = new Stopwatch();
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        _timer.Start();

        var response = await next();

        _timer.Stop();

        var elapsedMilliseconds = _timer.ElapsedMilliseconds;

        if (elapsedMilliseconds > 500) // Threshold for slow requests
        {
            var requestName = typeof(TRequest).Name;

            _logger.LogWarning(
                "Long running request: {RequestName} ({ElapsedMilliseconds}ms)",
                requestName,
                elapsedMilliseconds);
        }

        return response;
    }
}
```

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
    private readonly ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> _logger;

    public UnhandledExceptionBehavior(ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
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

            _logger.LogError(ex,
                "Unhandled exception for request {RequestName}: {@Request}",
                requestName,
                request);

            throw;
        }
    }
}
```

### Validation via RequestValidationProcessor (Already Implemented)

**What it does:** Runs all FluentValidation validators for the request before the handler executes. Throws `ValidationException` if any validation fails.

**Why it's useful:**
- Validates input BEFORE any business logic runs
- Catches bad data early (fail fast)
- Returns user-friendly validation errors
- Validators are automatically discovered and injected

**How it works:**
```
Request arrives
  → RequestPreProcessorBehavior (MediatR built-in)
    → RequestValidationProcessor (your implementation)
      → Runs all IValidator<TRequest> instances
        → Valid? → Continue to handler
        → Invalid? → Throw ValidationException (caught by ExceptionToJsonFilter)
```

**You already have this!** See `RequestValidationProcessor.cs` in BuildingBlocks.Application (from 04-validation.md).

---

### Step 4: Register Behaviors in BuildingBlocks

Update `BuildingBlocks/BuildingBlocks.Application/BuildingBlocksServiceCollectionExtensions.cs`:

```csharp
using System.Reflection;
using BuildingBlocks.Application.Behaviors;
using FluentValidation;
using MediatR;
using MediatR.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Application;

public static class BuildingBlocksServiceCollectionExtensions
{
    private static bool _fluentValidationDefaultsConfigured;

    /// <summary>
    /// Registers a bounded context's MediatR handlers and FluentValidation validators.
    /// Also registers shared pipeline behaviors and configures BuildingBlocks defaults (once).
    /// </summary>
    public static IServiceCollection AddBoundedContext(this IServiceCollection services, Assembly boundedContextAssembly)
    {
        SetFluentValidationDefaults();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(boundedContextAssembly);

            // Pipeline behaviors - Order matters! First registered = outermost in pipeline
            cfg.AddOpenBehavior(typeof(UnhandledExceptionBehavior<,>));
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));

            // Validation pre-processor (runs before handler)
            cfg.AddOpenBehavior(typeof(RequestPreProcessorBehavior<,>));
        });

        services.AddValidatorsFromAssembly(boundedContextAssembly, includeInternalTypes: true);

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
        → RequestPreProcessorBehavior (runs validators via RequestValidationProcessor)
          → Handler (actual logic)
```

### Step 5: Simplify Bounded Context Registration

Each bounded context now just calls `AddBoundedContext()` - no need to register behaviors:

Location: `Core/Scheduling/Scheduling.Application/ServiceCollectionExtensions.cs`

```csharp
using BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingApplication(this IServiceCollection services)
    {
        // Registers MediatR handlers, validators, and all pipeline behaviors
        services.AddBoundedContext(typeof(ServiceCollectionExtensions).Assembly);

        // Add any Scheduling-specific services here

        return services;
    }
}
```

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
    private readonly IUnitOfWork _unitOfWork;

    public TransactionBehavior(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only apply to commands (not queries)
        if (!typeof(TRequest).Name.EndsWith("Command"))
            return await next();

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var response = await next();
            await _unitOfWork.CloseTransactionAsync(cancellationToken: cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            await _unitOfWork.CloseTransactionAsync(ex, cancellationToken);
            throw;
        }
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

- [ ] `LoggingBehavior` created in BuildingBlocks.Application/Behaviors
- [ ] `PerformanceBehavior` created in BuildingBlocks.Application/Behaviors
- [ ] `UnhandledExceptionBehavior` created in BuildingBlocks.Application/Behaviors
- [ ] `TransactionBehavior` created in BuildingBlocks.Application/Behaviors (optional)
- [ ] All behaviors registered in `AddBoundedContext()` in correct order
- [ ] Pipeline behaviors run for all MediatR requests across all bounded contexts
- [ ] Validation failures return proper error responses
- [ ] Slow requests are logged as warnings
- [ ] BuildingBlocks.Application.csproj has Microsoft.Extensions.Logging.Abstractions package

---

## Folder Structure After This Step

```
BuildingBlocks/
└── BuildingBlocks.Application/
    ├── Behaviors/                              <- Cross-cutting behaviors
    │   ├── LoggingBehavior.cs
    │   ├── PerformanceBehavior.cs
    │   ├── TransactionBehavior.cs              <- Optional, uses IUnitOfWork
    │   └── UnhandledExceptionBehavior.cs
    ├── Dtos/
    │   └── SuccessOrFailureDto.cs
    ├── Interfaces/
    │   ├── IRepository.cs
    │   └── IUnitOfWork.cs                      <- Has BeginTransactionAsync/CloseTransactionAsync
    ├── Validators/
    │   └── UserValidator.cs
    ├── BuildingBlocksServiceCollectionExtensions.cs  <- Registers all behaviors
    └── RequestValidationProcessor.cs

Core/Scheduling/
└── Scheduling.Application/
    ├── Patients/
    │   ├── Commands/
    │   │   └── ...
    │   ├── Queries/
    │   │   └── ...
    │   └── EventHandlers/
    │       └── PatientCreatedEventHandler.cs
    └── ServiceCollectionExtensions.cs          <- Just calls AddBoundedContext()
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
