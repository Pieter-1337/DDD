# Validator Tests

## Overview

Validator tests are **unit tests** that verify FluentValidation rules. They use mocked repositories for fast execution.

---

## Step 1: Create Bounded Context Validator Test Base

Each bounded context creates its own validator test base extending `ValidatorTestBase`.

Location: `Core/Scheduling/Scheduling.Domain.Tests/SchedulingValidatorTestBase.cs`

```csharp
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Tests;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Scheduling.Application;
using Scheduling.Domain.Patients;

namespace Scheduling.Tests;

/// <summary>
/// Base class for validator unit tests in the Scheduling bounded context.
/// Uses mocked IUnitOfWork - configure UnitOfWorkMock for validators that need entity existence checks.
/// </summary>
public abstract class SchedulingValidatorTestBase : ValidatorTestBase
{
    protected Mock<IRepository<Patient>> PatientRepositoryMock { get; private set; } = null!;

    protected override void RegisterServices(IServiceCollection services)
    {
        services.AddSchedulingApplication();

        // Setup repository mocks for validators that need them
        PatientRepositoryMock = new Mock<IRepository<Patient>>();
        UnitOfWorkMock.Setup(u => u.RepositoryFor<Patient>()).Returns(PatientRepositoryMock.Object);
    }

    /// <summary>
    /// Configure the mock to return true for ExistsAsync for the given patient ID.
    /// </summary>
    protected void SetupPatientExists(Guid patientId)
    {
        PatientRepositoryMock
            .Setup(r => r.ExistsAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    /// <summary>
    /// Configure the mock to return false for ExistsAsync for the given patient ID.
    /// </summary>
    protected void SetupPatientNotExists(Guid patientId)
    {
        PatientRepositoryMock
            .Setup(r => r.ExistsAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }
}
```

### Key Features

- **`AddSchedulingApplication()`** - Registers all validators from the application layer
- **Repository mocks** - Pre-configured for `ExistsAsync` checks
- **Helper methods** - `SetupPatientExists()` / `SetupPatientNotExists()` for common scenarios

---

## Step 2: Write Validator Tests

### Test File Location

```
Core/Scheduling/Scheduling.Domain.Tests/
└── ApplicationTests/
    └── ValidatorTests/
        ├── CreatePatientCommandValidatorTests.cs
        ├── SuspendPatientCommandValidatorTests.cs
        ├── ActivatePatientCommandValidatorTests.cs
        ├── GetPatientQueryValidatorTests.cs
        └── GetAllPatientsQueryValidatorTests.cs
```

### Example: CreatePatientCommandValidatorTests

```csharp
using BuildingBlocks.Enumerations;
using BuildingBlocks.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Application.Patients.Commands;
using Scheduling.Domain.Patients;
using Shouldly;

namespace Scheduling.Tests.ApplicationTests.ValidatorTests;

[TestClass]
public class CreatePatientCommandValidatorTests : SchedulingValidatorTestBase
{
    [TestMethod]
    public async Task Invalid_When_PatientIsNull()
    {
        // Arrange
        var command = new CreatePatientCommand(null!);

        // Act
        StartStopwatch();
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);
        StopStopwatch();

        // Assert
        // Built-in FluentValidation rule - use constant
        result.Errors.ShouldContainValidation(nameof(CreatePatientCommand.Patient), VALIDATION_NOT_NULL_VALIDATOR);
        result.Errors.Count.ShouldBe(1);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Invalid_When_RequiredFieldsAreEmpty()
    {
        // Arrange
        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "",
            LastName = "",
            Email = "",
            DateOfBirth = default,
            Status = null!  // SmartEnum uses string in DTOs
        });

        // Act
        StartStopwatch();
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);
        StopStopwatch();

        // Assert - Custom ErrorCodes use ErrorCode.X.Value
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.FirstName), ErrorCode.FirstNameRequired.Value);
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.LastName), ErrorCode.LastNameRequired.Value);
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.Email), ErrorCode.EmailRequired.Value);
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.Email), ErrorCode.InvalidEmail.Value);
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.DateOfBirth), ErrorCode.DateOfBirthRequired.Value);
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.Status), ErrorCode.InvalidStatus.Value);

        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Invalid_When_EmailIsInvalid()
    {
        // Arrange
        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "not-an-email",
            DateOfBirth = new DateTime(1990, 1, 15),
            Status = PatientStatus.Active.Name  // Use .Name to avoid magic strings
        });

        // Act
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);

        // Assert
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.Email), ErrorCode.InvalidEmail.Value);
        result.Errors.Count.ShouldBe(1);
    }

    [TestMethod]
    public async Task Valid_When_AllFieldsAreValid()
    {
        // Arrange
        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            DateOfBirth = new DateTime(1990, 1, 15),
            Status = PatientStatus.Active.Name  // Use .Name to avoid magic strings
        });

        // Act
        StartStopwatch();
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);
        StopStopwatch();

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Errors.Count.ShouldBe(0);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Valid_When_PhoneNumberIsNull()
    {
        // Arrange - PhoneNumber is optional
        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            DateOfBirth = new DateTime(1990, 1, 15),
            PhoneNumber = null,
            Status = PatientStatus.Active.Name
        });

        // Act
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeTrue();
    }
}
```

### Example: Testing Entity Existence

```csharp
using BuildingBlocks.Enumerations;
using BuildingBlocks.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Application.Patients.Commands;
using Shouldly;

namespace Scheduling.Tests.ApplicationTests.ValidatorTests;

[TestClass]
public class SuspendPatientCommandValidatorTests : SchedulingValidatorTestBase
{
    [TestMethod]
    public async Task Invalid_When_PatientDoesNotExist()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupPatientNotExists(patientId);  // <-- Mock returns false

        var command = new SuspendPatientCommand { Id = patientId };

        // Act
        StartStopwatch();
        var result = await ValidatorFor<SuspendPatientCommand>().ValidateAsync(command);
        StopStopwatch();

        // Assert
        result.Errors.ShouldContainValidation(nameof(SuspendPatientCommand.Id), ErrorCode.NotFound.Value);
        result.Errors.Count.ShouldBe(1);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Valid_When_PatientExists()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupPatientExists(patientId);  // <-- Mock returns true

        var command = new SuspendPatientCommand { Id = patientId };

        // Act
        StartStopwatch();
        var result = await ValidatorFor<SuspendPatientCommand>().ValidateAsync(command);
        StopStopwatch();

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Errors.Count.ShouldBe(0);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }
}
```

### Example: ActivatePatientCommandValidatorTests

```csharp
using BuildingBlocks.Enumerations;
using BuildingBlocks.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Application.Patients.Commands;
using Shouldly;

namespace Scheduling.Tests.ApplicationTests.ValidatorTests;

[TestClass]
public class ActivatePatientCommandValidatorTests : SchedulingValidatorTestBase
{
    [TestMethod]
    public async Task Invalid_When_PatientDoesNotExist()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupPatientNotExists(patientId);  // <-- Mock returns false

        var command = new ActivatePatientCommand { Id = patientId };

        // Act
        StartStopwatch();
        var result = await ValidatorFor<ActivatePatientCommand>().ValidateAsync(command);
        StopStopwatch();

        // Assert
        result.Errors.ShouldContainValidation(nameof(ActivatePatientCommand.Id), ErrorCode.NotFound.Value);
        result.Errors.Count.ShouldBe(1);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Valid_When_PatientExists()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupPatientExists(patientId);  // <-- Mock returns true

        var command = new ActivatePatientCommand { Id = patientId };

        // Act
        StartStopwatch();
        var result = await ValidatorFor<ActivatePatientCommand>().ValidateAsync(command);
        StopStopwatch();

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Errors.Count.ShouldBe(0);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }
}
```

---

## Assertion Patterns

### Using Custom ErrorCodes

When validators use `.WithErrorCode()`:

```csharp
// In validator
RuleFor(p => p.FirstName)
    .NotEmpty()
    .WithErrorCode(ErrorCode.FirstNameRequired.Value)
    .WithMessage(ErrorCode.FirstNameRequired.Message);

// In test
result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.FirstName), ErrorCode.FirstNameRequired.Value);
```

### Using Built-in FluentValidation Rules

When validators use built-in rules without custom error codes:

```csharp
// In validator
RuleFor(p => p.Patient).NotNull();

// In test
result.Errors.ShouldContainValidation(nameof(CreatePatientCommand.Patient), VALIDATION_NOT_NULL_VALIDATOR);
```

### Checking Error Count

Always verify the expected number of errors:

```csharp
result.Errors.Count.ShouldBe(1);  // Exactly one error expected
```

### Performance Assertions

Validators should be fast:

```csharp
StartStopwatch();
var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);
StopStopwatch();

ElapsedSeconds().ShouldBeLessThan(0.1M);  // Less than 100ms
```

---

## Common Test Scenarios

| Scenario | What to Test |
|----------|-------------|
| Required fields | Empty/null values should fail |
| Format validation | Invalid email, phone format |
| Entity existence | Non-existent IDs should fail |
| Optional fields | Null values should pass |
| Valid input | All fields correct should pass |
| Edge cases | Boundary values, special characters |

---

→ Next: [04-handler-tests.md](./04-handler-tests.md)
