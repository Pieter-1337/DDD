# Command Validation with FluentValidation

## Why Validate Commands?

Commands carry user input. Before passing to handlers, we should validate:

1. **Format validation** - Is the email format correct?
2. **Required fields** - Is the first name provided?
3. **Business preconditions** - Does the patient exist? Is the date valid?

---

## Where Does Validation Live?

**All validation lives in FluentValidation.** The domain focuses on behavior only.

```
┌─────────────────────────────────────────────────────────────┐
│                      Responsibility Split                    │
├─────────────────────────────────────────────────────────────┤
│  FluentValidation  │  ALL validation (input + preconditions) │
│  Domain            │  Behavior and state transitions only    │
└─────────────────────────────────────────────────────────────┘
```

**Business preconditions** vs **Business rules**:

```csharp
// FluentValidation - preconditions (CAN we do this?)
RuleFor(x => x.Email).NotEmpty().EmailAddress();
RuleFor(x => x.PatientId).MustAsync(PatientExists);
RuleFor(x => x.PatientId).MustAsync(PatientNotAlreadySuspended);

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
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
</ItemGroup>
```

### Step 2: Create CreatePatientCommandValidator

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/CreatePatient/CreatePatientCommandValidator.cs`

```csharp
using FluentValidation;

namespace Scheduling.Application.Patients.Commands.CreatePatient;

public class CreatePatientCommandValidator : AbstractValidator<CreatePatientCommand>
{
    public CreatePatientCommandValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required")
            .MaximumLength(100).WithMessage("First name cannot exceed 100 characters");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required")
            .MaximumLength(100).WithMessage("Last name cannot exceed 100 characters");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .MaximumLength(255).WithMessage("Email cannot exceed 255 characters");

        RuleFor(x => x.DateOfBirth)
            .NotEmpty().WithMessage("Date of birth is required")
            .LessThan(DateTime.UtcNow).WithMessage("Date of birth must be in the past")
            .GreaterThan(DateTime.UtcNow.AddYears(-150)).WithMessage("Invalid date of birth");

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(20).WithMessage("Phone number cannot exceed 20 characters")
            .When(x => x.PhoneNumber is not null);
    }
}
```

### Step 3: Create SuspendPatientCommandValidator

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/SuspendPatient/SuspendPatientCommandValidator.cs`

```csharp
using FluentValidation;

namespace Scheduling.Application.Patients.Commands.SuspendPatient;

public class SuspendPatientCommandValidator : AbstractValidator<SuspendPatientCommand>
{
    public SuspendPatientCommandValidator()
    {
        RuleFor(x => x.PatientId)
            .NotEmpty().WithMessage("Patient ID is required");
    }
}
```

### Step 4: Create UpdateContactInfoCommandValidator

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/UpdateContactInfo/UpdateContactInfoCommandValidator.cs`

```csharp
using FluentValidation;

namespace Scheduling.Application.Patients.Commands.UpdateContactInfo;

public class UpdateContactInfoCommandValidator : AbstractValidator<UpdateContactInfoCommand>
{
    public UpdateContactInfoCommandValidator()
    {
        RuleFor(x => x.PatientId)
            .NotEmpty().WithMessage("Patient ID is required");

        RuleFor(x => x.NewEmail)
            .EmailAddress().WithMessage("Invalid email format")
            .MaximumLength(255).WithMessage("Email cannot exceed 255 characters")
            .When(x => !string.IsNullOrEmpty(x.NewEmail));

        RuleFor(x => x.NewPhoneNumber)
            .MaximumLength(20).WithMessage("Phone number cannot exceed 20 characters")
            .When(x => !string.IsNullOrEmpty(x.NewPhoneNumber));

        // At least one field should be provided
        RuleFor(x => x)
            .Must(x => !string.IsNullOrEmpty(x.NewEmail) || !string.IsNullOrEmpty(x.NewPhoneNumber))
            .WithMessage("At least one contact field must be provided");
    }
}
```

### Step 5: Create ValidationException

Location: `Core/Scheduling/Scheduling.Application/Exceptions/ValidationException.cs`

```csharp
using FluentValidation.Results;

namespace Scheduling.Application.Exceptions;

public class ValidationException : Exception
{
    public ValidationException() : base("One or more validation failures have occurred.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IEnumerable<ValidationFailure> failures) : this()
    {
        Errors = failures
            .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }

    public IDictionary<string, string[]> Errors { get; }
}
```

### Step 6: Create ValidationBehavior (MediatR Pipeline)

Location: `Core/Scheduling/Scheduling.Application/Behaviors/ValidationBehavior.cs`

```csharp
using FluentValidation;
using MediatR;
using ValidationException = Scheduling.Application.Exceptions.ValidationException;

namespace Scheduling.Application.Behaviors;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next();
    }
}
```

**Key points:**
- `IPipelineBehavior` intercepts all MediatR requests
- Runs all validators for the request type
- Throws `ValidationException` if any validation fails
- Continues to handler if validation passes

### Step 7: Register Validators and Behavior

Update `Core/Scheduling/Scheduling.Application/ServiceCollectionExtensions.cs`:

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Scheduling.Application.Behaviors;

namespace Scheduling.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingApplication(this IServiceCollection services)
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;

        // Register MediatR
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        // Register all validators from this assembly
        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}
```

### Step 8: Handle ValidationException in API

Location: `WebApi/Middleware/ExceptionHandlingMiddleware.cs`

```csharp
using System.Text.Json;
using Scheduling.Application.Exceptions;

namespace WebApi.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed: {Errors}", ex.Errors);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";

            var response = new
            {
                title = "Validation Failed",
                status = 400,
                errors = ex.Errors
            };

            await context.Response.WriteAsJsonAsync(response);
        }
        catch (PatientNotFoundException ex)
        {
            _logger.LogWarning("Patient not found: {PatientId}", ex.PatientId);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/json";

            var response = new
            {
                title = "Not Found",
                status = 404,
                detail = ex.Message
            };

            await context.Response.WriteAsJsonAsync(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var response = new
            {
                title = "Internal Server Error",
                status = 500,
                detail = "An unexpected error occurred"
            };

            await context.Response.WriteAsJsonAsync(response);
        }
    }
}
```

### Step 9: Register Middleware

Location: `WebApi/Program.cs`

```csharp
using Scheduling.Infrastructure;
using Scheduling.Application;
using WebApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddSchedulingInfrastructure(connectionString);
builder.Services.AddSchedulingApplication();

var app = builder.Build();

// Add exception handling middleware (before other middleware)
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

---

## Validation Guidelines

### 1. All Validation in FluentValidation

```csharp
// Input validation
RuleFor(x => x.Email).NotEmpty().EmailAddress();

// Business preconditions - also in validator!
RuleFor(x => x.Email)
    .MustAsync(async (email, ct) => !await _repo.EmailExistsAsync(email))
    .WithMessage("Email already in use");

// Existence checks
RuleFor(x => x.PatientId)
    .MustAsync(async (id, ct) => await _repo.ExistsAsync(id, ct))
    .WithMessage("Patient not found");
```

This keeps ALL validation with custom messages in one place.

### 2. Keep Validators Focused

```csharp
// GOOD - simple, focused rules
RuleFor(x => x.FirstName)
    .NotEmpty()
    .MaximumLength(100);

// BAD - complex business logic
RuleFor(x => x)
    .Must(x => {
        // 50 lines of business logic...
    });
```

### 3. Use Custom Messages

```csharp
// GOOD - user-friendly messages
RuleFor(x => x.Email)
    .NotEmpty().WithMessage("Please provide an email address")
    .EmailAddress().WithMessage("Please provide a valid email address");

// BAD - technical messages
RuleFor(x => x.Email)
    .NotEmpty()  // Default: "'Email' must not be empty"
    .EmailAddress();  // Default: "'Email' is not a valid email address"
```

### 4. Async Validation for External Checks

```csharp
// Database checks
RuleFor(x => x.Email)
    .MustAsync(async (email, ct) =>
        !await _context.Patients.AnyAsync(p => p.Email == email, ct))
    .WithMessage("Email is already registered");

// External API validation (e.g., VAT number)
RuleFor(x => x.VatNumber)
    .MustAsync(async (vat, ct) => await _vatService.IsValidAsync(vat, ct))
    .WithMessage("VAT number is not valid");
```

Async validation is the right place for any checks that need external data or services.

---

## Testing Validators

```csharp
public class CreatePatientCommandValidatorTests
{
    private readonly CreatePatientCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_ReturnsSuccess()
    {
        var command = new CreatePatientCommand(
            "John", "Doe", "john@example.com",
            DateTime.UtcNow.AddYears(-30), null);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyFirstName_ReturnsError()
    {
        var command = new CreatePatientCommand(
            "", "Doe", "john@example.com",
            DateTime.UtcNow.AddYears(-30), null);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FirstName");
    }

    [Fact]
    public void Validate_InvalidEmail_ReturnsError()
    {
        var command = new CreatePatientCommand(
            "John", "Doe", "not-an-email",
            DateTime.UtcNow.AddYears(-30), null);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_FutureDateOfBirth_ReturnsError()
    {
        var command = new CreatePatientCommand(
            "John", "Doe", "john@example.com",
            DateTime.UtcNow.AddYears(1), null);  // Future date

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DateOfBirth");
    }
}
```

---

## Verification Checklist

- [ ] FluentValidation packages added to Application project
- [ ] `CreatePatientCommandValidator` created
- [ ] `SuspendPatientCommandValidator` created
- [ ] `UpdateContactInfoCommandValidator` created
- [ ] `ValidationException` class created
- [ ] `ValidationBehavior` pipeline behavior created
- [ ] Validators registered in DI
- [ ] `ExceptionHandlingMiddleware` created
- [ ] Middleware registered in Program.cs
- [ ] Validation tests pass

---

## Folder Structure After This Step

```
Core/Scheduling/
└── Scheduling.Application/
    ├── Behaviors/
    │   └── ValidationBehavior.cs
    ├── Common/
    │   └── PagedResult.cs
    ├── Exceptions/
    │   ├── PatientNotFoundException.cs
    │   └── ValidationException.cs
    ├── Patients/
    │   ├── Commands/
    │   │   ├── CreatePatient/
    │   │   │   ├── CreatePatientCommand.cs
    │   │   │   ├── CreatePatientCommandHandler.cs
    │   │   │   └── CreatePatientCommandValidator.cs
    │   │   ├── SuspendPatient/
    │   │   │   ├── SuspendPatientCommand.cs
    │   │   │   ├── SuspendPatientCommandHandler.cs
    │   │   │   └── SuspendPatientCommandValidator.cs
    │   │   └── UpdateContactInfo/
    │   │       ├── UpdateContactInfoCommand.cs
    │   │       ├── UpdateContactInfoCommandHandler.cs
    │   │       └── UpdateContactInfoCommandValidator.cs
    │   ├── Queries/
    │   │   └── ...
    │   └── EventHandlers/
    │       └── PatientCreatedEventHandler.cs
    └── ServiceCollectionExtensions.cs
WebApi/
├── Middleware/
│   └── ExceptionHandlingMiddleware.cs
└── Program.cs
```

---

→ Next: [05-pipeline-behaviors.md](./05-pipeline-behaviors.md) - More pipeline behaviors for logging, performance, etc.
