# Building the Patient Aggregate

## What You'll Build

```
┌─────────────────────────────────────┐
│  PATIENT (Aggregate Root)           │
│                                     │
│  Guid Id                            │
│  string FirstName                   │
│  string LastName                    │
│  string Email                       │
│  string PhoneNumber                 │
│  DateTime DateOfBirth               │
│  PatientStatus Status               │
│                                     │
└─────────────────────────────────────┘
```

For now, we'll keep it simple with primitive types. Later you can refactor if needed.

---

## Why Aggregates Have Rules

The aggregate protects its invariants (things that must always be true):

- Patient must have a name
- Email must be valid format
- Patient can't be deleted if they have upcoming appointments (future)

All changes go through methods that enforce these rules.

---

## What You Need To Do

### Step 1: Create folder structure

Inside `Scheduling.Domain/`, create:
```
Patients/
├── Patient.cs
├── PatientStatus.cs
└── Events/
    └── (empty for now)
```

### Step 2: Create PatientStatus enum

Location: `Scheduling.Domain/Patients/PatientStatus.cs`

```csharp
namespace Scheduling.Domain.Patients;

public enum PatientStatus
{
    Active,
    Inactive,
    Suspended
}
```

**Why an enum?** Status has fixed values. No need for a class.

### Step 3: Create the Patient entity

Location: `Scheduling.Domain/Patients/Patient.cs`

```csharp
namespace Scheduling.Domain.Patients;

public class Patient
{
    public Guid Id { get; private set; }
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public string Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public DateTime DateOfBirth { get; private set; }
    public PatientStatus Status { get; private set; }

    // EF Core needs this
    private Patient() { }

    // Factory method - the only way to create a Patient
    public static Patient Create(
        string firstName,
        string lastName,
        string email,
        DateTime dateOfBirth,
        string? phoneNumber = null)
    {
        // Validation - enforce invariants
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required", nameof(firstName));

        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name is required", nameof(lastName));

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required", nameof(email));

        if (!email.Contains('@'))
            throw new ArgumentException("Invalid email format", nameof(email));

        if (dateOfBirth > DateTime.UtcNow)
            throw new ArgumentException("Date of birth cannot be in the future", nameof(dateOfBirth));

        return new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            PhoneNumber = phoneNumber?.Trim(),
            DateOfBirth = dateOfBirth,
            Status = PatientStatus.Active
        };
    }

    // Behavior methods - how the entity changes
    public void UpdateContactInfo(string email, string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required", nameof(email));

        if (!email.Contains('@'))
            throw new ArgumentException("Invalid email format", nameof(email));

        Email = email.Trim().ToLowerInvariant();
        PhoneNumber = phoneNumber?.Trim();
    }

    public void Suspend()
    {
        if (Status == PatientStatus.Suspended)
            return; // Already suspended, no-op

        Status = PatientStatus.Suspended;
    }

    public void Activate()
    {
        if (Status == PatientStatus.Active)
            return;

        Status = PatientStatus.Active;
    }

    public void Deactivate()
    {
        if (Status == PatientStatus.Inactive)
            return;

        Status = PatientStatus.Inactive;
    }
}
```

### Step 4: Understand What You Just Built

**Private setters:**
```csharp
public string Email { get; private set; }
```
Nobody outside can do `patient.Email = "whatever"`. They must use `UpdateContactInfo()`.

**Factory method:**
```csharp
public static Patient Create(...) { }
```
The only way to create a valid Patient. Constructor is private.

**Behavior methods:**
```csharp
public void Suspend() { }
public void UpdateContactInfo(...) { }
```
All changes go through methods that enforce business rules.

**EF Core constructor:**
```csharp
private Patient() { }
```
EF Core needs a parameterless constructor to materialize objects from the database. It's private so your code can't use it.

---

## Step 5: Write a Test

Location: `tests/Scheduling.Domain.Tests/Patients/PatientTests.cs`

Create the folder `Patients/` first, then:

```csharp
using Scheduling.Domain.Patients;
using FluentAssertions;

namespace Scheduling.Domain.Tests.Patients;

public class PatientTests
{
    [Fact]
    public void Create_WithValidData_ShouldCreatePatient()
    {
        // Arrange
        var firstName = "John";
        var lastName = "Doe";
        var email = "john.doe@example.com";
        var dateOfBirth = new DateTime(1990, 1, 15);

        // Act
        var patient = Patient.Create(firstName, lastName, email, dateOfBirth);

        // Assert
        patient.Id.Should().NotBeEmpty();
        patient.FirstName.Should().Be("John");
        patient.LastName.Should().Be("Doe");
        patient.Email.Should().Be("john.doe@example.com");
        patient.Status.Should().Be(PatientStatus.Active);
    }

    [Fact]
    public void Create_WithEmptyFirstName_ShouldThrow()
    {
        // Arrange & Act
        var act = () => Patient.Create("", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("firstName");
    }

    [Fact]
    public void Create_WithInvalidEmail_ShouldThrow()
    {
        // Arrange & Act
        var act = () => Patient.Create("John", "Doe", "invalid-email", DateTime.UtcNow.AddYears(-30));

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("email");
    }

    [Fact]
    public void Suspend_ShouldChangeStatusToSuspended()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));

        // Act
        patient.Suspend();

        // Assert
        patient.Status.Should().Be(PatientStatus.Suspended);
    }

    [Fact]
    public void UpdateContactInfo_WithValidEmail_ShouldUpdateEmail()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "old@example.com", DateTime.UtcNow.AddYears(-30));

        // Act
        patient.UpdateContactInfo("new@example.com", null);

        // Assert
        patient.Email.Should().Be("new@example.com");
    }
}
```

### Step 6: Add FluentAssertions to test project

```bash
cd AGFA/tests/Scheduling.Domain.Tests
dotnet add package FluentAssertions
```

### Step 7: Run Tests

```bash
cd C:/projects/ddd/AGFA
dotnet test
```

All tests should pass.

---

## Verification Checklist

- [ ] `Patient.cs` has private setters
- [ ] `Patient.Create()` validates all inputs
- [ ] No public constructor (only private + factory method)
- [ ] Behavior methods (`Suspend`, `Activate`, etc.) exist
- [ ] Tests pass
- [ ] Solution builds

---

## What You Learned

1. **Encapsulation** - Private setters, changes through methods only
2. **Factory method** - Ensures valid objects are created
3. **Invariant enforcement** - Validation in the domain, not scattered elsewhere
4. **Behavior in the entity** - Not in external services

---

## Note on Validation

We're using simple `ArgumentException` for now. Later you could:
- Use a Result pattern instead of exceptions
- Create domain-specific exceptions (`InvalidEmailException`)
- Rely more on command validators for input validation

For learning, exceptions are fine. The key concept is: **the entity protects itself**.

→ Next: [04-domain-events.md](./04-domain-events.md) - Raising events when things happen
