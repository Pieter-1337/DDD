# Test Infrastructure

## Overview

This document covers setting up the shared test infrastructure in `BuildingBlocks.Tests`.

---

## Step 1: Create BuildingBlocks.Tests Project

Create a new test project at the solution root level:

```bash
dotnet new mstest -n BuildingBlocks.Tests -o BuildingBlocks/BuildingBlocks.Tests
dotnet sln add BuildingBlocks/BuildingBlocks.Tests
```

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
    <ProjectReference Include="..\BuildingBlocks.Application\BuildingBlocks.Application.csproj" />
    <ProjectReference Include="..\BuildingBlocks.Infrastructure\BuildingBlocks.Infrastructure.csproj" />
    <ProjectReference Include="..\BuildingBlocks.Domain\BuildingBlocks.Domain.csproj" />
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

### Key Features

| Feature | Description |
|---------|-------------|
| **Mocked `IUnitOfWork`** | Fast tests without database |
| **Validation error code constants** | For asserting specific validation rules |
| **`ShouldContainValidation()` extension** | Assert validation errors using `nameof()` |
| **Stopwatch helpers** | For performance assertions |
| **Culture helpers** | For localization tests |

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

### Key Differences from ValidatorTestBase

| Feature | ValidatorTestBase | TestBase<TContext> |
|---------|------------------|-------------------|
| Database | Mocked | Real SQLite in-memory |
| IUnitOfWork | Mocked | Real implementation |
| Test isolation | DI scope | Transaction rollback |
| Use case | Validator unit tests | Handler integration tests |

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
VALIDATION_MAXLENGTH_VALIDATOR
VALIDATION_GREATERTHAN_VALIDATOR
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

→ Next: [03-validator-tests.md](./03-validator-tests.md)
