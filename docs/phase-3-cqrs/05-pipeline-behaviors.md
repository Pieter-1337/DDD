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

## What You Need To Do

### Step 1: Create LoggingBehavior

Location: `Core/Scheduling/Scheduling.Application/Behaviors/LoggingBehavior.cs`

```csharp
using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Scheduling.Application.Behaviors;

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

Location: `Core/Scheduling/Scheduling.Application/Behaviors/PerformanceBehavior.cs`

```csharp
using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Scheduling.Application.Behaviors;

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

Location: `Core/Scheduling/Scheduling.Application/Behaviors/UnhandledExceptionBehavior.cs`

```csharp
using MediatR;
using Microsoft.Extensions.Logging;

namespace Scheduling.Application.Behaviors;

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

### Step 4: Register All Behaviors

Update `Core/Scheduling/Scheduling.Application/ServiceCollectionExtensions.cs`:

```csharp
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Scheduling.Application.Behaviors;

namespace Scheduling.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingApplication(this IServiceCollection services)
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;

        // Register MediatR with pipeline behaviors
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // Order matters! First registered = outermost in pipeline
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        // Register all validators
        services.AddValidatorsFromAssembly(assembly);

        return services;
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

For commands that need explicit transactions:

Location: `Core/Scheduling/Scheduling.Application/Behaviors/TransactionBehavior.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Scheduling.Infrastructure.Persistence;

namespace Scheduling.Application.Behaviors;

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly SchedulingDbContext _context;

    public TransactionBehavior(SchedulingDbContext context)
    {
        _context = context;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only apply to commands (not queries)
        if (!typeof(TRequest).Name.EndsWith("Command"))
            return await next();

        // Already in a transaction?
        if (_context.Database.CurrentTransaction is not null)
            return await next();

        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken);

            try
            {
                var response = await next();
                await transaction.CommitAsync(cancellationToken);
                return response;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
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

- [ ] `LoggingBehavior` created and logs request start/end
- [ ] `PerformanceBehavior` created and warns for slow requests
- [ ] `UnhandledExceptionBehavior` created and logs exceptions
- [ ] All behaviors registered in correct order
- [ ] Pipeline behaviors run for all MediatR requests
- [ ] Validation failures return proper error responses
- [ ] Slow requests are logged as warnings

---

## Folder Structure After This Step

```
Core/Scheduling/
└── Scheduling.Application/
    ├── Behaviors/
    │   ├── LoggingBehavior.cs
    │   ├── PerformanceBehavior.cs
    │   ├── UnhandledExceptionBehavior.cs
    │   └── ValidationBehavior.cs
    ├── Common/
    │   └── PagedResult.cs
    ├── Exceptions/
    │   ├── PatientNotFoundException.cs
    │   └── ValidationException.cs
    ├── Patients/
    │   ├── Commands/
    │   │   └── ...
    │   ├── Queries/
    │   │   └── ...
    │   └── EventHandlers/
    │       └── PatientCreatedEventHandler.cs
    └── ServiceCollectionExtensions.cs
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
