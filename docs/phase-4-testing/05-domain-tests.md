# Domain Tests

## Overview

Domain tests are **pure unit tests** that verify entity behavior without any database or framework dependencies. They are:
- The fastest tests to run
- The most important tests (domain logic is your core business value)
- Framework-agnostic (no base class needed)

---

## What to Test

| Aspect | Examples |
|--------|----------|
| **Factory methods** | `Patient.Create()` returns valid entity |
| **State transitions** | `Suspend()` changes status |
| **Business rules** | Can't suspend an already suspended patient |
| **Invariant protection** | Required fields validated |

---

## Test File Location

```
Core/Scheduling/Scheduling.Domain.Tests/
└── DomainTests/
    └── Patients/
        └── PatientTests.cs
```

---

## Example: PatientTests

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Domain.Patients;
using Shouldly;

namespace Scheduling.Tests.DomainTests.Patients;

[TestClass]
public class PatientTests
{
    #region Create Tests

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
        patient.DateOfBirth.ShouldBe(new DateTime(1990, 1, 15));
    }

    [TestMethod]
    public void Create_ShouldSetStatusToActive()
    {
        // Act
        var patient = Patient.Create("John", "Doe", "john@test.com", new DateTime(1990, 1, 15));

        // Assert
        patient.Status.ShouldBe(PatientStatus.Active);
    }

    [TestMethod]
    public void Create_ShouldGenerateUniqueId()
    {
        // Act
        var patient1 = Patient.Create("John", "Doe", "john@test.com", new DateTime(1990, 1, 15));
        var patient2 = Patient.Create("Jane", "Smith", "jane@test.com", new DateTime(1985, 6, 20));

        // Assert
        patient1.Id.ShouldNotBe(patient2.Id);
    }

    #endregion

    #region Suspend Tests

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

    [TestMethod]
    public void Suspend_ShouldBeIdempotent()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "john@test.com", new DateTime(1990, 1, 15));

        // Act - Suspend twice
        patient.Suspend();
        patient.Suspend();

        // Assert - Should still be suspended, no exception
        patient.Status.ShouldBe(PatientStatus.Suspended);
    }

    #endregion

    #region Activate Tests

    [TestMethod]
    public void Activate_ShouldChangeStatusToActive()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "john@test.com", new DateTime(1990, 1, 15));
        patient.Suspend();

        // Act
        patient.Activate();

        // Assert
        patient.Status.ShouldBe(PatientStatus.Active);
    }

    [TestMethod]
    public void Activate_ShouldBeIdempotent()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "john@test.com", new DateTime(1990, 1, 15));

        // Act - Already active, activate again
        patient.Activate();

        // Assert - Should still be active, no exception
        patient.Status.ShouldBe(PatientStatus.Active);
    }

    #endregion

    #region Update Tests

    [TestMethod]
    public void Update_ShouldChangePatientDetails()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "john@test.com", new DateTime(1990, 1, 15));

        // Act
        patient.Update("Jonathan", "Doe-Smith", "jonathan@test.com", "+1234567890");

        // Assert
        patient.FirstName.ShouldBe("Jonathan");
        patient.LastName.ShouldBe("Doe-Smith");
        patient.Email.ShouldBe("jonathan@test.com");
        patient.PhoneNumber.ShouldBe("+1234567890");
    }

    #endregion
}
```

---

## Testing Invariants

Entities should protect their invariants:

```csharp
[TestMethod]
public void Create_ShouldThrow_WhenFirstNameIsEmpty()
{
    // Act & Assert
    Should.Throw<ArgumentException>(() =>
        Patient.Create("", "Doe", "john@test.com", new DateTime(1990, 1, 15)));
}

[TestMethod]
public void Create_ShouldThrow_WhenEmailIsInvalid()
{
    // Act & Assert
    Should.Throw<ArgumentException>(() =>
        Patient.Create("John", "Doe", "not-an-email", new DateTime(1990, 1, 15)));
}
```

---

## Adding a New Bounded Context

To add testing for a new bounded context (e.g., Billing):

### 1. Create Test Project

```bash
dotnet new mstest -n Billing.Tests -o Core/Billing/Billing.Tests
dotnet sln add Core/Billing/Billing.Tests
```

### 2. Add References

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\..\BuildingBlocks\BuildingBlocks.Tests\BuildingBlocks.Tests.csproj" />
  <ProjectReference Include="..\Billing.Domain\Billing.Domain.csproj" />
  <ProjectReference Include="..\Billing.Application\Billing.Application.csproj" />
  <ProjectReference Include="..\Billing.Infrastructure\Billing.Infrastructure.csproj" />
</ItemGroup>
```

### 3. Create Test Bases

```csharp
// BillingValidatorTestBase.cs - for validator unit tests
public abstract class BillingValidatorTestBase : ValidatorTestBase
{
    protected Mock<IRepository<Invoice>> InvoiceRepositoryMock { get; private set; } = null!;

    protected override void RegisterServices(IServiceCollection services)
    {
        services.AddBillingApplication();

        InvoiceRepositoryMock = new Mock<IRepository<Invoice>>();
        UnitOfWorkMock.Setup(u => u.RepositoryFor<Invoice>()).Returns(InvoiceRepositoryMock.Object);
    }

    protected void SetupInvoiceExists(Guid invoiceId)
    {
        InvoiceRepositoryMock
            .Setup(r => r.ExistsAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
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

### 4. Create Test Structure

```
Core/Billing/Billing.Tests/
├── BillingValidatorTestBase.cs
├── BillingDbTestBase.cs
├── ApplicationTests/
│   ├── HandlerTests/
│   │   └── CreateInvoiceCommandHandlerTests.cs
│   └── ValidatorTests/
│       └── CreateInvoiceCommandValidatorTests.cs
└── DomainTests/
    └── Invoices/
        └── InvoiceTests.cs
```

---

## Best Practices

1. **Test behavior, not implementation** - Focus on what the entity does, not how
2. **Use descriptive test names** - `Create_ShouldSetStatusToActive` tells you exactly what's being tested
3. **One assertion concept per test** - Multiple assertions are fine if they verify the same concept
4. **Keep tests independent** - Each test should set up its own data
5. **Test edge cases** - Empty strings, null values, boundary conditions
6. **Don't mock the entity** - Test the real entity, only mock dependencies

---

## Running Tests

```bash
# Run all tests
dotnet test

# Run tests with output
dotnet test --logger "console;verbosity=detailed"

# Run specific test class
dotnet test --filter "FullyQualifiedName~PatientTests"

# Run specific test method
dotnet test --filter "Name=Create_ShouldCreatePatientWithCorrectValues"
```

---

## Summary

| Test Type | Base Class | Speed | Purpose |
|-----------|------------|-------|---------|
| Domain | None | Fastest | Entity behavior, business rules |
| Validator | `SchedulingValidatorTestBase` | Fast | Input validation |
| Handler | `SchedulingDbTestBase` | Medium | Full pipeline integration |

→ Continue to Phase 5: Event-Driven Architecture (when ready)
