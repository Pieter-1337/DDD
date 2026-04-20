# User Context and Authorization

> **Previous**: [05-angular-auth.md](./05-angular-auth.md)

---

## Overview

This document completes Phase 8 by implementing the **user context abstraction** and **role-based authorization** in the application layer. We'll activate the previously commented-out `UserValidator<T>` base class to provide declarative role-based validation in command validators.

**What you'll learn**:
- How to abstract current user information from HTTP context into domain-friendly interfaces
- Why user context lives in the Application layer (not Domain or Infrastructure)
- How to implement flexible AND/OR role validation logic
- How to integrate user context into command handlers for audit trails
- Testing strategies for role-based validation

**Architecture principle**: The Application layer depends on abstractions. Domain logic should never directly depend on HTTP or authentication concepts. Infrastructure provides concrete implementations of these abstractions.

---

## 1. The ICurrentUser Abstraction

### Why This Interface Exists

In a Clean Architecture/DDD application, layers have specific responsibilities:

```
┌─────────────────────────────────────────┐
│           Domain Layer                  │
│  (Business logic, aggregates, events)   │ ← No auth dependencies
└─────────────────────────────────────────┘
              ↑
┌─────────────────────────────────────────┐
│        Application Layer                │
│  (Commands, queries, validators)        │ ← Depends on ICurrentUser
└─────────────────────────────────────────┘
              ↑
┌─────────────────────────────────────────┐
│       Infrastructure Layer              │
│  (HttpContextCurrentUser implements)    │ ← Reads from HTTP context
└─────────────────────────────────────────┘
```

**The problem**: Command handlers and validators need to know who the current user is (for authorization and audit), but they can't depend on `HttpContext` directly because:
- Application layer shouldn't depend on ASP.NET Core
- Unit tests would require mocking the entire HTTP pipeline
- Domain concepts (like "current user") shouldn't leak infrastructure details

**The solution**: Define an abstraction in the Application layer that Infrastructure implements.

### Interface Definition

Create the interface in the BuildingBlocks Application layer:

```csharp
// BuildingBlocks/BuildingBlocks.Application/Abstractions/ICurrentUser.cs
namespace BuildingBlocks.Application.Abstractions;

/// <summary>
/// Abstraction for accessing the current authenticated user's information.
/// This interface is implemented by infrastructure (e.g., HttpContextCurrentUser)
/// and consumed by application layer services (validators, handlers).
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// The unique identifier of the current user (from "sub" claim).
    /// Null if not authenticated.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// The email address of the current user (from "email" claim).
    /// Null if not authenticated or email claim not present.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// The display name of the current user (from "name" claim).
    /// Null if not authenticated or name claim not present.
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// The roles assigned to the current user (from "role" claims).
    /// Empty collection if not authenticated or no roles assigned.
    /// </summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>
    /// Indicates whether the current user is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Checks if the current user has a specific role.
    /// </summary>
    /// <param name="role">The role name to check.</param>
    /// <returns>True if the user has the role, false otherwise.</returns>
    bool HasRole(string role) => Roles.Contains(role);
}
```

**Key design decisions**:
- **Read-only properties**: This is a query interface, not a command interface
- **Nullable strings**: The user might not be authenticated, or claims might be missing
- **IReadOnlyList for Roles**: Prevents modification, communicates intent
- **Default implementation of HasRole**: Convenience method, can be overridden if needed
- **No business logic**: This is pure data access, no validation or transformation

### Claims-to-Interface Mapping

The implementation (already created in doc 03) maps OIDC claims to interface properties:

| OIDC Claim | Cookie Claim Type | ICurrentUser Property | Domain Usage |
|------------|-------------------|----------------------|--------------|
| `sub` | `ClaimTypes.NameIdentifier` | `UserId` | Audit logging, ownership checks |
| `email` | `ClaimTypes.Email` | `Email` | Notifications, user identification |
| `name` | `ClaimTypes.Name` | `Name` | Display in UI, logging |
| `role` | `ClaimTypes.Role` | `Roles` | Authorization decisions |
| N/A | `User.Identity?.IsAuthenticated` | `IsAuthenticated` | Authentication gate |

---

## 2. HttpContextCurrentUser Implementation

The implementation was created in [03-infrastructure-auth.md](./03-infrastructure-auth.md). Here's a quick recap:

```csharp
// BuildingBlocks/BuildingBlocks.Infrastructure.Auth/HttpContextCurrentUser.cs
namespace BuildingBlocks.Infrastructure.Auth;

public class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? UserId =>
        _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public string? Email =>
        _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value;

    public string? Name =>
        _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value;

    public IReadOnlyList<string> Roles =>
        _httpContextAccessor.HttpContext?.User?
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList() ?? new List<string>();

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}
```

**Registered as Scoped** in `AddOidcCookieAuth()`:
```csharp
services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
```

Why Scoped? Because it depends on `IHttpContextAccessor`, which is scoped to the HTTP request.

---

## 3. Activating UserValidator<T>

The `UserValidator<T>` base class has been dormant with commented-out code. It's time to bring it to life with our new `ICurrentUser` abstraction.

### The Transformation

**BEFORE** (commented out, using old `IUserContext`):

```csharp
// BuildingBlocks/BuildingBlocks.Application/Validators/UserValidator.cs
public abstract class UserValidator<T> : AbstractValidator<T>
{
    //protected readonly IUserContext UserContext;
    //private IEnumerable<IEnumerable<string>> _allowedRoleGroups;

    //protected UserValidator(IUserContext userContext, params IEnumerable<string>[] allowedRoleGroups)
    //{
    //    UserContext = userContext;
    //    _allowedRoleGroups = allowedRoleGroups.Where(g => g != null && g.Any(role => !role.IsNullOrWhitespace()))
    //        .Select(g => g.Where(role => !role.IsNullOrWhitespace()));
    //}

    //protected IRuleBuilderOptions<T, T> RuleForUserValidation()
    //{
    //    return RuleFor(r => r)
    //        .Must(r => HaveAValidRole(_allowedRoleGroups))
    //            .WithMessage(m => Resources.ValidationMessages.role_notallowed)
    //            .WithErrorCode(Resources.ValidationCodes.http_403_forbidden);
    //}

    //private bool HaveAValidRole(IEnumerable<IEnumerable<string>> allowedRoleGroups)
    //{
    //    if (!allowedRoleGroups.Any())
    //        return true;
    //    foreach (var allowedRoleGroup in allowedRoleGroups)
    //    {
    //        if (allowedRoleGroup.All(role => UserContext.HasApplicationRole(role)))
    //            return true;
    //    }
    //    return false;
    //}
}
```

**AFTER** (activated with `ICurrentUser`):

```csharp
// BuildingBlocks/BuildingBlocks.Application/Validators/UserValidator.cs
using FluentValidation;
using BuildingBlocks.Application.Abstractions;

namespace BuildingBlocks.Application.Validators;

/// <summary>
/// Base validator that provides role-based authorization validation.
/// Validators can specify allowed role groups using AND/OR logic.
/// </summary>
/// <typeparam name="T">The type being validated (typically a command or query).</typeparam>
public abstract class UserValidator<T> : AbstractValidator<T>
{
    /// <summary>
    /// The current authenticated user. Available to derived validators for custom logic.
    /// </summary>
    protected readonly ICurrentUser CurrentUser;

    /// <summary>
    /// The allowed role groups for this validator.
    /// Outer collection is OR, inner collection is AND.
    /// Example: [["Admin"], ["Doctor", "Nurse"]] means "Admin" OR ("Doctor" AND "Nurse").
    /// </summary>
    private readonly IEnumerable<IEnumerable<string>> _allowedRoleGroups;

    /// <summary>
    /// Constructor for validators that require role-based validation.
    /// </summary>
    /// <param name="currentUser">The current user context.</param>
    /// <param name="allowedRoleGroups">
    /// One or more role groups. A user must have ALL roles in at least ONE group to pass validation.
    /// Example: new[] { "Admin" }, new[] { "Doctor", "Nurse" } means "Admin" OR ("Doctor" AND "Nurse").
    /// </param>
    protected UserValidator(ICurrentUser currentUser, params IEnumerable<string>[] allowedRoleGroups)
    {
        CurrentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));

        // Filter out null or empty role groups
        _allowedRoleGroups = allowedRoleGroups
            .Where(g => g != null && g.Any(role => !string.IsNullOrWhiteSpace(role)))
            .Select(g => g.Where(role => !string.IsNullOrWhiteSpace(role)))
            .ToList();
    }

    /// <summary>
    /// Parameterless constructor for validators that don't require role checks.
    /// Use this when any authenticated user can perform the action.
    /// </summary>
    protected UserValidator(ICurrentUser currentUser)
    {
        CurrentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _allowedRoleGroups = Enumerable.Empty<IEnumerable<string>>();
    }

    /// <summary>
    /// Creates a validation rule that checks if the current user has the required roles.
    /// Call this method in the derived validator's constructor to enforce role-based authorization.
    /// </summary>
    /// <returns>A FluentValidation rule builder for chaining.</returns>
    protected IRuleBuilderOptions<T, T> RuleForUserValidation()
    {
        return RuleFor(r => r)
            .Must(_ => HaveAValidRole())
                .WithMessage("You do not have the required role to perform this action.")
                .WithErrorCode("ERR_FORBIDDEN");
    }

    /// <summary>
    /// Checks if the current user satisfies at least one role group.
    /// </summary>
    /// <returns>True if user has valid roles or no role groups are defined.</returns>
    private bool HaveAValidRole()
    {
        // If no role groups are defined, allow access (any authenticated user can perform the action)
        if (!_allowedRoleGroups.Any())
            return true;

        // User must satisfy ALL roles in at least ONE group
        foreach (var group in _allowedRoleGroups)
        {
            if (group.All(role => CurrentUser.HasRole(role)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the allowed role groups for this validator (useful for testing or diagnostics).
    /// </summary>
    public IEnumerable<IEnumerable<string>> GetAllowedRoleGroups() => _allowedRoleGroups;

    /// <summary>
    /// Sets the allowed role groups dynamically (useful for testing or conditional logic).
    /// </summary>
    public void SetAllowedRoleGroups(params IEnumerable<string>[] allowedRoleGroups)
    {
        // This would require making _allowedRoleGroups mutable
        // Consider if this is needed in your application
        throw new NotImplementedException(
            "Dynamic role group modification is not supported. Define roles in constructor.");
    }
}
```

**Key improvements**:
- **Null safety**: ArgumentNullException on currentUser
- **Clear XML docs**: Explains the AND/OR pattern with examples
- **Two constructors**: One for role-based validation, one for "any authenticated user"
- **Better method names**: `HaveAValidRole()` (no parameter, uses field)
- **No resource files**: Hard-coded messages (you can add resource files later)

---

## 4. Understanding the AND/OR Role Pattern

The role validation logic supports complex scenarios using a two-level collection:

```
params IEnumerable<string>[] allowedRoleGroups
        ↑                              ↑
    Outer array                  Inner collection
      (OR logic)                   (AND logic)
```

### Examples

**Scenario 1: Only Admins can delete patients**

```csharp
public class DeletePatientCommandValidator : UserValidator<DeletePatientCommand>
{
    public DeletePatientCommandValidator(
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork)
        : base(currentUser, new[] { "Admin" })  // Single role group with one role
    {
        RuleForUserValidation();  // Enforce role check

        RuleFor(x => x.PatientId)
            .NotEmpty()
            .MustAsync(async (id, ct) => await unitOfWork.RepositoryFor<Patient>().ExistsAsync(id, ct))
            .WithMessage("Patient not found.");
    }
}
```

Logic: User must have "Admin" role.

**Scenario 2: Admins OR Doctors can suspend patients**

```csharp
public class SuspendPatientCommandValidator : UserValidator<SuspendPatientCommand>
{
    public SuspendPatientCommandValidator(
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork)
        : base(currentUser, new[] { "Admin" }, new[] { "Doctor" })  // Two role groups
    {
        RuleForUserValidation();

        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
```

Logic: User must have "Admin" OR "Doctor" role.

**Scenario 3: Complex - Admin OR (Doctor AND NurseManager)**

```csharp
public class CriticalActionCommandValidator : UserValidator<CriticalActionCommand>
{
    public CriticalActionCommandValidator(
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork)
        : base(currentUser,
               new[] { "Admin" },                    // Group 1: Admin alone
               new[] { "Doctor", "NurseManager" })   // Group 2: Doctor AND NurseManager
    {
        RuleForUserValidation();
        // ... other rules
    }
}
```

Logic: User must have "Admin" OR (both "Doctor" AND "NurseManager").

**Scenario 4: Any authenticated user (no role restrictions)**

```csharp
public class CreatePatientCommandValidator : UserValidator<CreatePatientCommand>
{
    public CreatePatientCommandValidator(
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork)
        : base(currentUser)  // Parameterless constructor, no role groups
    {
        // Don't call RuleForUserValidation() — no role check needed

        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MustAsync(async (email, ct) =>
                !await unitOfWork.RepositoryFor<Patient>().AnyAsync(p => p.Email == email, ct))
            .WithMessage("A patient with this email already exists.");
    }
}
```

Logic: Any authenticated user can create patients. Authentication is enforced by the `[Authorize]` attribute on the controller, but no specific role is required.

### Validation Flow Diagram

```
                    Request with command
                            ↓
              ┌─────────────────────────┐
              │  MediatR Pipeline       │
              │  (ValidationBehavior)   │
              └─────────────────────────┘
                            ↓
              ┌─────────────────────────┐
              │  DeletePatientCommand   │
              │       Validator         │
              └─────────────────────────┘
                            ↓
              ┌─────────────────────────┐
              │  RuleForUserValidation  │
              │      (called in ctor)   │
              └─────────────────────────┘
                            ↓
              ┌─────────────────────────┐
              │   HaveAValidRole()?     │
              └─────────────────────────┘
                     ↙            ↘
                  YES              NO
                   ↓                ↓
         Continue validation   Return error
            (other rules)      "ERR_FORBIDDEN"
```

---

## 5. Using ICurrentUser in Command Handlers

Beyond validation, command handlers can use `ICurrentUser` for audit trails, ownership checks, and business logic.

### Example: Audit Trail

```csharp
// Scheduling/Scheduling.Application/Commands/CreateAppointment/CreateAppointmentCommandHandler.cs
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.EfCore;
using MediatR;
using Scheduling.Domain.Aggregates.Appointments;
using Scheduling.Domain.Aggregates.Patients;

namespace Scheduling.Application.Commands.CreateAppointment;

public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Guid>
{
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _currentUser;

    public CreateAppointmentCommandHandler(IUnitOfWork uow, ICurrentUser currentUser)
    {
        _uow = uow;
        _currentUser = currentUser;
    }

    public async Task<Guid> Handle(CreateAppointmentCommand cmd, CancellationToken ct)
    {
        // Verify patient exists
        var patientExists = await _uow.RepositoryFor<Patient>().ExistsAsync(cmd.PatientId, ct);
        if (!patientExists)
            throw new InvalidOperationException($"Patient {cmd.PatientId} not found.");

        // Create appointment
        var appointment = Appointment.Create(
            cmd.PatientId,
            cmd.DoctorId,
            cmd.AppointmentDateTime,
            cmd.DurationMinutes);

        // Add audit information using current user
        // (In a real app, you might have an AuditableEntity base class)
        // For now, we'll use domain events or a separate audit log

        _uow.RepositoryFor<Appointment>().Add(appointment);

        // Queue integration event with creator information
        _uow.QueueIntegrationEvent(new AppointmentCreatedIntegrationEvent(
            appointment.Id,
            cmd.PatientId,
            cmd.DoctorId,
            cmd.AppointmentDateTime,
            cmd.DurationMinutes,
            CreatedBy: _currentUser.UserId,  // Audit: who created this
            CreatedByName: _currentUser.Name));

        await _uow.SaveChangesAsync(ct);
        return appointment.Id;
    }
}
```

### Example: Ownership Check

```csharp
// Example: Only allow users to update their own patient profile
public class UpdateMyPatientProfileCommandHandler : IRequestHandler<UpdateMyPatientProfileCommand>
{
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _currentUser;

    public async Task Handle(UpdateMyPatientProfileCommand cmd, CancellationToken ct)
    {
        var patient = await _uow.RepositoryFor<Patient>().GetByIdAsync(cmd.PatientId, ct);
        if (patient == null)
            throw new InvalidOperationException("Patient not found.");

        // Ownership check: Patient's UserId must match current user
        // (Assuming Patient has a UserId property linking to the identity system)
        if (patient.UserId != _currentUser.UserId)
            throw new UnauthorizedAccessException("You can only update your own profile.");

        patient.UpdateContactInfo(cmd.Email, cmd.PhoneNumber);
        await _uow.SaveChangesAsync(ct);
    }
}
```

---

## 6. Testing Role-Based Validation

Testing validators with role logic requires mocking `ICurrentUser`. Here's how to do it with Moq and Shouldly.

### Test Setup

```csharp
// Scheduling/Scheduling.Domain.Tests/Validators/DeletePatientCommandValidatorTests.cs
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.EfCore;
using Moq;
using Scheduling.Application.Commands.DeletePatient;
using Scheduling.Domain.Aggregates.Patients;
using Shouldly;

namespace Scheduling.Domain.Tests.Validators;

[TestClass]
public class DeletePatientCommandValidatorTests
{
    private Mock<ICurrentUser> _mockCurrentUser = null!;
    private Mock<IUnitOfWork> _mockUow = null!;
    private DeletePatientCommandValidator _validator = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockCurrentUser = new Mock<ICurrentUser>();
        _mockUow = new Mock<IUnitOfWork>();

        // Default: authenticated admin user
        _mockCurrentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _mockCurrentUser.Setup(u => u.UserId).Returns("test-user-id");
        _mockCurrentUser.Setup(u => u.Email).Returns("admin@test.com");
        _mockCurrentUser.Setup(u => u.Name).Returns("Test Admin");
        _mockCurrentUser.Setup(u => u.Roles).Returns(new List<string> { "Admin" });
        _mockCurrentUser.Setup(u => u.HasRole("Admin")).Returns(true);

        _validator = new DeletePatientCommandValidator(_mockCurrentUser.Object, _mockUow.Object);
    }

    [TestMethod]
    public async Task Validate_WhenUserIsAdmin_ShouldPass()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        var command = new DeletePatientCommand(patientId);

        var mockRepo = new Mock<IRepository<Patient>>();
        mockRepo.Setup(r => r.ExistsAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockUow.Setup(u => u.RepositoryFor<Patient>()).Returns(mockRepo.Object);

        // Act
        var result = await _validator.ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Validate_WhenUserIsNotAdmin_ShouldFail()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        var command = new DeletePatientCommand(patientId);

        // Reconfigure mock to be a non-admin user
        _mockCurrentUser.Setup(u => u.Roles).Returns(new List<string> { "User" });
        _mockCurrentUser.Setup(u => u.HasRole("Admin")).Returns(false);
        _mockCurrentUser.Setup(u => u.HasRole(It.IsAny<string>()))
            .Returns((string role) => role == "User");

        // Recreate validator with updated mock
        _validator = new DeletePatientCommandValidator(_mockCurrentUser.Object, _mockUow.Object);

        // Act
        var result = await _validator.ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorCode == "ERR_FORBIDDEN");
        result.Errors.ShouldContain(e =>
            e.ErrorMessage.Contains("do not have the required role"));
    }

    [TestMethod]
    public async Task Validate_WhenUserIsDoctor_ShouldFail()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        var command = new DeletePatientCommand(patientId);

        // Reconfigure mock to be a doctor (not admin)
        _mockCurrentUser.Setup(u => u.Roles).Returns(new List<string> { "Doctor" });
        _mockCurrentUser.Setup(u => u.HasRole("Admin")).Returns(false);
        _mockCurrentUser.Setup(u => u.HasRole("Doctor")).Returns(true);

        _validator = new DeletePatientCommandValidator(_mockCurrentUser.Object, _mockUow.Object);

        // Act
        var result = await _validator.ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorCode == "ERR_FORBIDDEN");
    }

    [TestMethod]
    public async Task Validate_WhenPatientDoesNotExist_ShouldFail()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        var command = new DeletePatientCommand(patientId);

        var mockRepo = new Mock<IRepository<Patient>>();
        mockRepo.Setup(r => r.ExistsAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockUow.Setup(u => u.RepositoryFor<Patient>()).Returns(mockRepo.Object);

        // Act
        var result = await _validator.ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("Patient not found"));
    }
}
```

### Testing Complex Role Logic (AND/OR)

```csharp
[TestClass]
public class CriticalActionCommandValidatorTests
{
    private Mock<ICurrentUser> _mockCurrentUser = null!;
    private Mock<IUnitOfWork> _mockUow = null!;

    [TestMethod]
    public async Task Validate_WhenUserIsAdmin_ShouldPass()
    {
        // Arrange
        var mockCurrentUser = CreateMockUser(roles: new[] { "Admin" });
        var validator = new CriticalActionCommandValidator(mockCurrentUser.Object, _mockUow.Object);
        var command = new CriticalActionCommand();

        // Act
        var result = await validator.ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Validate_WhenUserIsDoctorAndNurseManager_ShouldPass()
    {
        // Arrange
        var mockCurrentUser = CreateMockUser(roles: new[] { "Doctor", "NurseManager" });
        var validator = new CriticalActionCommandValidator(mockCurrentUser.Object, _mockUow.Object);
        var command = new CriticalActionCommand();

        // Act
        var result = await validator.ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Validate_WhenUserIsDoctorOnly_ShouldFail()
    {
        // Arrange
        var mockCurrentUser = CreateMockUser(roles: new[] { "Doctor" });
        var validator = new CriticalActionCommandValidator(mockCurrentUser.Object, _mockUow.Object);
        var command = new CriticalActionCommand();

        // Act
        var result = await validator.ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorCode == "ERR_FORBIDDEN");
    }

    [TestMethod]
    public async Task Validate_WhenUserIsNurseManagerOnly_ShouldFail()
    {
        // Arrange
        var mockCurrentUser = CreateMockUser(roles: new[] { "NurseManager" });
        var validator = new CriticalActionCommandValidator(mockCurrentUser.Object, _mockUow.Object);
        var command = new CriticalActionCommand();

        // Act
        var result = await validator.ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeFalse();
    }

    private Mock<ICurrentUser> CreateMockUser(string[] roles)
    {
        var mock = new Mock<ICurrentUser>();
        mock.Setup(u => u.IsAuthenticated).Returns(true);
        mock.Setup(u => u.UserId).Returns("test-user-id");
        mock.Setup(u => u.Roles).Returns(roles.ToList());
        mock.Setup(u => u.HasRole(It.IsAny<string>()))
            .Returns((string role) => roles.Contains(role));
        return mock;
    }
}
```

---

## 7. Updating Existing Validators

All existing validators in the project extend `UserValidator<T>` but don't currently use role validation. You need to update them to inject `ICurrentUser`.

### Before (Existing Code)

```csharp
public class CreatePatientCommandValidator : UserValidator<CreatePatientCommand>
{
    public CreatePatientCommandValidator(IUnitOfWork unitOfWork)
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        // ... other rules
    }
}
```

### After (With ICurrentUser)

```csharp
public class CreatePatientCommandValidator : UserValidator<CreatePatientCommand>
{
    public CreatePatientCommandValidator(ICurrentUser currentUser, IUnitOfWork unitOfWork)
        : base(currentUser)  // No role restrictions, any authenticated user
    {
        // Don't call RuleForUserValidation() — authentication is handled by [Authorize]

        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MustAsync(async (email, ct) =>
                !await unitOfWork.RepositoryFor<Patient>().AnyAsync(p => p.Email == email, ct))
            .WithMessage("A patient with this email already exists.");
    }
}
```

**Migration checklist for each validator**:
1. Add `ICurrentUser currentUser` parameter to constructor
2. Call `base(currentUser)` if no role restrictions needed
3. Call `base(currentUser, roleGroups)` if specific roles required
4. Call `RuleForUserValidation()` in constructor if role check is needed
5. Update validator tests to mock `ICurrentUser`

---

## 8. Integration with MediatR Pipeline

The validation happens automatically in the MediatR pipeline via `ValidationBehavior`:

```
HTTP Request
    ↓
Controller calls MediatR.Send(command)
    ↓
┌────────────────────────────────────┐
│   MediatR Pipeline                 │
│  ┌──────────────────────────────┐  │
│  │ ValidationBehavior<TReq,TRes>│  │
│  │  - Resolve all validators    │  │
│  │  - Call ValidateAsync()      │  │
│  │  - Throw if invalid          │  │
│  └──────────────────────────────┘  │
│               ↓                    │
│  ┌──────────────────────────────┐  │
│  │  Command Handler             │  │
│  │  - Execute business logic    │  │
│  └──────────────────────────────┘  │
└────────────────────────────────────┘
    ↓
HTTP Response
```

The `ValidationBehavior` (from BuildingBlocks.Application) will:
1. Resolve all `IValidator<TRequest>` from DI
2. Call `ValidateAsync()` on each validator
3. If any validator fails (including role check), throw `ValidationException`
4. The exception is caught by the global error handler and returned as 400 Bad Request (or 403 Forbidden if you inspect error codes)

---

## 9. Error Handling for Authorization Failures

By default, FluentValidation failures return 400 Bad Request. For better UX, you should distinguish authorization failures (403 Forbidden) from validation failures (400 Bad Request).

### Option 1: Check Error Code in Global Error Handler

```csharp
// BuildingBlocks/BuildingBlocks.WebApplications/Filters/GlobalExceptionFilter.cs
public class GlobalExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is ValidationException validationEx)
        {
            // Check if any validation error is a forbidden error
            var hasForbiddenError = validationEx.Errors.Any(e => e.ErrorCode == "ERR_FORBIDDEN");

            if (hasForbiddenError)
            {
                context.Result = new ObjectResult(new
                {
                    error = "Forbidden",
                    message = "You do not have permission to perform this action.",
                    details = validationEx.Errors
                        .Where(e => e.ErrorCode == "ERR_FORBIDDEN")
                        .Select(e => e.ErrorMessage)
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }
            else
            {
                context.Result = new BadRequestObjectResult(new
                {
                    error = "Validation failed",
                    details = validationEx.Errors.Select(e => new
                    {
                        property = e.PropertyName,
                        message = e.ErrorMessage,
                        code = e.ErrorCode
                    })
                });
            }

            context.ExceptionHandled = true;
        }
    }
}
```

### Option 2: Custom Exception for Authorization

Create a custom exception for authorization failures:

```csharp
// BuildingBlocks/BuildingBlocks.Application/Exceptions/ForbiddenException.cs
namespace BuildingBlocks.Application.Exceptions;

public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}
```

Update `UserValidator` to throw this exception instead of using FluentValidation:

```csharp
// In UserValidator<T>
protected void EnsureUserValidation()
{
    if (!HaveAValidRole())
        throw new ForbiddenException("You do not have the required role to perform this action.");
}

// In derived validators
public DeletePatientCommandValidator(ICurrentUser currentUser, IUnitOfWork unitOfWork)
    : base(currentUser, new[] { "Admin" })
{
    EnsureUserValidation();  // Throws ForbiddenException immediately

    // Other rules...
}
```

Handle in global error filter:

```csharp
if (context.Exception is ForbiddenException forbiddenEx)
{
    context.Result = new ObjectResult(new { error = forbiddenEx.Message })
    {
        StatusCode = StatusCodes.Status403Forbidden
    };
    context.ExceptionHandled = true;
}
```

---

## 10. Summary

### What We Built

| Component | Location | Purpose |
|-----------|----------|---------|
| `ICurrentUser` | `BuildingBlocks.Application/Abstractions` | Interface for accessing current user information |
| `HttpContextCurrentUser` | `BuildingBlocks.Infrastructure.Auth` | Implementation reading from HTTP context claims |
| `UserValidator<T>` | `BuildingBlocks.Application/Validators` | Base validator with role-based authorization |
| Role validation | Validators | AND/OR logic for complex role requirements |
| Testing support | Test projects | Mocking `ICurrentUser` for unit tests |

### Key Concepts

1. **Abstraction in Application Layer**: `ICurrentUser` lives in Application, not Domain or Infrastructure. This maintains Clean Architecture principles.

2. **AND/OR Role Logic**:
   - Inner collection = AND (user must have ALL roles)
   - Outer array = OR (user must satisfy at least ONE group)

3. **Two-Constructor Pattern**:
   - `UserValidator(ICurrentUser, params roleGroups)` for role-based validation
   - `UserValidator(ICurrentUser)` for "any authenticated user"

4. **Testing Strategy**: Mock `ICurrentUser` with Moq, configure roles per test case.

5. **Error Handling**: Distinguish 403 Forbidden (authorization) from 400 Bad Request (validation).

### Architecture Flow

```
┌─────────────────────────────────────────────────────────────┐
│                    HTTP Request (Cookie)                    │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│  ASP.NET Core Authentication Middleware                     │
│  - Reads cookie                                             │
│  - Populates HttpContext.User with claims                   │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│  HttpContextCurrentUser (Infrastructure)                    │
│  - Reads claims from HttpContext.User                       │
│  - Maps to ICurrentUser properties                          │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│  UserValidator<T> (Application)                             │
│  - Receives ICurrentUser via DI                             │
│  - Validates roles using HaveAValidRole()                   │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│  Command Handler (Application)                              │
│  - Receives ICurrentUser via DI                             │
│  - Uses for audit, ownership checks, business logic         │
└─────────────────────────────────────────────────────────────┘
```

### Next Steps

With user context and authorization in place, Phase 8 is complete. You now have:
- Duende IdentityServer-based authentication
- Cookie-based session management
- Angular frontend integration with OIDC
- Blazor Server authentication (documented)
- User context abstraction
- Role-based authorization in validators

**Future enhancements**:
- Resource-based authorization (e.g., "user can only edit their own appointments")
- Policy-based authorization with ASP.NET Core `IAuthorizationService`
- Attribute-based access control (ABAC)
- Claims transformation for custom business roles
- Multi-tenancy support

---

## Phase 8: Authentication & Authorization - Complete

This concludes Phase 8. Here's the full documentation series:

1. **[01-auth-overview.md](./01-auth-overview.md)** - Architecture overview, design decisions, Duende IdentityServer introduction
2. **[02-authorization-server-setup.md](./02-authorization-server-setup.md)** - Duende IdentityServer configuration, database migrations, dev data seeding
3. **[03-shared-auth-infrastructure.md](./03-shared-auth-infrastructure.md)** - BuildingBlocks.Infrastructure.Auth library, cookie authentication, middleware setup
4. **[04-api-resource-protection.md](./04-api-resource-protection.md)** - Integrating auth into WebAPI projects, controllers, Swagger UI
5. **[05-angular-auth.md](./05-angular-auth.md)** - Angular frontend authentication, OIDC client, guards, interceptors
6. **[06-user-context-and-authorization.md](./06-user-context-and-authorization.md)** *(this document)* - ICurrentUser abstraction, UserValidator activation, role-based authorization

**What you've learned**:
- How to implement enterprise authentication with Duende IdentityServer
- Cookie-based authentication for both SPA and Blazor frontends
- Clean separation of concerns (abstraction in Application, implementation in Infrastructure)
- Flexible role-based authorization with AND/OR logic
- Testing strategies for authentication and authorization

**What's next**: Phase 9 (API Gateway & BFF Pattern) or continue refining your frontends with the authentication foundation in place.

---

> **Previous**: [05-angular-auth.md](./05-angular-auth.md)

**Phase 8 Complete!** You now have a fully functional authentication and authorization system integrated across your DDD microservices architecture.
