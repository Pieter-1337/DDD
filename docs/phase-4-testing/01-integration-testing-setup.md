# Integration Testing Setup

## Overview

This project uses a two-tier test base hierarchy:

1. **`ValidatorTestBase`** - For fast unit tests with mocked `IUnitOfWork`
2. **`TestBase<TContext>`** - For integration tests with real SQLite database (inherits from ValidatorTestBase)

Each bounded context then creates its own test bases extending these.

---

## Project Structure

```
BuildingBlocks.Tests/
├── ValidatorTestBase.cs          <- Unit tests with mocked IUnitOfWork
└── TestBase.cs                   <- Integration tests (inherits ValidatorTestBase)

Core/Scheduling/Scheduling.Domain.Tests/
├── SchedulingValidatorTestBase.cs   <- Validator unit tests
├── SchedulingDbTestBase.cs          <- Handler integration tests
├── ApplicationTests/
│   ├── HandlerTests/
│   │   ├── CreatePatientCommandHandlerTests.cs
│   │   ├── GetAllPatientsQueryHandlerTests.cs
│   │   └── GetPatientQueryHandlerTests.cs
│   └── ValidatorTests/
│       ├── CreatePatientCommandValidatorTests.cs
│       ├── GetPatientQueryValidatorTests.cs
│       ├── GetAllPatientsQueryValidatorTests.cs
│       └── SuspendPatientCommandValidatorTests.cs
└── DomainTests/
    └── Patients/
        └── PatientTests.cs
```

---

## Step 1: Create BuildingBlocks.Tests Project

Create a new test project at the solution root level:

### BuildingBlocks.Tests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="MSTest.TestAdapter" />
    <PackageReference Include="MSTest.TestFramework" />
    <PackageReference Include="Moq" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="NBuilder" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
    <PackageReference Include="MediatR" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\BuildingBlocks\BuildingBlocks.Application\BuildingBlocks.Application.csproj" />
    <ProjectReference Include="..\BuildingBlocks\BuildingBlocks.Infrastructure\BuildingBlocks.Infrastructure.csproj" />
    <ProjectReference Include="..\BuildingBlocks\BuildingBlocks.Domain\BuildingBlocks.Domain.csproj" />
  </ItemGroup>

</Project>
```

**Note:** Package versions come from `Directory.Packages.props` (central package management).

---

## Step 2: Create ValidatorTestBase (Unit Tests)

This base class provides a DI container with **mocked `IUnitOfWork`** for fast validator unit tests.

Location: `BuildingBlocks.Tests/ValidatorTestBase.cs`

```csharp
using System.Diagnostics;
using System.Globalization;
using BuildingBlocks.Application.Interfaces;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;

namespace BuildingBlocks.Tests;

/// <summary>
/// Base class for validator unit tests.
/// Provides DI container with validators and a mocked IUnitOfWork.
/// </summary>
[TestClass]
public abstract class ValidatorTestBase
{
    #region FluentValidation Error Codes

    protected const string VALIDATION_NULL_VALIDATOR = "NullValidator";
    protected const string VALIDATION_EMPTY_VALIDATOR = "EmptyValidator";
    protected const string VALIDATION_NOT_EMPTY_VALIDATOR = "NotEmptyValidator";
    protected const string VALIDATION_NOT_NULL_VALIDATOR = "NotNullValidator";
    protected const string VALIDATION_GREATERTHAN_VALIDATOR = "GreaterThanValidator";
    protected const string VALIDATION_GREATERTHANOREQUAL_VALIDATOR = "GreaterThanOrEqualValidator";
    protected const string VALIDATION_LESSTHAN_VALIDATOR = "LessThanValidator";
    protected const string VALIDATION_LESSTHANOREQUAL_VALIDATOR = "LessThanOrEqualValidator";
    protected const string VALIDATION_INCLUSIVEBETWEEN_VALIDATOR = "InclusiveBetweenValidator";
    protected const string VALIDATION_MAXLENGTH_VALIDATOR = "MaximumLengthValidator";
    protected const string VALIDATION_PREDICATE_VALIDATOR = "PredicateValidator";
    protected const string VALIDATION_ASYNCPREDICATE_VALIDATOR = "AsyncPredicateValidator";
    protected const string VALIDATION_EMAIL_VALIDATOR = "EmailValidator";
    protected const string VALIDATION_EQUAL_VALIDATOR = "EqualValidator";
    protected const string VALIDATION_NOT_EQUAL_VALIDATOR = "NotEqualValidator";
    protected const string VALIDATION_REGEX_VALIDATOR = "RegularExpressionValidator";
    protected const string VALIDATION_ENUM_VALIDATOR = "EnumValidator";

    #endregion

    private ServiceProvider? _serviceProvider;
    private Stopwatch? _stopwatch;

    protected Mock<IUnitOfWork> UnitOfWorkMock { get; private set; } = null!;

    public TestContext? TestContext { get; set; }

    /// <summary>
    /// Override to register validators and any additional services.
    /// </summary>
    protected abstract void RegisterServices(IServiceCollection services);

    [TestInitialize]
    public virtual void TestInitialize()
    {
        _stopwatch = new Stopwatch();

        var services = new ServiceCollection();

        // Register mocked IUnitOfWork for validators that need it
        UnitOfWorkMock = new Mock<IUnitOfWork>();
        services.AddSingleton(UnitOfWorkMock.Object);

        // Let derived class register validators
        RegisterServices(services);

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public virtual void TestCleanup()
    {
        _serviceProvider?.Dispose();
        _serviceProvider = null;
    }

    #region Service Accessors

    protected IValidator<T> ValidatorFor<T>()
    {
        return _serviceProvider!.GetRequiredService<IValidator<T>>();
    }

    protected T GetService<T>() where T : notnull
    {
        return _serviceProvider!.GetRequiredService<T>();
    }

    #endregion

    #region Stopwatch

    protected void StartStopwatch() => _stopwatch?.Restart();
    protected void StopStopwatch() => _stopwatch?.Stop();
    protected decimal ElapsedSeconds() => _stopwatch is not null
        ? (decimal)_stopwatch.Elapsed.TotalSeconds
        : -1;
    protected long ElapsedMilliseconds() => _stopwatch?.ElapsedMilliseconds ?? -1;

    #endregion

    #region Culture

    protected void SetCurrentCulture(string language = "en-GB")
    {
        CultureInfo.CurrentCulture = new CultureInfo(language);
        CultureInfo.CurrentUICulture = new CultureInfo(language);
    }

    #endregion
}

/// <summary>
/// Extension methods for FluentValidation assertions.
/// </summary>
public static class ValidationResultExtensions
{
    /// <summary>
    /// Assert if a validation message exists with specified property name and error code.
    /// Use with nameof() for property names.
    /// </summary>
    public static void ShouldContainValidation(this IEnumerable<ValidationFailure> actual, string propertyName, string errorCode)
    {
        actual.ShouldContain(e => e.FormattedMessagePlaceholderValues.Contains(new KeyValuePair<string, object>("PropertyName", propertyName)) && e.ErrorCode == errorCode,
            $"No validation message exists with property name '{propertyName}' for rule '{errorCode}'");
    }
}
```

**Key features:**
- **Mocked `IUnitOfWork`** - Fast tests without database
- **Validation error code constants** - For asserting specific validation rules
- **`ShouldContainValidation()` extension** - Assert validation errors using `nameof()`
- **Stopwatch helpers** - For performance assertions

---

## Step 3: Create TestBase<TContext> (Integration Tests)

This base class **inherits from `ValidatorTestBase`** and adds real database support.

Location: `BuildingBlocks.Tests/TestBase.cs`

```csharp
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Domain;
using BuildingBlocks.Infrastructure;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using FluentValidation;
using FizzWare.NBuilder;

namespace BuildingBlocks.Tests;

/// <summary>
/// Base class for integration tests.
/// Each test runs within a transaction that is rolled back after the test completes.
/// </summary>
[TestClass]
public abstract class TestBase<TContext> : ValidatorTestBase where TContext : DbContext
{
    private ServiceProvider? _serviceProvider;
    private SqliteConnection? _connection;
    private IServiceScope? _scope;

    static TestBase()
    {
        ConfigureNBuilder();
    }

    /// <summary>
    /// Override to register bounded context-specific services (MediatR, validators, etc.)
    /// </summary>
    protected abstract void RegisterBoundedContextServices(IServiceCollection services);

    protected sealed override void RegisterServices(IServiceCollection services)
    {
        // Not used - we register everything in TestInitialize with real database
    }

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize(); // For stopwatch only

        // SQLite in-memory requires the connection to stay open
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();

        services.AddLogging();

        // Register DbContext with SQLite
        services.AddDbContext<TContext>(options =>
            options.UseSqlite(_connection));

        // Register UnitOfWork (real implementation, not mock)
        services.AddScoped<IUnitOfWork, UnitOfWork<TContext>>();

        // Register bounded context services
        RegisterBoundedContextServices(services);

        _serviceProvider = services.BuildServiceProvider();

        // Ensure database is created
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();
        dbContext.Database.EnsureCreated();

        // Create scope for test and begin transaction
        _scope = _serviceProvider.CreateScope();
        Uow.BeginTransactionAsync().GetAwaiter().GetResult();
    }

    [TestCleanup]
    public override void TestCleanup()
    {
        // Rollback transaction to keep database clean
        Uow.CloseTransactionAsync(new Exception("Test rollback")).GetAwaiter().GetResult();

        _scope?.Dispose();
        _scope = null;

        _serviceProvider?.Dispose();
        _serviceProvider = null;

        _connection?.Dispose();
        _connection = null;

        base.TestCleanup();
    }

    #region Service Accessors

    protected IMediator GetMediator()
    {
        return _scope!.ServiceProvider.GetRequiredService<IMediator>();
    }

    protected new IValidator<T> ValidatorFor<T>()
    {
        return _scope!.ServiceProvider.GetRequiredService<IValidator<T>>();
    }

    protected IUnitOfWork Uow => _scope!.ServiceProvider.GetRequiredService<IUnitOfWork>();

    protected TContext DbContext => _scope!.ServiceProvider.GetRequiredService<TContext>();

    #endregion

    #region NBuilder Configuration

    private static void ConfigureNBuilder()
    {
        BuilderSetup.DisablePropertyNamingFor<Entity, Guid>(x => x.Id);
    }

    #endregion
}
```

**Key differences from ValidatorTestBase:**
- **Real SQLite database** (in-memory)
- **Real `IUnitOfWork`** (not mocked)
- **Transaction-based isolation** - rollback after each test
- **`GetMediator()`** - for sending commands/queries

---

## Step 4: Create Bounded Context Test Bases

Each bounded context creates **two test bases**:

### SchedulingValidatorTestBase (Unit Tests)

For fast validator unit tests with mocked repositories.

Location: `Core/Scheduling/Scheduling.Domain.Tests/SchedulingValidatorTestBase.cs`

```csharp
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Tests;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Scheduling.Application;
using Scheduling.Domain.Patients;

namespace Scheduling.Tests;

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

### SchedulingDbTestBase (Integration Tests)

For full integration tests with real database.

Location: `Core/Scheduling/Scheduling.Domain.Tests/SchedulingDbTestBase.cs`

```csharp
using BuildingBlocks.Tests;
using Microsoft.Extensions.DependencyInjection;
using Scheduling.Application;
using Scheduling.Infrastructure.Persistence;

namespace Scheduling.Tests;

public class SchedulingDbTestBase : TestBase<SchedulingDbContext>
{
    protected override void RegisterBoundedContextServices(IServiceCollection services)
    {
        services.AddSchedulingApplication();
    }
}
```

---

## Step 5: Write Validator Tests

Validator tests use `SchedulingValidatorTestBase` with mocked repositories:

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
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);

        // Assert
        // Built-in FluentValidation rule - use constant
        result.Errors.ShouldContainValidation(nameof(CreatePatientCommand.Patient), VALIDATION_NOT_NULL_VALIDATOR);
        result.Errors.Count.ShouldBe(1);
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
            DateOfBirth = default
        });

        // Act
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);

        // Assert - Custom ErrorCodes use ErrorCode.X.Value
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.FirstName), ErrorCode.FirstNameRequired.Value);
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.LastName), ErrorCode.LastNameRequired.Value);
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.Email), ErrorCode.EmailRequired.Value);
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.Email), ErrorCode.InvalidEmail.Value);
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.DateOfBirth), ErrorCode.DateOfBirthRequired.Value);
    }
}
```

**Note:**
- Use `nameof()` with `ShouldContainValidation()` for type-safe assertions
- For custom error codes (set with `.WithErrorCode()`), use `ErrorCode.X.Value`
- For built-in FluentValidation rules (like `NotNull()`), use the constants like `VALIDATION_NOT_NULL_VALIDATOR`

---

## Step 6: Write Handler Tests

Handler tests use `SchedulingDbTestBase` with real database:

```csharp
using BuildingBlocks.Tests;
using FizzWare.NBuilder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Application.Patients.Commands;
using Scheduling.Domain.Patients;
using Shouldly;

namespace Scheduling.Tests.ApplicationTests.HandlerTests;

[TestClass]
public class CreatePatientCommandHandlerTests : SchedulingDbTestBase
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
            .With(p => p.PhoneNumber = "+1234567890")
            .Build();

        var command = new CreatePatientCommand(request);

        // Act
        StartStopwatch();
        var response = await GetMediator().Send(command);
        StopStopwatch();

        // Assert
        response.ShouldNotBeNull();
        response.Success.ShouldBeTrue();
        response.Message.ShouldNotBeNullOrEmpty();
        response.PatientDto.ShouldNotBeNull();
        response.PatientDto.Id.ShouldNotBe(default);

        // Verify persistence
        var reloaded = await Uow.RepositoryFor<Patient>().GetByIdAsync(response.PatientDto.Id);
        reloaded.ShouldNotBeNull();
        reloaded!.FirstName.ShouldBe("John");
        reloaded.Status.ShouldBe(PatientStatus.Active);

        ElapsedSeconds().ShouldBeLessThan(1M);
    }
}
```

---

## Step 7: Write Domain Tests

Domain tests are pure unit tests without any base class:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Domain.Patients;
using Shouldly;

namespace Scheduling.Tests.DomainTests.Patients;

[TestClass]
public class PatientTests
{
    [TestMethod]
    public void Create_ShouldCreatePatientWithCorrectValues()
    {
        // Arrange & Act
        var patient = Patient.Create("John", "Doe", "john@test.com", new DateTime(1990, 1, 15));

        // Assert
        patient.Id.ShouldNotBe(default);
        patient.FirstName.ShouldBe("John");
        patient.LastName.ShouldBe("Doe");
        patient.Email.ShouldBe("john@test.com");
        patient.Status.ShouldBe(PatientStatus.Active);
    }

    [TestMethod]
    public void Suspend_ShouldChangeStatusToSuspended()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "john@test.com", new DateTime(1990, 1, 15));

        // Act
        patient.Suspend();

        // Assert
        patient.Status.ShouldBe(PatientStatus.Suspended);
    }
}
```

---

## When to Use Which Base Class

| Test Type | Base Class | Database | Speed | Use For |
|-----------|------------|----------|-------|---------|
| Validator unit tests | `SchedulingValidatorTestBase` | Mocked | Fast | Testing validation rules |
| Handler integration tests | `SchedulingDbTestBase` | SQLite | Medium | Testing full command/query pipeline |
| Domain entity tests | None | None | Fastest | Testing entity behavior |

---

## Available Test Helpers

### From ValidatorTestBase

| Helper | Description |
|--------|-------------|
| `ValidatorFor<T>()` | Returns `IValidator<T>` for direct validator testing |
| `UnitOfWorkMock` | Mocked `IUnitOfWork` for setup |
| `StartStopwatch()` / `StopStopwatch()` | Performance timing |
| `ElapsedSeconds()` / `ElapsedMilliseconds()` | Get timing results |
| `SetCurrentCulture(language)` | Set culture for localization tests |

### From TestBase<TContext> (inherits ValidatorTestBase)

| Helper | Description |
|--------|-------------|
| `GetMediator()` | Returns `IMediator` for sending commands/queries |
| `Uow` | Returns real `IUnitOfWork` for repository access |
| `DbContext` | Returns the bounded context's `DbContext` |

### FluentValidation Error Codes

```csharp
VALIDATION_NOT_EMPTY_VALIDATOR
VALIDATION_NOT_NULL_VALIDATOR
VALIDATION_EMAIL_VALIDATOR
VALIDATION_PREDICATE_VALIDATOR
VALIDATION_ASYNCPREDICATE_VALIDATOR
VALIDATION_ENUM_VALIDATOR
// ... and more
```

### ShouldContainValidation Extension

```csharp
// For custom error codes (set with .WithErrorCode())
result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.Email), ErrorCode.InvalidEmail.Value);

// For built-in FluentValidation rules (no custom error code)
result.Errors.ShouldContainValidation(nameof(CreatePatientCommand.Patient), VALIDATION_NOT_NULL_VALIDATOR);
```

---

## Adding a New Bounded Context

To add testing for a new bounded context (e.g., Billing):

1. Create `Core/Billing/Billing.Tests/` project
2. Reference `BuildingBlocks.Tests`
3. Create two test bases:

```csharp
// BillingValidatorTestBase.cs - for validator unit tests
public abstract class BillingValidatorTestBase : ValidatorTestBase
{
    protected override void RegisterServices(IServiceCollection services)
    {
        services.AddBillingApplication();
        // Setup any repository mocks needed
    }
}

// BillingDbTestBase.cs - for integration tests
public class BillingDbTestBase : TestBase<BillingDbContext>
{
    protected override void RegisterBoundedContextServices(IServiceCollection services)
    {
        services.AddBillingApplication();
    }
}
```

4. Write tests extending the appropriate base class

---

## Verification Checklist

- [x] `BuildingBlocks.Tests` project created
- [x] `ValidatorTestBase` with mocked `IUnitOfWork` and validation constants
- [x] `TestBase<TContext>` inheriting from `ValidatorTestBase`
- [x] `ShouldContainValidation()` extension method
- [x] `SchedulingValidatorTestBase` for validator unit tests
- [x] `SchedulingDbTestBase` for integration tests
- [x] Transaction-based test isolation
- [x] SQLite in-memory database for integration tests
- [x] Validator tests for all commands/queries
- [x] Handler tests for commands/queries
- [x] Domain tests for entity behavior

---

## Next Steps

Continue to Phase 5: Event-Driven Architecture (when ready).
