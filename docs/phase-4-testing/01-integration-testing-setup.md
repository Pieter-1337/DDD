# Integration Testing Setup

## Overview

This project uses a generic `TestBase<TContext>` pattern that allows each bounded context to have its own test infrastructure while sharing common functionality.

---

## Project Structure

```
BuildingBlocks.Tests/
+-- TestBase.cs                        <- Generic base class

Core/Scheduling/Scheduling.Tests/
+-- Common/
|   +-- SchedulingTestBase.cs          <- Bounded context test base
+-- ApplicationTests/
|   +-- HandlerTests/
|       +-- CreatePatientCommandHandlerTests.cs
+-- DomainTests/
    +-- Patients/
        +-- PatientTests.cs
```

---

## Step 1: Create BuildingBlocks.Tests Project

Create a new test project at the solution root level:

```
BuildingBlocks.Tests/
+-- BuildingBlocks.Tests.csproj
+-- TestBase.cs
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
    <ProjectReference Include="..\BuildingBlocks\BuildingBlocks.Application\BuildingBlocks.Application.csproj" />
    <ProjectReference Include="..\BuildingBlocks\BuildingBlocks.Infrastructure\BuildingBlocks.Infrastructure.csproj" />
    <ProjectReference Include="..\BuildingBlocks\BuildingBlocks.Domain\BuildingBlocks.Domain.csproj" />
  </ItemGroup>

</Project>
```

**Note:** Package versions come from `Directory.Packages.props` (central package management).

---

## Step 2: Create Generic TestBase

Location: `BuildingBlocks.Tests/TestBase.cs`

```csharp
using BuildingBlocks.Application;
using BuildingBlocks.Domain;
using BuildingBlocks.Infrastructure;
using System.Diagnostics;
using System.Globalization;
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
/// Each test runs within a transaction that is rolled back after the test completes,
/// ensuring test isolation without recreating the database.
/// </summary>
[TestClass]
public abstract class TestBase<TContext> where TContext : DbContext
{
    #region FluentValidation Error Codes
    protected const string VALIDATION_NOT_EMPTY_VALIDATOR = "NotEmptyValidator";
    protected const string VALIDATION_NOT_NULL_VALIDATOR = "NotNullValidator";
    protected const string VALIDATION_EMAIL_VALIDATOR = "EmailValidator";
    protected const string VALIDATION_PREDICATE_VALIDATOR = "PredicateValidator";
    protected const string VALIDATION_ASYNCPREDICATE_VALIDATOR = "AsyncPredicateValidator";
    // ... add more as needed
    #endregion

    private ServiceProvider? _serviceProvider;  // NOT IServiceProvider - need Dispose()
    private SqliteConnection? _connection;
    private Stopwatch? _stopwatch;
    private IServiceScope? _scope;

    public TestContext? TestContext { get; set; }

    static TestBase()
    {
        ConfigureNBuilder();
    }

    /// <summary>
    /// Override to register bounded context-specific services (MediatR, validators, etc.)
    /// </summary>
    protected abstract void RegisterBoundedContextServices(IServiceCollection services);

    [TestInitialize]
    public void TestInitialize()
    {
        _stopwatch = new Stopwatch();

        // SQLite in-memory requires the connection to stay open
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();

        // Register logging (required by event handlers)
        services.AddLogging();

        // Register DbContext with SQLite
        services.AddDbContext<TContext>(options =>
            options.UseSqlite(_connection));

        // Register UnitOfWork
        services.AddScoped<IUnitOfWork, UnitOfWork<TContext>>();

        // Let derived class register bounded context services
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
    public void TestCleanup()
    {
        // Rollback transaction to keep database clean
        Uow.CloseTransactionAsync(new Exception("Test rollback")).GetAwaiter().GetResult();

        _scope?.Dispose();
        _scope = null;

        _serviceProvider?.Dispose();
        _serviceProvider = null;

        _connection?.Dispose();
        _connection = null;
    }

    #region Service Accessors

    protected IMediator GetMediator()
    {
        return _scope!.ServiceProvider.GetRequiredService<IMediator>();
    }

    protected IValidator<T> ValidatorFor<T>()
    {
        return _scope!.ServiceProvider.GetRequiredService<IValidator<T>>();
    }

    protected IUnitOfWork Uow => _scope!.ServiceProvider.GetRequiredService<IUnitOfWork>();

    protected TContext DbContext => _scope!.ServiceProvider.GetRequiredService<TContext>();

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

    #region NBuilder Configuration

    private static void ConfigureNBuilder()
    {
        // Disable auto-naming for base entity properties
        BuilderSetup.DisablePropertyNamingFor<Entity, Guid>(x => x.Id);
    }

    #endregion
}
```

### Key Design Decisions

1. **`ServiceProvider?` not `IServiceProvider?`** - `IServiceProvider` doesn't have `Dispose()`, but `ServiceProvider` does. Using the wrong type causes compile errors.

2. **Generic `TContext`** - Allows any bounded context's `DbContext` to be used.

3. **Abstract `RegisterBoundedContextServices`** - Each bounded context provides its own MediatR handlers and validators.

4. **Transaction-based isolation** - Each test runs in a transaction that's rolled back, so tests don't interfere with each other.

5. **SQLite in-memory** - Fast, no external database needed. Connection must stay open for the duration of the test.

---

## Step 3: Create Bounded Context Test Base

Each bounded context creates its own test base that extends `TestBase<TContext>`.

Location: `Core/Scheduling/Scheduling.Tests/Common/SchedulingTestBase.cs`

```csharp
using BuildingBlocks.Tests;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Scheduling.Application;
using Scheduling.Infrastructure.Persistence;

namespace Scheduling.Tests.Common;

public abstract class SchedulingTestBase : TestBase<SchedulingDbContext>
{
    protected override void RegisterBoundedContextServices(IServiceCollection services)
    {
        // Registers MediatR handlers and validators from Scheduling.Application
        services.AddSchedulingApplication();
    }
}
```

### Scheduling.Tests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>Scheduling.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="MSTest.TestAdapter" />
    <PackageReference Include="MSTest.TestFramework" />
    <PackageReference Include="Moq" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="NBuilder" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Scheduling.Application\Scheduling.Application.csproj" />
    <ProjectReference Include="..\Scheduling.Domain\Scheduling.Domain.csproj" />
    <ProjectReference Include="..\Scheduling.Infrastructure\Scheduling.Infrastructure.csproj" />
    <ProjectReference Include="..\..\..\BuildingBlocks.Tests\BuildingBlocks.Tests.csproj" />
  </ItemGroup>

</Project>
```

---

## Step 4: Write Tests

Tests extend the bounded context test base:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Tests.Common;
using Scheduling.Application.Patients.Commands;
using Shouldly;
using FizzWare.NBuilder;

namespace Scheduling.Tests.ApplicationTests.HandlerTests;

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
        StartStopwatch();
        var response = await GetMediator().Send(command);
        StopStopwatch();

        // Assert
        response.ShouldNotBeNull();
        response.Success.ShouldBeTrue();
        response.PatientDto.Id.ShouldNotBe(default);

        ElapsedSeconds().ShouldBeLessThan(1M);
    }
}
```

---

## Available Test Helpers

### From TestBase

| Helper | Description |
|--------|-------------|
| `GetMediator()` | Returns `IMediator` for sending commands/queries |
| `ValidatorFor<T>()` | Returns `IValidator<T>` for direct validator testing |
| `Uow` | Returns `IUnitOfWork` for repository access |
| `DbContext` | Returns the bounded context's `DbContext` |
| `StartStopwatch()` / `StopStopwatch()` | Performance timing |
| `ElapsedSeconds()` / `ElapsedMilliseconds()` | Get timing results |
| `SetCurrentCulture(language)` | Set culture for localization tests |

### FluentValidation Error Codes

Constants for asserting specific validation failures:

```csharp
VALIDATION_NOT_EMPTY_VALIDATOR
VALIDATION_NOT_NULL_VALIDATOR
VALIDATION_EMAIL_VALIDATOR
VALIDATION_PREDICATE_VALIDATOR
VALIDATION_ASYNCPREDICATE_VALIDATOR
// ... etc
```

---

## Adding a New Bounded Context

To add testing for a new bounded context (e.g., Billing):

1. Create `Core/Billing/Billing.Tests/` project
2. Reference `BuildingBlocks.Tests`
3. Create `BillingTestBase`:

```csharp
public abstract class BillingTestBase : TestBase<BillingDbContext>
{
    protected override void RegisterBoundedContextServices(IServiceCollection services)
    {
        // Assumes AddBillingApplication() registers MediatR + validators
        services.AddBillingApplication();
    }
}
```

4. Write tests extending `BillingTestBase`

