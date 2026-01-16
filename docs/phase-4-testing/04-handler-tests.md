# Handler Tests

## Overview

Handler tests are **integration tests** that verify the full command/query pipeline including:
- Command/Query handlers
- Database persistence
- Domain logic
- MediatR pipeline

They use a real SQLite in-memory database with transaction rollback for isolation.

---

## Step 1: Create Bounded Context Database Test Base

Each bounded context creates its own database test base extending `TestBase<TContext>`.

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

### What This Provides

| Feature | Description |
|---------|-------------|
| **Real SQLite database** | In-memory, schema created automatically |
| **Real `IUnitOfWork`** | Actual persistence, not mocked |
| **Transaction isolation** | Each test rolls back after completion |
| **`GetMediator()`** | Send commands and queries |
| **`Uow`** | Access repositories directly |
| **`DbContext`** | Direct database access if needed |

---

## Step 2: Write Handler Tests

### Test File Location

```
Core/Scheduling/Scheduling.Domain.Tests/
└── ApplicationTests/
    └── HandlerTests/
        ├── CreatePatientCommandHandlerTests.cs
        ├── GetPatientQueryHandlerTests.cs
        └── GetAllPatientsQueryHandlerTests.cs
```

### Example: CreatePatientCommandHandlerTests

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
        reloaded.LastName.ShouldBe("Doe");
        reloaded.Email.ShouldBe("john.doe@example.com");
        reloaded.Status.ShouldBe(PatientStatus.Active);

        ElapsedSeconds().ShouldBeLessThan(1M);
    }

    [TestMethod]
    public async Task Handle_Should_CreatePatient_WithoutPhoneNumber()
    {
        // Arrange
        var request = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "Jane")
            .With(p => p.LastName = "Smith")
            .With(p => p.Email = "jane.smith@example.com")
            .With(p => p.DateOfBirth = new DateTime(1985, 6, 20))
            .With(p => p.PhoneNumber = null)
            .Build();

        var command = new CreatePatientCommand(request);

        // Act
        var response = await GetMediator().Send(command);

        // Assert
        response.ShouldNotBeNull();
        response.Success.ShouldBeTrue();
        response.PatientDto.PhoneNumber.ShouldBeNull();

        var reloaded = await Uow.RepositoryFor<Patient>().GetByIdAsync(response.PatientDto.Id);
        reloaded.ShouldNotBeNull();
        reloaded!.PhoneNumber.ShouldBeNull();
    }

    [TestMethod]
    public async Task Handle_Should_NormalizeEmail_ToLowerCase()
    {
        // Arrange
        var request = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "Test")
            .With(p => p.LastName = "User")
            .With(p => p.Email = "TEST.USER@EXAMPLE.COM")
            .With(p => p.DateOfBirth = new DateTime(2000, 1, 1))
            .Build();

        var command = new CreatePatientCommand(request);

        // Act
        var response = await GetMediator().Send(command);

        // Assert
        response.PatientDto.Email.ShouldBe("test.user@example.com");

        var reloaded = await Uow.RepositoryFor<Patient>().GetByIdAsync(response.PatientDto.Id);
        reloaded!.Email.ShouldBe("test.user@example.com");
    }
}
```

### Example: Query Handler Tests

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Application.Patients.Commands;
using Scheduling.Application.Patients.Queries;
using Scheduling.Domain.Patients;
using Shouldly;

namespace Scheduling.Tests.ApplicationTests.HandlerTests;

[TestClass]
public class GetPatientQueryHandlerTests : SchedulingDbTestBase
{
    [TestMethod]
    public async Task Handle_Should_ReturnPatient_WhenExists()
    {
        // Arrange - Create a patient first
        var createCommand = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@test.com",
            DateOfBirth = new DateTime(1990, 1, 1)
        });
        var createResponse = await GetMediator().Send(createCommand);
        var patientId = createResponse.PatientDto.Id;

        // Act
        var query = new GetPatientQuery { Id = patientId };
        var result = await GetMediator().Send(query);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(patientId);
        result.FirstName.ShouldBe("John");
        result.LastName.ShouldBe("Doe");
    }
}

[TestClass]
public class GetAllPatientsQueryHandlerTests : SchedulingDbTestBase
{
    [TestMethod]
    public async Task Handle_Should_ReturnAllPatients()
    {
        // Arrange - Create multiple patients
        await GetMediator().Send(new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@test.com",
            DateOfBirth = new DateTime(1990, 1, 1)
        }));

        await GetMediator().Send(new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "Jane",
            LastName = "Smith",
            Email = "jane@test.com",
            DateOfBirth = new DateTime(1985, 5, 15)
        }));

        // Act
        var query = new GetAllPatientsQuery();
        var result = await GetMediator().Send(query);

        // Assert
        result.ShouldNotBeNull();
        result.Count().ShouldBe(2);
    }

    [TestMethod]
    public async Task Handle_Should_ReturnEmptyList_WhenNoPatients()
    {
        // Act
        var query = new GetAllPatientsQuery();
        var result = await GetMediator().Send(query);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }
}
```

---

## Using NBuilder for Test Data

NBuilder helps create test data with fluent syntax:

```csharp
using FizzWare.NBuilder;

// Create a single object
var request = Builder<CreatePatientRequest>.CreateNew()
    .With(p => p.FirstName = "John")
    .With(p => p.LastName = "Doe")
    .With(p => p.Email = "john@test.com")
    .Build();

// Create a list of objects
var requests = Builder<CreatePatientRequest>.CreateListOfSize(10)
    .All()
        .With(p => p.DateOfBirth = new DateTime(1990, 1, 1))
    .TheFirst(5)
        .With(p => p.FirstName = "Active")
    .TheLast(5)
        .With(p => p.FirstName = "Inactive")
    .Build();
```

**Note:** `Entity.Id` is automatically excluded from NBuilder's auto-generation (configured in `TestBase<TContext>`).

---

## Test Patterns

### Arrange-Act-Assert

```csharp
[TestMethod]
public async Task Handle_Should_DoSomething_When_Condition()
{
    // Arrange - Set up test data
    var command = new SomeCommand { /* ... */ };

    // Act - Execute the handler
    var result = await GetMediator().Send(command);

    // Assert - Verify expectations
    result.ShouldNotBeNull();
}
```

### Testing Side Effects

```csharp
// Verify database was updated
var reloaded = await Uow.RepositoryFor<Patient>().GetByIdAsync(id);
reloaded.ShouldNotBeNull();
reloaded!.Status.ShouldBe(PatientStatus.Suspended);
```

### Testing with Pre-existing Data

```csharp
[TestMethod]
public async Task Handle_Should_UpdateExistingPatient()
{
    // Arrange - Create existing patient
    var createResponse = await GetMediator().Send(new CreatePatientCommand(/* ... */));
    var patientId = createResponse.PatientDto.Id;

    // Act - Send update command
    var updateCommand = new UpdatePatientCommand { Id = patientId, /* ... */ };
    var result = await GetMediator().Send(updateCommand);

    // Assert
    result.Success.ShouldBeTrue();
}
```

---

## Performance Considerations

Handler tests are slower than unit tests because they:
1. Create a SQLite database
2. Run migrations/schema creation
3. Execute actual SQL queries
4. Manage transactions

Keep handler tests focused on integration concerns:
- Does the handler persist data correctly?
- Does the query return expected data?
- Do complex scenarios work end-to-end?

For validation logic, use validator tests instead (faster).

---

→ Next: [05-domain-tests.md](./05-domain-tests.md)
