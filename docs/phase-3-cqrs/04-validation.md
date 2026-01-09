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
RuleFor(x => x.Email).NotEmpty().EmailAddress(EmailValidationMode.AspNetCoreCompatible);
RuleFor(x => x.Id).MustAsync(BeAValidPatientAsync).WithMessage("Patient not found");

// Domain - rules (HOW do we do this?)
public void Suspend()
{
    if (Status == PatientStatus.Suspended)
        return; // Idempotent
    Status = PatientStatus.Suspended;
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

### Step 3: Create Validators Inline with Commands

Validators are defined in the same file as their command, using `#region` to organize:

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/CreatePatientCommand.cs`

```csharp
using BuildingBlocks.Application;
using FluentValidation;
using FluentValidation.Validators;
using MediatR;
using Scheduling.Application.Patients.Dtos;

namespace Scheduling.Application.Patients.Commands;

public record CreatePatientCommand(CreatePatientRequest Patient) : IRequest<CreatePatientCommandResponse>;

public class CreatePatientRequest
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string? PhoneNumber { get; set; }
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
            .WithMessage("FirstName cannot be empty");

        RuleFor(p => p.LastName)
            .NotEmpty()
            .WithMessage("LastName cannot be empty");

        RuleFor(p => p.Email)
            .NotEmpty()
            .WithMessage("Email cannot be empty")
            .EmailAddress(EmailValidationMode.AspNetCoreCompatible)
            .WithMessage("Invalid email address");

        RuleFor(p => p.DateOfBirth)
            .NotEmpty()
            .WithMessage("Date of birth cannot be empty");
    }
}
#endregion Validators
```

**Key points:**
- Validators are `internal` classes in the same file
- Use `#region Validators` to organize
- Use `EmailAddress(EmailValidationMode.AspNetCoreCompatible)` for email validation (not the obsolete regex mode)
- Nested DTOs get their own validator, composed with `SetValidator()`

### Step 4: Entity Existence Validation with ExistsAsync

For commands that operate on existing entities, validate existence using the repository:

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/SuspendPatientCommand.cs`

```csharp
using BuildingBlocks.Application;
using FluentValidation;
using MediatR;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands;

public class SuspendPatientCommand : IRequest<SuspendPatientCommandResponse>
{
    public Guid Id { get; set; }
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
            .WithMessage("Patient not found");
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
- Call `ExistsAsync` on the repository (not `GetByIdAsync`)
- No need for `NotEmpty()` check - `ExistsAsync` returns false for empty Guid

### Step 5: Query Validation

Queries can also have validators for input validation:

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/GetPatientQuery.cs`

```csharp
using BuildingBlocks.Application;
using FluentValidation;
using MediatR;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries;

public class GetPatientQuery : IRequest<PatientDto?>
{
    public Guid Id { get; set; }
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
            .WithMessage("Patient not found");
    }

    private async Task<bool> BeAValidPatientAsync(Guid id, CancellationToken ct)
    {
        return await _uow.RepositoryFor<Patient>().ExistsAsync(id, ct);
    }
}
#endregion Validators
```

### Step 6: Enum Validation

For enum parameters, use `IsInEnum()`:

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/GetAllPatientsQuery.cs`

```csharp
using BuildingBlocks.Application;
using FluentValidation;
using MediatR;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries;

public class GetAllPatientsQuery : IRequest<IEnumerable<PatientDto>>
{
    public PatientStatus Status { get; set; }
}

#region Validators
internal class GetAllPatientsQueryValidator : UserValidator<GetAllPatientsQuery>
{
    public GetAllPatientsQueryValidator()
    {
        RuleFor(q => q.Status)
            .IsInEnum()
            .WithMessage("Invalid patient status");
    }
}
#endregion Validators
```

### Step 7: Register Validators in DI

Update `Core/Scheduling/Scheduling.Application/ServiceCollectionExtensions.cs`:

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingApplication(this IServiceCollection services)
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;

        // Register MediatR
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

        // Register all validators from this assembly
        services.AddValidatorsFromAssembly(assembly);

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
public class CreatePatientCommandHandlerTests : TestBase
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

The validators above are registered but not automatically invoked by MediatR. To automatically validate all requests before they reach handlers, implement a pre-processor that runs validation.

### Step 8: Create RequestValidationProcessor (Optional)

This pre-processor runs before each MediatR request handler, executing all registered validators.

Location: `Core/Scheduling/Scheduling.Application/Pipeline/RequestValidationProcessor.cs`

```csharp
using FluentValidation;
using FluentValidation.Results;
using MediatR.Pipeline;

namespace Scheduling.Application.Pipeline;

public class RequestValidationProcessor<TRequest> : IRequestPreProcessor<TRequest>
    where TRequest : notnull
{
    private readonly IValidator<TRequest>[] _validators;

    public RequestValidationProcessor(IValidator<TRequest>[] validators)
    {
        _validators = validators;
    }

    public async Task Process(TRequest request, CancellationToken cancellationToken)
    {
        if (_validators.Length == 0)
            return;

        var validationContext = new ValidationContext<TRequest>(request);
        var validationFailures = new List<ValidationFailure>();

        foreach (var validator in _validators)
        {
            var validationResult = await validator.ValidateAsync(validationContext, cancellationToken);

            if (!validationResult.IsValid)
            {
                validationFailures.AddRange(validationResult.Errors.Where(f => f is not null));
            }
        }

        if (validationFailures.Count > 0)
        {
            throw new ValidationException(validationFailures);
        }
    }
}
```

**Key points:**
- Uses `IRequestPreProcessor<TRequest>` (runs before handler)
- Uses FluentValidation's built-in `ValidationException` (no custom exception needed)
- Collects failures from all validators before throwing
- Early exit if no validators registered

### Step 9: Create ExceptionToJsonFilter (Optional)

This MVC filter catches exceptions and transforms them into structured JSON responses. It implements both `IExceptionFilter` and `IActionFilter`:

- **IExceptionFilter**: Catches exceptions and formats error responses
- **IActionFilter**: Captures request arguments before execution for logging when errors occur

Location: `WebApi/Filters/ExceptionToJsonFilter.cs`

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WebApi.Filters;

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

### Step 10: Create ValidationErrorWrapper (Optional)

This class transforms FluentValidation failures into a structured API response with separate errors and warnings.

Location: `WebApi/Filters/ValidationErrorWrapper.cs`

```csharp
using FluentValidation;
using System.Text.Json.Serialization;

namespace WebApi.Filters;

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

### Step 11: Register Filter and Pre-Processor (Optional)

Update `WebApi/Program.cs`:

```csharp
using WebApi.Filters;

// Register the exception filter globally
builder.Services.AddControllers(options =>
{
    options.SuppressAsyncSuffixInActionNames = false;
    options.Filters.Add<ExceptionToJsonFilter>();
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
```

Update `Core/Scheduling/Scheduling.Application/ServiceCollectionExtensions.cs`:

```csharp
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingApplication(this IServiceCollection services)
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;

        // Register MediatR with pre/post processor behaviors
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(RequestPreProcessorBehavior<,>));
        });

        // Register all validators from this assembly
        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}
```

### Step 12: Using Custom Error Codes and Severity

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
- [x] `CreatePatientCommandValidator` and `CreatePatientRequestValidator` created (inline)
- [x] `SuspendPatientCommandValidator` created with ExistsAsync pattern
- [x] `GetPatientQueryValidator` created with ExistsAsync pattern
- [x] `GetAllPatientsQueryValidator` created with IsInEnum
- [x] Validators registered in DI with `AddValidatorsFromAssembly`
- [x] Email validation uses `EmailValidationMode.AspNetCoreCompatible`
- [ ] `RequestValidationProcessor` pre-processor (optional)
- [ ] `ExceptionToJsonFilter` for exception handling (optional)
- [ ] `ValidationErrorWrapper` for structured error responses (optional)

---

## Folder Structure After This Step

```
Core/Scheduling/
+-- Scheduling.Application/
    +-- Pipeline/                             <- Optional
    |   +-- RequestValidationProcessor.cs
    +-- Patients/
    |   +-- Commands/
    |   |   +-- CreatePatientCommand.cs       <- Command + Request + Response + Validators
    |   |   +-- CreatePatientCommandHandler.cs
    |   |   +-- SuspendPatientCommand.cs      <- Command + Response + Validator
    |   |   +-- SuspendPatientCommandHandler.cs
    |   +-- Queries/
    |   |   +-- GetPatientQuery.cs            <- Query + Validator
    |   |   +-- GetPatientQueryHandler.cs
    |   |   +-- GetAllPatientsQuery.cs        <- Query + Validator
    |   |   +-- GetAllPatientsQueryHandler.cs
    |   +-- Dtos/
    |   |   +-- PatientDto.cs
    |   +-- EventHandlers/
    |       +-- PatientCreatedEventHandler.cs
    +-- ServiceCollectionExtensions.cs
BuildingBlocks/
+-- BuildingBlocks.Application/
    +-- UserValidator.cs                      <- Base validator class
    +-- IUnitOfWork.cs
    +-- IRepository.cs
    +-- SuccessOrFailureDto.cs
WebApi/
+-- Filters/                                  <- Optional
    +-- ExceptionToJsonFilter.cs
    +-- ValidationErrorWrapper.cs
```

---

-> Next: [05-pipeline-behaviors.md](./05-pipeline-behaviors.md) - Pipeline behaviors for logging, performance, validation pipeline
