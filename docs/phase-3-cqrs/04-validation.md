# Command Validation with FluentValidation

## Why Validate Commands?

Commands carry user input. Before passing to handlers, we should validate:

1. **Format validation** - Is the email format correct?
2. **Required fields** - Is the first name provided?
3. **Business preconditions** - Does the patient exist? Is the status valid?

---

## Where Does Validation Live?

**All validation lives in FluentValidation.** The domain focuses on behavior only.

```
+---------------------------------------------------------+
|                  Responsibility Split                    |
+---------------------------------------------------------+
|  FluentValidation  |  ALL validation (input + preconditions) |
|  Domain            |  Behavior and state transitions only    |
+---------------------------------------------------------+
```

**Business preconditions** vs **Business rules**:

```csharp
// FluentValidation - preconditions (CAN we do this?)
RuleFor(x => x.Email)
    .NotEmpty()
    .WithErrorCode(ErrorCode.EmailRequired.Value)
    .WithMessage(ErrorCode.EmailRequired.Message)
    .EmailAddress(EmailValidationMode.AspNetCoreCompatible)
    .WithErrorCode(ErrorCode.InvalidEmail.Value)
    .WithMessage(ErrorCode.InvalidEmail.Message);

RuleFor(x => x.Id)
    .MustAsync(BeAValidPatientAsync)
    .WithErrorCode(ErrorCode.NotFound.Value)
    .WithMessage(ErrorCode.NotFound.Message);

// Domain - rules (HOW do we do this?)
public void Suspend()
{
    if (Status == PatientStatus.Suspended)
        return; // Idempotent
    Status = PatientStatus.Suspended;
}

public void Activate()
{
    if (Status == PatientStatus.Active)
        return; // Idempotent
    Status = PatientStatus.Active;
}
```

---

## What You Need To Do

### Step 1: Add FluentValidation packages

Already in `Directory.Packages.props`:

```xml
<PackageVersion Include="FluentValidation" Version="11.11.0" />
<PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="11.11.0" />
```

Add to `Scheduling.Application.csproj`:

```xml
<ItemGroup>
    <PackageReference Include="FluentValidation" />
</ItemGroup>
```

### Step 2: Create UserValidator Base Class

Location: `BuildingBlocks/BuildingBlocks.Application/UserValidator.cs`

```csharp
using FluentValidation;

namespace BuildingBlocks.Application;

public abstract class UserValidator<T> : AbstractValidator<T>
{
    // Base class for all validators
    // Can be extended for user context validation (roles, permissions)
}
```

**Key points:**
- Base class extending `AbstractValidator<T>`
- Can be extended later for role-based validation
- All command/query validators inherit from this

### Step 3: Create ErrorCode Enumeration

Error codes provide consistent, machine-readable error identifiers. We use the same SmartEnum pattern as domain enumerations.

Location: `BuildingBlocks/BuildingBlocks.Enumerations/ErrorCode.cs`

```csharp
using Ardalis.SmartEnum;

namespace BuildingBlocks.Enumerations;

/// <summary>
/// Base class for custom error codes. Automatically prefixes codes with "ERR_".
/// Inherit from this in bounded contexts to define domain-specific errors.
/// </summary>
public abstract class ErrorCodeBase<TEnum> : SmartEnum<TEnum, string>
    where TEnum : SmartEnum<TEnum, string>
{
    private const string Prefix = "ERR_";

    public string Message { get; }

    protected ErrorCodeBase(string code, string message)
        : base(Prefix + code, Prefix + code)
    {
        Message = message;
    }
}

/// <summary>
/// Base class for custom warning codes. Automatically prefixes codes with "WRN_".
/// Inherit from this in bounded contexts to define domain-specific warnings.
/// </summary>
public abstract class WarningCodeBase<TEnum> : SmartEnum<TEnum, string>
    where TEnum : SmartEnum<TEnum, string>
{
    private const string Prefix = "WRN_";

    public string Message { get; }

    protected WarningCodeBase(string code, string message)
        : base(Prefix + code, Prefix + code)
    {
        Message = message;
    }
}

/// <summary>
/// Common error codes shared across all bounded contexts.
/// </summary>
public sealed class ErrorCode : ErrorCodeBase<ErrorCode>
{
    // General errors
    public static readonly ErrorCode NotFound = new("NOT_FOUND", "The requested resource was not found");
    public static readonly ErrorCode InvalidInput = new("INVALID_INPUT", "The provided input is invalid");
    public static readonly ErrorCode Conflict = new("CONFLICT", "The operation conflicts with the current state");
    public static readonly ErrorCode Unauthorized = new("UNAUTHORIZED", "Authentication is required");
    public static readonly ErrorCode Forbidden = new("FORBIDDEN", "You do not have permission to perform this action");
    public static readonly ErrorCode ValidationFailed = new("VALIDATION_FAILED", "One or more validation errors occurred");

    // Field validation errors
    public static readonly ErrorCode FirstNameRequired = new("FIRSTNAME_REQUIRED", "First name is required");
    public static readonly ErrorCode LastNameRequired = new("LASTNAME_REQUIRED", "Last name is required");
    public static readonly ErrorCode EmailRequired = new("EMAIL_REQUIRED", "Email is required");
    public static readonly ErrorCode InvalidEmail = new("INVALID_EMAIL", "Invalid email address");
    public static readonly ErrorCode DateOfBirthRequired = new("DOB_REQUIRED", "Date of birth is required");
    public static readonly ErrorCode InvalidStatus = new("INVALID_STATUS", "Invalid status");

    private ErrorCode(string code, string message) : base(code, message) { }
}
```

**Key points:**
- `ErrorCodeBase<T>` **automatically prefixes** codes with `"ERR_"` (e.g., `"NOT_FOUND"` becomes `"ERR_NOT_FOUND"`)
- `WarningCodeBase<T>` **automatically prefixes** codes with `"WRN_"` for warning-level validations
- `ErrorCode` contains common codes shared across all contexts
- Each code has a machine-readable `Value` (e.g., `"ERR_NOT_FOUND"`) and human-readable `Message`
- The `ValidationErrorWrapper` filters by prefix to only show custom codes in API responses

### Step 4: Create Validators Inline with Commands

Validators are defined in the same file as their command, using `#region` to organize:

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/CreatePatientCommand.cs`

```csharp
using BuildingBlocks.Application.Dtos;
using BuildingBlocks.Application.Cqrs;
using BuildingBlocks.Application.Validators;
using BuildingBlocks.Enumerations;
using FluentValidation;
using FluentValidation.Validators;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands;

public record CreatePatientCommand(CreatePatientRequest Patient) : Command<CreatePatientCommandResponse>;

public class CreatePatientRequest
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string? PhoneNumber { get; set; }
    public PatientStatus Status { get; set; }
}

public class CreatePatientCommandResponse : SuccessOrFailureDto
{
    public PatientDto PatientDto { get; set; }
}

#region Validators
internal class CreatePatientCommandValidator : UserValidator<CreatePatientCommand>
{
    public CreatePatientCommandValidator(IValidator<CreatePatientRequest> createPatientRequestValidator)
    {
        RuleFor(c => c.Patient).Cascade(CascadeMode.Stop)
            .NotNull()
            .SetValidator(createPatientRequestValidator);
    }
}

internal class CreatePatientRequestValidator : AbstractValidator<CreatePatientRequest>
{
    public CreatePatientRequestValidator()
    {
        RuleFor(p => p.FirstName)
            .NotEmpty()
            .WithErrorCode(ErrorCode.FirstNameRequired.Value)
            .WithMessage(ErrorCode.FirstNameRequired.Message);

        RuleFor(p => p.LastName)
            .NotEmpty()
            .WithErrorCode(ErrorCode.LastNameRequired.Value)
            .WithMessage(ErrorCode.LastNameRequired.Message);

        RuleFor(p => p.Email)
            .NotEmpty()
            .WithErrorCode(ErrorCode.EmailRequired.Value)
            .WithMessage(ErrorCode.EmailRequired.Message)
            .EmailAddress(EmailValidationMode.AspNetCoreCompatible)
            .WithErrorCode(ErrorCode.InvalidEmail.Value)
            .WithMessage(ErrorCode.InvalidEmail.Message);

        RuleFor(p => p.DateOfBirth)
            .NotEmpty()
            .WithErrorCode(ErrorCode.DateOfBirthRequired.Value)
            .WithMessage(ErrorCode.DateOfBirthRequired.Message);
    }
}
#endregion Validators
```

**Key points:**
- Validators are `internal` classes in the same file
- Use `#region Validators` to organize
- Use `.WithErrorCode(ErrorCode.X.Value).WithMessage(ErrorCode.X.Message)` for consistent error codes
- Use `EmailAddress(EmailValidationMode.AspNetCoreCompatible)` for email validation (not the obsolete regex mode)
- Nested DTOs get their own validator, composed with `SetValidator()`

### Step 5: Entity Existence Validation with ExistsAsync

For commands that operate on existing entities, validate existence using the repository:

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/SuspendPatientCommand.cs`

```csharp
using BuildingBlocks.Application.Dtos;
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Cqrs;
using BuildingBlocks.Application.Validators;
using BuildingBlocks.Enumerations;
using FluentValidation;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands;

public record SuspendPatientCommand : Command<SuspendPatientCommandResponse>
{
    public Guid Id { get; init; }
}

public class SuspendPatientCommandResponse : SuccessOrFailureDto { }

#region Validators
internal class SuspendPatientCommandValidator : UserValidator<SuspendPatientCommand>
{
    private readonly IUnitOfWork _uow;

    public SuspendPatientCommandValidator(IUnitOfWork uow)
    {
        _uow = uow;

        RuleFor(c => c.Id)
            .MustAsync(BeAValidPatientAsync)
            .WithErrorCode(ErrorCode.NotFound.Value)
            .WithMessage(ErrorCode.NotFound.Message);
    }

    private async Task<bool> BeAValidPatientAsync(Guid id, CancellationToken ct)
    {
        return await _uow.RepositoryFor<Patient>().ExistsAsync(id, ct);
    }
}
#endregion Validators
```

**Key points:**
- Inject `IUnitOfWork` into the validator
- Use `MustAsync` for async validation rules
- Use `.WithErrorCode(ErrorCode.NotFound.Value).WithMessage(ErrorCode.NotFound.Message)` for entity not found errors
- Call `ExistsAsync` on the repository (not `GetByIdAsync`)
- No need for `NotEmpty()` check - `ExistsAsync` returns false for empty Guid

### Step 5a: Create ActivatePatientCommandValidator

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/ActivatePatientCommand.cs`

```csharp
using BuildingBlocks.Application.Dtos;
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Cqrs;
using BuildingBlocks.Application.Validators;
using BuildingBlocks.Enumerations;
using FluentValidation;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands;

public record ActivatePatientCommand : Command<ActivatePatientCommandResponse>
{
    public Guid Id { get; init; }
}

public class ActivatePatientCommandResponse : SuccessOrFailureDto { }

#region Validators
internal class ActivatePatientCommandValidator : UserValidator<ActivatePatientCommand>
{
    private readonly IUnitOfWork _uow;

    public ActivatePatientCommandValidator(IUnitOfWork uow)
    {
        _uow = uow;

        RuleFor(c => c.Id)
            .MustAsync(BeAValidPatientAsync)
            .WithErrorCode(ErrorCode.NotFound.Value)
            .WithMessage(ErrorCode.NotFound.Message);
    }

    private async Task<bool> BeAValidPatientAsync(Guid id, CancellationToken ct)
    {
        return await _uow.RepositoryFor<Patient>().ExistsAsync(id, ct);
    }
}
#endregion Validators
```

**Key points:**
- Same pattern as `SuspendPatientCommandValidator`
- Validates patient existence before attempting to activate
- Uses `.WithErrorCode(ErrorCode.NotFound.Value).WithMessage(ErrorCode.NotFound.Message)` for consistency

### Step 5b: Create DeletePatientCommandValidator

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/DeletePatientCommand.cs`

```csharp
using BuildingBlocks.Application.Dtos;
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Cqrs;
using BuildingBlocks.Application.Validators;
using BuildingBlocks.Enumerations;
using FluentValidation;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands;

public record DeletePatientCommand : Command<DeletePatientCommandResponse>
{
    public Guid Id { get; init; }
}

public class DeletePatientCommandResponse : SuccessOrFailureDto { }

#region Validators
internal class DeletePatientCommandValidator : UserValidator<DeletePatientCommand>
{
    private readonly IUnitOfWork _uow;

    public DeletePatientCommandValidator(IUnitOfWork uow)
    {
        _uow = uow;

        RuleFor(c => c.Id)
            .MustAsync(BeAValidPatientAsync)
            .WithErrorCode(ErrorCode.NotFound.Value)
            .WithMessage(ErrorCode.NotFound.Message);
    }

    private async Task<bool> BeAValidPatientAsync(Guid id, CancellationToken ct)
    {
        return await _uow.RepositoryFor<Patient>().ExistsAsync(id, ct);
    }
}
#endregion Validators
```

**Key points:**
- Same pattern as `SuspendPatientCommandValidator` and `ActivatePatientCommandValidator`
- Validates patient existence before attempting to delete
- Uses `.WithErrorCode(ErrorCode.NotFound.Value).WithMessage(ErrorCode.NotFound.Message)` for consistency

### Step 6: Query Validation

Queries can also have validators for input validation:

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/GetPatientQuery.cs`

```csharp
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Cqrs;
using BuildingBlocks.Application.Validators;
using BuildingBlocks.Enumerations;
using FluentValidation;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries;

public record GetPatientQuery : Query<PatientDto?>
{
    public Guid Id { get; init; }
}

#region Validators
internal class GetPatientQueryValidator : UserValidator<GetPatientQuery>
{
    private readonly IUnitOfWork _uow;

    public GetPatientQueryValidator(IUnitOfWork uow)
    {
        _uow = uow;

        RuleFor(q => q.Id)
            .MustAsync(BeAValidPatientAsync)
            .WithErrorCode(ErrorCode.NotFound.Value)
            .WithMessage(ErrorCode.NotFound.Message);
    }

    private async Task<bool> BeAValidPatientAsync(Guid id, CancellationToken ct)
    {
        return await _uow.RepositoryFor<Patient>().ExistsAsync(id, ct);
    }
}
#endregion Validators
```

### Step 7: SmartEnum Validation

For SmartEnum parameters in DTOs, use `string` type and validate with `.Must(SmartEnum.TryFromName)`. This gives you full control over validation errors instead of relying on JSON deserialization exceptions.

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/GetAllPatientsQuery.cs`

```csharp
using BuildingBlocks.Application.Cqrs;
using BuildingBlocks.Application.Validators;
using BuildingBlocks.Enumerations;
using FluentValidation;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries;

public record GetAllPatientsQuery : Query<IEnumerable<PatientDto>>
{
    public string Status { get; init; }  // String
}

#region Validators
internal class GetAllPatientsQueryValidator : UserValidator<GetAllPatientsQuery>
{
    public GetAllPatientsQueryValidator()
    {
        RuleFor(q => q.Status)
            .Must(PatientStatus.IsInEnum)
            .WithErrorCode(ErrorCode.InvalidStatus.Value)
            .WithMessage(ErrorCode.InvalidStatus.Message);
    }
}
#endregion Validators
```

**In the handler**, convert the string to SmartEnum:

```csharp
public async Task<IEnumerable<PatientDto>> Handle(GetAllPatientsQuery query, CancellationToken ct)
{
    var status = PatientStatus.FromName(query.Status);  // Safe - already validated
    return await _uow.RepositoryFor<Patient>().GetAllAsDtosAsync<PatientDto>(p => p.Status == status);
}
```

**Why string in DTOs instead of SmartEnum directly?**
- Invalid SmartEnum values throw `SmartEnumNotFoundException` during JSON deserialization
- This results in a generic 500 error, not a proper validation response
- Using `string` + `IsInEnum` gives you a proper 400 response with your custom error code
- `SmartEnumBase<T>` provides `IsInEnum` for clean validation syntax

**In tests**, use `PatientStatus.X.Name` to avoid magic strings:
```csharp
var query = new GetAllPatientsQuery { Status = PatientStatus.Active.Name };
```

### Step 8: Register Validators in DI

The `AddBoundedContext` extension method in `BuildingBlocks.Application` handles the common registration for all bounded contexts:

Location: `BuildingBlocks/BuildingBlocks.Application/BuildingBlocksServiceCollectionExtensions.cs`

```csharp
using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Application;

public static class BuildingBlocksServiceCollectionExtensions
{
    private static bool _fluentValidationDefaultsConfigured;

    /// <summary>
    /// Registers a bounded context's MediatR handlers and FluentValidation validators.
    /// Add additional shared config for context services here.
    /// Also configures shared BuildingBlocks defaults (once).
    /// </summary>
    public static IServiceCollection AddBoundedContext(this IServiceCollection services, Assembly boundedContextAssembly)
    {
        SetFluentValidationDefaults();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(boundedContextAssembly));
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

**Key points:**
- **`includeInternalTypes: true`** - Registers internal validators (our validators are internal)
- **`SetFluentValidationDefaults()`** - Configures FluentValidation once across all bounded contexts
- **`DisplayNameResolver`** - Keeps property names as-is (e.g., `FirstName` instead of `"First Name"`) for cleaner test assertions with `nameof()`

Each bounded context calls this shared method:

Location: `Core/Scheduling/Scheduling.Application/ServiceCollectionExtensions.cs`

```csharp
using BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingApplication(this IServiceCollection services)
    {
        services.AddBoundedContext(typeof(ServiceCollectionExtensions).Assembly);

        // Add any Scheduling-specific services here

        return services;
    }
}
```

---

## Email Validation Modes

FluentValidation provides two email validation modes:

```csharp
// RECOMMENDED - Uses the same validation as ASP.NET Core
RuleFor(p => p.Email)
    .EmailAddress(EmailValidationMode.AspNetCoreCompatible)
    .WithMessage("Invalid email address");

// OBSOLETE - Uses regex (marked obsolete)
RuleFor(p => p.Email)
    .EmailAddress()  // Defaults to Net4xRegex, which is obsolete
    .WithMessage("Invalid email address");
```

Always use `EmailValidationMode.AspNetCoreCompatible` for email validation.

---

## DateTime Validation Notes

`DateTime` in C# is a struct, so it's always a valid date by definition. Invalid JSON payloads are handled by the model binder before validation runs:

- **Invalid date format in JSON**: Returns 400 Bad Request with model binding error
- **Valid date format in JSON**: Date is parsed, then FluentValidation runs

For date validation, focus on business rules:

```csharp
RuleFor(p => p.DateOfBirth)
    .NotEmpty()  // Catches default(DateTime)
    .WithMessage("Date of birth cannot be empty");

// Optional: Business rule validation
RuleFor(p => p.DateOfBirth)
    .LessThan(DateTime.UtcNow)
    .WithMessage("Date of birth must be in the past");
```

---

## Validation Guidelines

### 1. Keep Validators Internal

```csharp
// GOOD - internal, in same file as command
internal class CreatePatientCommandValidator : UserValidator<CreatePatientCommand>

// BAD - public, separate file
public class CreatePatientCommandValidator : AbstractValidator<CreatePatientCommand>
```

### 2. Use UserValidator Base Class

```csharp
// GOOD - extends UserValidator
internal class SuspendPatientCommandValidator : UserValidator<SuspendPatientCommand>

// OK for nested DTOs - extends AbstractValidator
internal class CreatePatientRequestValidator : AbstractValidator<CreatePatientRequest>
```

### 3. ExistsAsync for Entity Validation

```csharp
// GOOD - uses ExistsAsync (efficient, returns bool)
private async Task<bool> BeAValidPatientAsync(Guid id, CancellationToken ct)
{
    return await _uow.RepositoryFor<Patient>().ExistsAsync(id, ct);
}

// BAD - uses GetByIdAsync (loads entire entity)
private async Task<bool> BeAValidPatientAsync(Guid id, CancellationToken ct)
{
    return await _uow.RepositoryFor<Patient>().GetByIdAsync(id, ct) != null;
}
```

### 4. Custom Error Messages

```csharp
// GOOD - user-friendly messages
RuleFor(x => x.Email)
    .NotEmpty().WithMessage("Email cannot be empty")
    .EmailAddress(EmailValidationMode.AspNetCoreCompatible)
        .WithMessage("Invalid email address");

// BAD - default messages
RuleFor(x => x.Email)
    .NotEmpty()  // Default: "'Email' must not be empty"
    .EmailAddress();
```

---

## Testing Validators

Integration tests validate the full pipeline including validators:

```csharp
[TestClass]
public class CreatePatientCommandHandlerTests : SchedulingTestBase
{
    [TestMethod]
    public async Task Handle_Should_CreatePatient_ForValidRequest()
    {
        // Arrange
        var request = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "John")
            .With(p => p.LastName = "Doe")
            .With(p => p.Email = "john.doe@example.com")
            .With(p => p.DateOfBirth = new DateTime(1990, 1, 15))
            .Build();

        var command = new CreatePatientCommand(request);

        // Act
        var response = await GetMediator().Send(command);

        // Assert
        response.ShouldNotBeNull();
        response.Success.ShouldBeTrue();
    }
}
```

For unit testing validators directly:

```csharp
[TestMethod]
public async Task Validate_InvalidEmail_ReturnsError()
{
    // Arrange
    var validator = ValidatorFor<CreatePatientCommand>();
    var request = Builder<CreatePatientRequest>.CreateNew()
        .With(p => p.Email = "not-an-email")
        .Build();
    var command = new CreatePatientCommand(request);

    // Act
    var result = await validator.ValidateAsync(command);

    // Assert
    result.IsValid.ShouldBeFalse();
    result.Errors.ShouldContain(e => e.PropertyName.Contains("Email"));
}
```

---

## Optional: Validation Pipeline Integration

The validators above are registered but not automatically invoked by MediatR. To automatically validate all requests before they reach handlers, implement a pipeline behavior that runs validation.

### Step 9: Create ValidationBehavior (Optional)

This behavior runs before each MediatR request handler, executing all registered validators.

**Note:** This is covered in detail in [05-pipeline-behaviors.md](./05-pipeline-behaviors.md). The behavior lives in `BuildingBlocks.Application/Behaviors/` alongside other cross-cutting behaviors.

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

**Key points:**
- Uses `IPipelineBehavior<TRequest, TResponse>` (consistent with other behaviors)
- Uses FluentValidation's built-in `ValidationException` (no custom exception needed)
- Collects failures from all validators before throwing
- `virtual` methods allow web apps to override behavior (see 05-pipeline-behaviors.md)

### Step 10: Create ExceptionToJsonFilter (Optional)

This MVC filter catches exceptions and transforms them into structured JSON responses. It implements both `IExceptionFilter` and `IActionFilter`:

- **IExceptionFilter**: Catches exceptions and formats error responses
- **IActionFilter**: Captures request arguments before execution for logging when errors occur

Location: `BuildingBlocks/BuildingBlocks.WebApplications/ExceptionToJsonFilter.cs`

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BuildingBlocks.WebApplications;

public class ExceptionToJsonFilter : IExceptionFilter, IActionFilter
{
    private readonly ILogger<ExceptionToJsonFilter> _logger;
    private const string ActionArgumentsKey = "ActionArgumentsJson";

    public ExceptionToJsonFilter(ILogger<ExceptionToJsonFilter> logger)
    {
        _logger = logger;
    }

    // IActionFilter - capture arguments before action executes
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionArguments.Count == 0)
            return;

        // Store serialized arguments for potential error logging
        context.HttpContext.Items[ActionArgumentsKey] =
            System.Text.Json.JsonSerializer.Serialize(context.ActionArguments);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    // IExceptionFilter - handle exceptions
    public void OnException(ExceptionContext context)
    {
        if (context.ExceptionHandled)
            return;

        switch (context.Exception)
        {
            case ValidationException validationEx:
                HandleValidationException(context, validationEx);
                break;
            default:
                HandleUnexpectedException(context);
                break;
        }

        context.ExceptionHandled = true;
    }

    private void HandleValidationException(ExceptionContext context, ValidationException exception)
    {
        var response = new ValidationErrorWrapper(exception);

        context.Result = new JsonResult(response)
        {
            StatusCode = response.HttpStatusCode
        };
    }

    private void HandleUnexpectedException(ExceptionContext context)
    {
        // Log with request details including the captured arguments
        _logger.LogError(context.Exception,
            "Unhandled exception in {Method} {Path} - Arguments: {Arguments}",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.GetDisplayUrl(),
            context.HttpContext.Items[ActionArgumentsKey]);

        context.Result = new JsonResult(new
        {
            Title = "An unexpected error occurred",
            Status = StatusCodes.Status500InternalServerError
        })
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
    }
}
```

### Step 11: Create ValidationErrorWrapper (Optional)

This class transforms FluentValidation failures into a structured API response with separate errors and warnings.

Location: `BuildingBlocks/BuildingBlocks.WebApplications/ValidationErrorWrapper.cs`

```csharp
using FluentValidation;
using System.Text.Json.Serialization;

namespace BuildingBlocks.WebApplications;

public class ValidationErrorWrapper
{
    public List<ValidationItem> Errors { get; init; } = [];
    public List<ValidationItem> Warnings { get; init; } = [];

    [JsonIgnore]
    public int HttpStatusCode { get; private set; } = StatusCodes.Status400BadRequest;

    public ValidationErrorWrapper(ValidationException exception)
    {
        ProcessValidationFailures(exception);
    }

    private void ProcessValidationFailures(ValidationException exception)
    {
        // Separate by severity - warnings first, then errors
        var warnings = exception.Errors
            .Where(e => e.Severity == Severity.Warning)
            .ToList();

        var errors = exception.Errors
            .Where(e => e.Severity == Severity.Error)
            .ToList();

        // Process warnings
        foreach (var warning in warnings)
        {
            Warnings.Add(new ValidationItem
            {
                Code = ExtractCustomCode(warning.ErrorCode, "WRN_"),
                Message = warning.ErrorMessage
            });
        }

        // Process errors
        foreach (var error in errors)
        {
            Errors.Add(new ValidationItem
            {
                Code = ExtractCustomCode(error.ErrorCode, "ERR_"),
                Message = error.ErrorMessage
            });
        }

        // Allow single validation failure to override HTTP status code
        TrySetCustomHttpStatusCode(exception);
    }

    private static string? ExtractCustomCode(string? errorCode, string prefix)
    {
        // Only include codes that start with the expected prefix
        if (string.IsNullOrEmpty(errorCode))
            return null;

        return errorCode.StartsWith(prefix) ? errorCode : null;
    }

    private void TrySetCustomHttpStatusCode(ValidationException exception)
    {
        // If single error with numeric code between 100-599, use as HTTP status
        if (exception.Errors.Count() != 1)
            return;

        var singleError = exception.Errors.Single();

        if (int.TryParse(singleError.ErrorCode, out var httpCode) &&
            httpCode >= 100 && httpCode < 600)
        {
            HttpStatusCode = httpCode;
        }
    }

    public class ValidationItem
    {
        public string? Code { get; init; }
        public required string Message { get; init; }
    }
}
```

**Key features:**
- Separates errors and warnings by `Severity`
- Custom error codes use `ERR_` prefix (e.g., `ERR_DUPLICATE_EMAIL`)
- Custom warning codes use `WRN_` prefix (e.g., `WRN_FIELD_DEPRECATED`)
- Supports custom HTTP status code via error code (e.g., `"403"` for Forbidden)
- Default status is 400 Bad Request

### Step 12: Register Filter and Behaviors

Update `WebApplications/Scheduling.WebApi/Program.cs`:

```csharp
using BuildingBlocks.Application;
using BuildingBlocks.WebApplications.Filters;
using BuildingBlocks.WebApplications.Json;

// Register the exception filter globally
builder.Services.AddControllers(options =>
{
    options.SuppressAsyncSuffixInActionNames = false;
    options.Filters.Add<ExceptionToJsonFilter>();
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new SmartEnumJsonConverterFactory());
});

// ... other registrations ...

// Register bounded contexts (handlers + validators)
builder.Services.AddSchedulingApplication();

// Register default pipeline behaviors (includes ValidationBehavior)
builder.Services.AddDefaultPipelineBehaviors();
```

**Important:**
- `ExceptionToJsonFilter` converts `ValidationException` into proper 400 responses. Without it, validation failures return 500 errors.
- `AddDefaultPipelineBehaviors()` registers `ValidationBehavior` which throws `ValidationException` when validation fails.
- Scheduling.WebApi needs to reference `BuildingBlocks.WebApplications` for the filter.

### Step 13: Using Custom Error Codes and Severity

Example validator with custom codes and warnings:

```csharp
internal class CreatePatientCommandValidator : UserValidator<CreatePatientCommand>
{
    public CreatePatientCommandValidator(IUnitOfWork uow)
    {
        RuleFor(c => c.Patient.Email)
            .MustAsync(async (email, ct) => !await uow.RepositoryFor<Patient>()
                .AnyAsync(p => p.Email == email, ct))
            .WithMessage("Email address is already registered")
            .WithErrorCode("ERR_DUPLICATE_EMAIL");

        // Warning example - doesn't block but informs
        RuleFor(c => c.Patient.PhoneNumber)
            .Must(phone => phone?.StartsWith("+") ?? true)
            .WithMessage("Phone number should include country code")
            .WithSeverity(Severity.Warning)
            .WithErrorCode("WRN_PHONE_FORMAT");
    }
}
```

### Example API Response

**Validation failure (400 Bad Request):**
```json
{
    "errors": [
        {
            "code": "ERR_DUPLICATE_EMAIL",
            "message": "Email address is already registered"
        }
    ],
    "warnings": [
        {
            "code": "WRN_PHONE_FORMAT",
            "message": "Phone number should include country code"
        }
    ]
}
```

**Custom status code (403 Forbidden):**
```csharp
RuleFor(c => c)
    .Must(_ => userContext.HasPermission("patients.create"))
    .WithMessage("You don't have permission to create patients")
    .WithErrorCode("403");  // Sets HTTP status to 403
```

---

## Verification Checklist

- [x] FluentValidation package added to Application project
- [x] `UserValidator<T>` base class created in BuildingBlocks.Application
- [x] `ErrorCode` and `ErrorCodeBase<T>` created in BuildingBlocks.Enumerations
- [x] `CreatePatientCommandValidator` uses `.WithErrorCode().WithMessage()` with ErrorCode
- [x] `SuspendPatientCommandValidator` uses `.WithErrorCode(ErrorCode.NotFound.Value)`
- [x] `ActivatePatientCommandValidator` uses `.WithErrorCode(ErrorCode.NotFound.Value)`
- [x] `DeletePatientCommandValidator` uses `.WithErrorCode(ErrorCode.NotFound.Value)`
- [x] `GetPatientQueryValidator` uses `.WithErrorCode(ErrorCode.NotFound.Value)`
- [x] `GetAllPatientsQueryValidator` uses `.NotNull()` for SmartEnum validation
- [x] Validators registered in DI with `AddValidatorsFromAssembly`
- [x] Email validation uses `EmailValidationMode.AspNetCoreCompatible`
- [ ] `ValidationBehavior` in BuildingBlocks.Application/Behaviors (see 05-pipeline-behaviors.md)
- [ ] `ExceptionToJsonFilter` registered in Scheduling.WebApi (required for proper validation error responses)
- [ ] `ValidationErrorWrapper` in BuildingBlocks.WebApplications (used by ExceptionToJsonFilter)

---

## Folder Structure After This Step

```
Core/Scheduling/
+-- Scheduling.Application/
    +-- Patients/
    |   +-- Commands/
    |   |   +-- CreatePatientCommand.cs
    |   |   +-- CreatePatientCommandHandler.cs
    |   |   +-- SuspendPatientCommand.cs
    |   |   +-- SuspendPatientCommandHandler.cs
    |   |   +-- ActivatePatientCommand.cs
    |   |   +-- ActivatePatientCommandHandler.cs
    |   |   +-- DeletePatientCommand.cs
    |   |   +-- DeletePatientCommandHandler.cs
    |   +-- Queries/
    |   |   +-- GetPatientQuery.cs
    |   |   +-- GetPatientQueryHandler.cs
    |   |   +-- GetAllPatientsQuery.cs
    |   |   +-- GetAllPatientsQueryHandler.cs
    |   +-- Dtos/
    |   |   +-- PatientDto.cs
    |   +-- EventHandlers/
    |       +-- PatientCreatedEventHandler.cs
    +-- ServiceCollectionExtensions.cs
BuildingBlocks/
+-- BuildingBlocks.Enumerations/
|   +-- ErrorCode.cs                                  <- ErrorCodeBase<T> + common ErrorCode
+-- BuildingBlocks.Application/
|   +-- Behaviors/                                    <- See 05-pipeline-behaviors.md
|   |   +-- LoggingBehavior.cs
|   |   +-- PerformanceBehavior.cs
|   |   +-- UnhandledExceptionBehavior.cs
|   |   +-- ValidationBehavior.cs                     <- Runs validators before handler
|   +-- Validators/
|   |   +-- UserValidator.cs                          <- Base validator class
|   +-- Interfaces/
|   |   +-- IUnitOfWork.cs
|   |   +-- IRepository.cs
|   +-- Dtos/
|   |   +-- SuccessOrFailureDto.cs
|   +-- BuildingBlocksServiceCollectionExtensions.cs  <- AddBoundedContext + AddDefaultPipelineBehaviors
+-- BuildingBlocks.WebApplications/
    +-- Filters/
    |   +-- ExceptionToJsonFilter.cs                  <- Optional
    |   +-- ValidationErrorWrapper.cs                 <- Optional
    +-- Json/
        +-- SmartEnumJsonConverterFactory.cs          <- Generic SmartEnum JSON serialization
```

---

-> Next: [05-pipeline-behaviors.md](./05-pipeline-behaviors.md) - Pipeline behaviors for logging, performance, validation pipeline
