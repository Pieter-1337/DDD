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

## Why Aggregates Encapsulate Behavior

The aggregate encapsulates **behavior** and **state transitions**:

- How a patient gets suspended
- How contact info gets updated
- State changes that have business meaning

**Note:** Input validation (required fields, email format) is handled by FluentValidation in the Application layer. The domain focuses on behavior, not gatekeeping.

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

### Step 2: Set up SmartEnum infrastructure

We use **Ardalis.SmartEnum** instead of C# enums. SmartEnum provides:
- Type safety (can't pass invalid values)
- Associated data (like error messages on error codes)
- Consistent pattern across domain and application layers

#### 2a: Add SmartEnum packages to `Directory.Packages.props`

```xml
<ItemGroup>
  <!-- Enumerations -->
  <PackageVersion Include="Ardalis.SmartEnum" Version="8.1.0" />
  <PackageVersion Include="Ardalis.SmartEnum.SystemTextJson" Version="8.1.0" />
  <PackageVersion Include="Ardalis.SmartEnum.EFCore" Version="8.1.0" />
</ItemGroup>
```

#### 2b: Create `BuildingBlocks.Enumerations` project

This project holds shared enumerations that can be used across layers (Domain, Application, Infrastructure).

```bash
dotnet new classlib -n BuildingBlocks.Enumerations -o BuildingBlocks/BuildingBlocks.Enumerations
dotnet sln add BuildingBlocks/BuildingBlocks.Enumerations
```

Add the package reference to `BuildingBlocks.Enumerations.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Ardalis.SmartEnum" />
</ItemGroup>
```

Add project reference from `BuildingBlocks.Domain.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\BuildingBlocks.Enumerations\BuildingBlocks.Enumerations.csproj" />
</ItemGroup>
```

#### 2c: Create SmartEnumJsonConverterFactory for JSON serialization

This factory creates JSON converters for all SmartEnum types, serializing them by name (e.g., `"Active"` instead of `1`).

Create `BuildingBlocks.WebApplications` project if it doesn't exist:

```bash
dotnet new classlib -n BuildingBlocks.WebApplications -o BuildingBlocks/BuildingBlocks.WebApplications
dotnet sln add BuildingBlocks/BuildingBlocks.WebApplications
```

Add packages to `BuildingBlocks.WebApplications.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Ardalis.SmartEnum" />
  <PackageReference Include="Ardalis.SmartEnum.SystemTextJson" />
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

Location: `BuildingBlocks/BuildingBlocks.WebApplications/Json/SmartEnumJsonConverterFactory.cs`

```csharp
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ardalis.SmartEnum;
using Ardalis.SmartEnum.SystemTextJson;

namespace BuildingBlocks.WebApplications.Json;

public class SmartEnumJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return IsSmartEnum(typeToConvert, out _, out _);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (!IsSmartEnum(typeToConvert, out var enumType, out var valueType))
            return null;

        var converterType = typeof(SmartEnumNameConverter<,>).MakeGenericType(enumType!, valueType!);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }

    private static bool IsSmartEnum(Type type, out Type? enumType, out Type? valueType)
    {
        enumType = null;
        valueType = null;

        var baseType = type;
        while (baseType != null)
        {
            if (baseType.IsGenericType)
            {
                var genericDef = baseType.GetGenericTypeDefinition();
                if (genericDef == typeof(SmartEnum<>) || genericDef == typeof(SmartEnum<,>))
                {
                    var args = baseType.GetGenericArguments();
                    enumType = args[0];
                    valueType = args.Length > 1 ? args[1] : typeof(int);
                    return true;
                }
            }
            baseType = baseType.BaseType;
        }
        return false;
    }
}
```

#### 2d: Configure JSON serialization in Program.cs

Update `WebApi/Program.cs` to use the SmartEnum converter:

```csharp
using BuildingBlocks.WebApplications.Json;

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new SmartEnumJsonConverterFactory());
    });
```

**Why a custom factory?**
- Handles all SmartEnum types generically (no need to register each one)
- Serializes by name (`"Active"`) for readability in API responses
- Works with SmartEnum<T> (int value) and SmartEnum<T, TValue> (custom value type)

### Step 3: Create PatientStatus

Location: `Scheduling.Domain/Patients/PatientStatus.cs`

```csharp
using Ardalis.SmartEnum;

namespace Scheduling.Domain.Patients;

public sealed class PatientStatus : SmartEnum<PatientStatus>
{
    public static readonly PatientStatus Active = new(nameof(Active), 1);
    public static readonly PatientStatus Inactive = new(nameof(Inactive), 2);
    public static readonly PatientStatus Suspended = new(nameof(Suspended), 3);

    private PatientStatus(string name, int value) : base(name, value) { }
}
```

**Why SmartEnum over C# enum?**
- **Type safety**: Can't cast an invalid int to PatientStatus
- **Consistency**: Same pattern for domain status enums and error codes
- **Extensibility**: Can add properties (e.g., `ErrorCode` has a `Message` property)
- **JSON serialization**: Works cleanly with a generic converter factory

### Step 4: Create the Patient entity

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
        // No validation here - FluentValidation handles input validation
        // Domain focuses on constructing a valid object
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

    // Behavior methods - how the entity changes state
    public void UpdateContactInfo(string email, string? phoneNumber)
    {
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

### Step 5: Understand What You Just Built

**Private setters:**
```csharp
public string Email { get; private set; }
```
Nobody outside can do `patient.Email = "whatever"`. They must use `UpdateContactInfo()`.

**Factory method:**
```csharp
public static Patient Create(...) { }
```
The only way to create a Patient. Constructor is private.

**Behavior methods:**
```csharp
public void Suspend() { }
public void UpdateContactInfo(...) { }
```
All state changes go through methods that encapsulate the behavior. These methods handle state transitions, not input validation.

**EF Core constructor:**
```csharp
private Patient() { }
```
EF Core needs a parameterless constructor to materialize objects from the database. It's private so your code can't use it.

---

## Step 6: Write a Test

Location: `tests/Scheduling.Domain.Tests/Patients/PatientTests.cs`

Create the folder `Patients/` first, then:

```csharp
using Scheduling.Domain.Patients;
using FluentAssertions;

namespace Scheduling.Domain.Tests.Patients;

public class PatientTests
{
    [Fact]
    public void Create_ShouldCreatePatientWithCorrectValues()
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
    public void Suspend_WhenAlreadySuspended_ShouldRemainSuspended()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));
        patient.Suspend();

        // Act
        patient.Suspend(); // Call again

        // Assert
        patient.Status.Should().Be(PatientStatus.Suspended);
    }

    [Fact]
    public void UpdateContactInfo_ShouldUpdateEmail()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "old@example.com", DateTime.UtcNow.AddYears(-30));

        // Act
        patient.UpdateContactInfo("new@example.com", null);

        // Assert
        patient.Email.Should().Be("new@example.com");
    }

    [Fact]
    public void Activate_WhenSuspended_ShouldChangeStatusToActive()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));
        patient.Suspend();

        // Act
        patient.Activate();

        // Assert
        patient.Status.Should().Be(PatientStatus.Active);
    }
}
```

**Note:** Tests focus on **behavior** (state transitions), not input validation. Input validation is tested in FluentValidation validator tests.

### Step 7: Add FluentAssertions to test project

```bash
cd DDD/tests/Scheduling.Domain.Tests
dotnet add package FluentAssertions
```

### Step 8: Run Tests

```bash
cd C:/projects/ddd/DDD
dotnet test
```

All tests should pass.

---

## Verification Checklist

- [ ] `BuildingBlocks.Enumerations` project created with `Ardalis.SmartEnum` package
- [ ] `BuildingBlocks.Domain` references `BuildingBlocks.Enumerations`
- [ ] `BuildingBlocks.WebApplications` project created with `SmartEnumJsonConverterFactory`
- [ ] `SmartEnumJsonConverterFactory` registered in `Program.cs`
- [ ] `PatientStatus` uses SmartEnum (not C# enum)
- [ ] `Patient.cs` has private setters
- [ ] `Patient.Create()` factory method exists
- [ ] No public constructor (only private + factory method)
- [ ] Behavior methods (`Suspend`, `Activate`, etc.) exist
- [ ] Behavior tests pass
- [ ] Solution builds

---

## What You Learned

1. **SmartEnum** - Type-safe enumerations with Ardalis.SmartEnum
2. **Encapsulation** - Private setters, changes through methods only
3. **Factory method** - Controlled way to create entities
4. **Behavior in the entity** - State transitions encapsulated, not in external services
5. **Separation of concerns** - Domain handles behavior, FluentValidation handles input validation

---

## Note on Validation

**Input validation** (required fields, email format, etc.) is handled by **FluentValidation** in the Application layer. This gives you:
- Custom error messages
- Async validation (external API calls)
- Reusable validators

**Domain behavior** (state transitions) stays in the entity:
- `Suspend()`, `Activate()`, `Deactivate()`
- Business rules like "can't suspend an already suspended patient"

This separation keeps the domain focused on **behavior**, not gatekeeping.

**Business preconditions** (e.g., "patient must exist", "patient can't already be suspended") are checked in validators before calling domain methods.

→ Next: [04-domain-events.md](./04-domain-events.md) - Raising events when things happen
