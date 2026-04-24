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

> **Important**: This is where role-based authorization lives in our architecture. Controllers use `[Authorize]` for authentication only (see [doc 04](./04-api-resource-protection.md)). All role checks are handled here in the application layer by `UserValidator<T>`, keeping authorization logic testable and centralized.

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
// File: BuildingBlocks/BuildingBlocks.Application/Auth/ICurrentUser.cs

namespace BuildingBlocks.Application.Auth;

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

| OIDC Claim | Runtime claim name (.NET 9) | ICurrentUser Property | Domain Usage |
|------------|----------------------------|----------------------|--------------|
| `sub` | `"sub"` (fallback from `ClaimTypes.NameIdentifier`) | `UserId` | Audit logging, ownership checks |
| `email` | `"email"` (fallback from `ClaimTypes.Email`) | `Email` | Notifications, user identification |
| `name` | `"name"` (fallback from `ClaimTypes.Name`) | `Name` | Display in UI, logging |
| `role` | `"role"` (checked alongside `ClaimTypes.Role`) | `Roles` | Authorization decisions |
| N/A | `User.Identity?.IsAuthenticated` | `IsAuthenticated` | Authentication gate |

> **Note**: Under .NET 9's `JsonWebTokenHandler`, claims are stored with their raw JWT names — no remapping to `ClaimTypes` URIs occurs. Each property reads both the mapped URI form and the raw JWT name so the code is forward- and backward-compatible.

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

    public IReadOnlyList<string> Roles
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return Array.Empty<string>();

            // .NET 9 uses JsonWebTokenHandler which does NOT remap JWT claim names to
            // SOAP/WS-Fed URIs. Claims arrive with raw JWT names ("role"), not as
            // ClaimTypes.Role ("http://schemas.microsoft.com/ws/2008/06/identity/claims/role").
            // UserId, Name, and Email already fall back to their raw names; Roles must do
            // the same or it silently returns an empty list even when the IDP issued roles.
            // See "Claim naming and JsonWebTokenHandler" below.
            var mapped = user.FindAll(ClaimTypes.Role).Select(c => c.Value);
            var raw    = user.FindAll("role").Select(c => c.Value);
            return mapped.Concat(raw).Distinct().ToList();
        }
    }

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}
```

> **Claim naming and JsonWebTokenHandler**
>
> .NET 9's OIDC middleware uses `JsonWebTokenHandler` instead of the older `JwtSecurityTokenHandler`. The old handler automatically remapped JWT claim names to their SOAP/WS-Fed `ClaimTypes` URIs (e.g., `role` → `ClaimTypes.Role`). `JsonWebTokenHandler` does **not** — claims land in the `ClaimsPrincipal` with the raw JWT names they arrived with: `sub`, `name`, `email`, `role`.
>
> `UserId`, `Name`, and `Email` have always fallen back to their raw names (e.g., `user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email")`). `Roles` only checked `ClaimTypes.Role` and therefore silently returned an empty list even when the IDP issued `role` claims. The fix above checks both names and de-duplicates.
>
> Setting `TokenValidationParameters.NameClaimType = "name"` and `RoleClaimType = "role"` in `AddOpenIdConnect` does **not** trigger claim remapping. It only tells the identity which raw claim to use for `User.Identity.Name` and for `User.IsInRole(...)` lookups. The raw claim name (`"role"`) is still what appears in `ClaimsPrincipal.Claims`, so explicit fallback reads are always necessary.

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
using BuildingBlocks.Application.Auth;
using BuildingBlocks.Enumerations;
using FluentValidation;

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

        // Auto-register the role check so derived validators can't silently bypass it
        // by forgetting to invoke it. Runs before any rules registered in the derived ctor.
        RuleFor(r => r)
            .Must(_ => HaveAValidRole())
                .WithMessage(ErrorCode.Forbidden.Message)
                .WithErrorCode(ErrorCode.Forbidden.Value);
    }

    /// <summary>
    /// Checks if the current user satisfies at least one role group.
    /// A user must have ALL roles in at least ONE group to pass.
    /// </summary>
    private bool HaveAValidRole()
    {
        foreach (var group in _allowedRoleGroups)
        {
            if (group.All(role => CurrentUser.HasRole(role)))
                return true;
        }

        return false;
    }
}
```

**Key improvements**:
- **Null safety**: ArgumentNullException on currentUser
- **Clear XML docs**: Explains the AND/OR pattern with examples
- **Single responsibility**: Only for validators that need a role gate. For "any authenticated user" validators, extend `AbstractValidator<T>` directly — `[Authorize]` on the controller already enforces authentication.
- **Auto-registered role rule**: The base constructor registers the role check itself, so derived validators can't silently bypass it by forgetting to invoke it.
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
using Shared.Auth;

public class DeletePatientCommandValidator : UserValidator<DeletePatientCommand>
{
    public DeletePatientCommandValidator(
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork)
        : base(currentUser, new[] { AppRoles.Admin })  // Single role group: Admin only
    {
        RuleFor(x => x.Id)
            .MustAsync(async (id, ct) => await unitOfWork.RepositoryFor<Patient>().ExistsAsync(id, ct))
            .WithMessage("Patient not found.");
    }
}
```

Logic: User must have Admin role.

**Scenario 2: Admins OR Doctors can suspend patients**

```csharp
using Shared.Auth;

public class SuspendPatientCommandValidator : UserValidator<SuspendPatientCommand>
{
    public SuspendPatientCommandValidator(
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork)
        : base(currentUser, new[] { AppRoles.Admin }, new[] { AppRoles.Doctor })  // Two role groups
    {
        RuleFor(x => x.Id)
            .MustAsync(async (id, ct) => await unitOfWork.RepositoryFor<Patient>().ExistsAsync(id, ct))
            .WithMessage("Patient not found.");
    }
}
```

Logic: User must have Admin OR Doctor role.

**Scenario 3: Complex - Admin OR (Doctor AND NurseManager)**

```csharp
using Shared.Auth;

public class CriticalActionCommandValidator : UserValidator<CriticalActionCommand>
{
    public CriticalActionCommandValidator(
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork)
        : base(currentUser,
               new[] { AppRoles.Admin },                    // Group 1: Admin alone
               new[] { AppRoles.Doctor, "NurseManager" })   // Group 2: Doctor AND NurseManager
    {
        // ... other rules
    }
}
```

Logic: User must have Admin OR (both Doctor AND NurseManager).

> **Note**: The "NurseManager" role is not currently defined in `AppRoles`. You can add it if needed:
> ```csharp
> // Shared/Auth/AppRoles.cs
> public const string NurseManager = "NurseManager";
> ```

**Scenario 4: Nurse, Doctor, or Admin can create patients**

```csharp
using Shared.Auth;

public class CreatePatientCommandValidator : UserValidator<CreatePatientCommand>
{
    public CreatePatientCommandValidator(
        ICurrentUser currentUser,
        IValidator<CreatePatientRequest> requestValidator)
        : base(currentUser, new[] { AppRoles.Nurse }, new[] { AppRoles.Doctor }, new[] { AppRoles.Admin })
    {
        RuleFor(c => c.Patient).Cascade(CascadeMode.Stop)
            .NotNull()
            .SetValidator(requestValidator);
    }
}
```

Logic: User must have Nurse, Doctor, or Admin role. Front desk and clinical staff can register patients. The existing `CreatePatientRequestValidator` handles field-level validation (names, email, date of birth).

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
              │    Role check rule      │
              │ (auto-registered in base)│
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

Beyond validation, command handlers can use `ICurrentUser` for audit trails — stamping who created a record, attaching the caller to an integration event, or passing the user into a domain method that needs it. Authorization checks (role gates, ownership rules, NotFound guards) stay in the validator layer; the handler assumes those already passed.

### Example: Audit Trail

Existence and role checks go in the validator; the handler only does the work and stamps audit information using `ICurrentUser`.

```csharp
// Scheduling/Scheduling.Application/Commands/CreateAppointment/CreateAppointmentCommandValidator.cs
public class CreateAppointmentCommandValidator : UserValidator<CreateAppointmentCommand>
{
    private readonly IUnitOfWork _uow;

    public CreateAppointmentCommandValidator(ICurrentUser currentUser, IUnitOfWork uow)
        : base(currentUser, new[] { AppRoles.Doctor }, new[] { AppRoles.Nurse }, new[] { AppRoles.Admin })
    {
        _uow = uow;

        RuleFor(c => c.PatientId)
            .MustAsync(BeAValidPatientAsync)
                .WithErrorCode(ErrorCode.NotFound.Value)
                .WithMessage(ErrorCode.NotFound.Message);
    }

    private Task<bool> BeAValidPatientAsync(Guid id, CancellationToken ct) =>
        _uow.RepositoryFor<Patient>().ExistsAsync(id, ct);
}
```

```csharp
// Scheduling/Scheduling.Application/Commands/CreateAppointment/CreateAppointmentCommandHandler.cs
using BuildingBlocks.Application.Auth;
using BuildingBlocks.Infrastructure.EfCore;
using MediatR;
using Scheduling.Domain.Aggregates.Appointments;

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
        // Validator already guaranteed: caller has the role AND the patient exists.
        var appointment = Appointment.Create(
            cmd.PatientId,
            cmd.DoctorId,
            cmd.AppointmentDateTime,
            cmd.DurationMinutes);

        _uow.RepositoryFor<Appointment>().Add(appointment);

        // Audit: stamp the creator on the integration event using ICurrentUser
        _uow.QueueIntegrationEvent(new AppointmentCreatedIntegrationEvent(
            appointment.Id,
            cmd.PatientId,
            cmd.DoctorId,
            cmd.AppointmentDateTime,
            cmd.DurationMinutes,
            CreatedBy: _currentUser.UserId,
            CreatedByName: _currentUser.Name));

        await _uow.SaveChangesAsync(ct);
        return appointment.Id;
    }
}
```

### Example: Ownership Check (Validator)

Ownership checks belong in the validator, not the handler — same layer that enforces role gates and `NotFound` existence checks. The handler stays thin and trusts that validation has already passed.

```csharp
// Scheduling/Scheduling.Application/Patients/Commands/UpdateMyPatientProfileCommandValidator.cs
public class UpdateMyPatientProfileCommandValidator : UserValidator<UpdateMyPatientProfileCommand>
{
    private readonly IUnitOfWork _uow;

    public UpdateMyPatientProfileCommandValidator(ICurrentUser currentUser, IUnitOfWork uow)
        : base(currentUser, new[] { AppRoles.Patient })
    {
        _uow = uow;

        RuleFor(c => c.PatientId)
            .MustAsync(BeAValidPatientAsync)
                .WithErrorCode(ErrorCode.NotFound.Value)
                .WithMessage(ErrorCode.NotFound.Message)
            .MustAsync(BeOwnedByCurrentUserAsync)
                .WithErrorCode(ErrorCode.Forbidden.Value)
                .WithMessage("You can only update your own profile.");
    }

    private Task<bool> BeAValidPatientAsync(Guid id, CancellationToken ct) =>
        _uow.RepositoryFor<Patient>().ExistsAsync(id, ct);

    private async Task<bool> BeOwnedByCurrentUserAsync(Guid id, CancellationToken ct)
    {
        var patient = await _uow.RepositoryFor<Patient>().GetByIdAsync(id, ct);
        return patient?.UserId == CurrentUser.UserId;
    }
}
```

The handler then contains no authorization logic — it only performs the business action:

```csharp
public class UpdateMyPatientProfileCommandHandler : IRequestHandler<UpdateMyPatientProfileCommand>
{
    private readonly IUnitOfWork _uow;

    public async Task Handle(UpdateMyPatientProfileCommand cmd, CancellationToken ct)
    {
        // Validator already guaranteed: patient exists AND is owned by current user
        var patient = await _uow.RepositoryFor<Patient>().GetByIdAsync(cmd.PatientId, ct);
        patient!.UpdateContactInfo(cmd.Email, cmd.PhoneNumber);
        await _uow.SaveChangesAsync(ct);
    }
}
```

---

## 6. Updating Existing Validators

All existing validators in the project extend `UserValidator<T>` but don't currently use role validation. You need to update each one based on the role matrix from [doc 04](./04-api-resource-protection.md#authorization-summary) — queries that any authenticated user can issue demote to `AbstractValidator<T>`, commands keep `UserValidator<T>` with explicit role groups.

### Target State

| Validator | Base class | Role groups (`: base(currentUser, ...)`) |
|-----------|-----------|------------------------------------------|
| `GetAllPatientsQueryValidator` | `AbstractValidator<T>` | — (authentication handled by `[Authorize]`) |
| `GetPatientQueryValidator` | `AbstractValidator<T>` | — (authentication handled by `[Authorize]`) |
| `CreatePatientCommandValidator` | `UserValidator<T>` | `new[] { AppRoles.Nurse }, new[] { AppRoles.Doctor }, new[] { AppRoles.Admin }` — Nurse OR Doctor OR Admin |
| `SuspendPatientCommandValidator` | `UserValidator<T>` | `new[] { AppRoles.Doctor }, new[] { AppRoles.Admin }` — Doctor OR Admin |
| `ActivatePatientCommandValidator` | `UserValidator<T>` | `new[] { AppRoles.Doctor }, new[] { AppRoles.Admin }` — Doctor OR Admin |
| `DeletePatientCommandValidator` | `UserValidator<T>` | `new[] { AppRoles.Admin }` — Admin only |

### Before (Existing Code)

```csharp
public class SuspendPatientCommandValidator : UserValidator<SuspendPatientCommand>
{
    private readonly IUnitOfWork _uow;

    public SuspendPatientCommandValidator(IUnitOfWork uow)
    {
        _uow = uow;

        RuleFor(c => c.Id)
            .MustAsync(BeAValidPatientAsync)
            .WithErrorCode(ErrorCode.NotFound.Value)
            .WithMessage(ErrorCode.NotFound.Message);
    }
    // ...
}
```

### After (With ICurrentUser)

```csharp
public class SuspendPatientCommandValidator : UserValidator<SuspendPatientCommand>
{
    private readonly IUnitOfWork _uow;

    public SuspendPatientCommandValidator(ICurrentUser currentUser, IUnitOfWork uow)
        : base(currentUser, new[] { AppRoles.Admin }, new[] { AppRoles.Doctor })  // Doctor or Admin
    {
        _uow = uow;

        RuleFor(c => c.Id)
            .MustAsync(BeAValidPatientAsync)
            .WithErrorCode(ErrorCode.NotFound.Value)
            .WithMessage(ErrorCode.NotFound.Message);
    }
    // ...
}
```

**Migration checklist for each validator**:
1. If the endpoint has no role restriction (any authenticated user), extend `AbstractValidator<T>` directly — `[Authorize]` on the controller already handles authentication. Skip the remaining steps.
2. Otherwise, extend `UserValidator<T>` and:
   - Add `ICurrentUser currentUser` parameter to constructor
   - Call `: base(currentUser, roleGroups)` with the required role groups — the base constructor auto-registers the role check rule, so no explicit call is needed in the derived body
   - Update validator tests to mock `ICurrentUser`

---

## 7. Testing Role-Based Validation

Testing validators with role logic requires mocking `ICurrentUser`. Here's how to do it with Moq and Shouldly.

### What Needs to Change in the Existing Test Suite

The existing validator tests in `Scheduling.Domain.Tests/ApplicationTests/ValidatorTests/` were written before role gates existed. Now that the base validator auto-registers a role rule, any test that ran successfully with the old "no role check" validator will start failing unless the mock user is set up with the right role. Here's the concrete work per test class:

| Test class | Action | Why |
|------------|--------|-----|
| `GetAllPatientsQueryValidatorTests` | No change | Validator is now `AbstractValidator<T>` — no role gate |
| `GetPatientQueryValidatorTests` | No change | Same as above |
| `CreatePatientCommandValidatorTests` | Fix existing tests + add role tests | Validator now gates on Nurse OR Doctor OR Admin |
| `SuspendPatientCommandValidatorTests` | Fix existing tests + add role tests | Validator now gates on Doctor OR Admin |
| `ActivatePatientCommandValidatorTests` | Fix existing tests + add role tests | Validator now gates on Doctor OR Admin |
| `DeletePatientCommandValidatorTests` | Fix existing tests + add role tests | Validator now gates on Admin only |

**"Fix existing tests"** = every currently passing test (e.g., `Valid_When_PatientExists`) needs the mock user to have one of the allowed roles, otherwise the auto-registered role rule fails the validation.

**"Add role tests"** per command validator (one happy-path test per allowed role group + one denial test for an unauthorized role):

| Command validator | Tests to add |
|-------------------|--------------|
| `CreatePatientCommandValidator` | `Valid_When_UserIsNurse`, `Valid_When_UserIsDoctor`, `Valid_When_UserIsAdmin`, `Invalid_When_UserHasNoAllowedRole` |
| `SuspendPatientCommandValidator` | `Valid_When_UserIsDoctor`, `Valid_When_UserIsAdmin`, `Invalid_When_UserIsNurse`, `Invalid_When_UserHasNoAllowedRole` |
| `ActivatePatientCommandValidator` | `Valid_When_UserIsDoctor`, `Valid_When_UserIsAdmin`, `Invalid_When_UserIsNurse`, `Invalid_When_UserHasNoAllowedRole` |
| `DeletePatientCommandValidator` | `Valid_When_UserIsAdmin`, `Invalid_When_UserIsDoctor`, `Invalid_When_UserIsNurse`, `Invalid_When_UserHasNoAllowedRole` |

A denial test should assert the error has `ErrorCode == ErrorCode.Forbidden.Value` — see the examples below.

### What Needs to Be Added to the Test Infrastructure

The existing `ValidatorTestBase` in `BuildingBlocks.Tests` already builds a DI container and registers a mocked `IUnitOfWork`. We extend it with a `Mock<ICurrentUser>` so the auto-registered role rule has something to read, plus a `SetupUserRoles(...)` helper so tests can declare the caller's role in a single line.

```csharp
// BuildingBlocks/BuildingBlocks.Tests/ValidatorTestBase.cs — additions only
using BuildingBlocks.Application.Auth;

public abstract class ValidatorTestBase
{
    // ... existing members ...

    protected Mock<ICurrentUser> CurrentUserMock { get; private set; } = null!;

    [TestInitialize]
    public virtual void TestInitialize()
    {
        _stopwatch = new Stopwatch();

        var services = new ServiceCollection();

        // Existing: mocked IUnitOfWork
        UnitOfWorkMock = new Mock<IUnitOfWork>();
        services.AddSingleton(UnitOfWorkMock.Object);

        // NEW: mocked ICurrentUser — authenticated but with no roles by default.
        // Tests that exercise a role-gated validator call SetupUserRoles(...) in
        // their Arrange block to give the caller an allowed role.
        CurrentUserMock = new Mock<ICurrentUser>();
        CurrentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        CurrentUserMock.Setup(u => u.UserId).Returns("test-user-id");
        CurrentUserMock.Setup(u => u.Name).Returns("Test User");
        CurrentUserMock.Setup(u => u.Email).Returns("test@test.com");
        CurrentUserMock.Setup(u => u.Roles).Returns(Array.Empty<string>());
        CurrentUserMock.Setup(u => u.HasRole(It.IsAny<string>())).Returns(false);
        services.AddSingleton(CurrentUserMock.Object);

        RegisterServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Configure the mocked ICurrentUser to return the given roles.
    /// Call this in the Arrange section of any test that exercises a role-gated validator.
    /// </summary>
    protected void SetupUserRoles(params string[] roles)
    {
        var list = roles.ToList();
        CurrentUserMock.Setup(u => u.Roles).Returns(list);
        CurrentUserMock.Setup(u => u.HasRole(It.IsAny<string>()))
            .Returns((string r) => list.Contains(r));
    }
}
```

`SchedulingValidatorTestBase` does not need to change — it already overrides `RegisterServices` to register Scheduling validators, and the new `CurrentUserMock` is picked up by the DI container automatically when the role-gated validator is constructed.

### Updated Test Pattern (DeletePatientCommandValidator — Admin only)

Existing tests (`Invalid_When_PatientDoesNotExist`, `Valid_When_PatientExists`) need a `SetupUserRoles(AppRoles.Admin)` call added to their Arrange block so the auto-registered role rule passes. New role-focused tests follow the project's existing naming convention (`Valid_When_X` / `Invalid_When_X`) and use the same `SchedulingValidatorTestBase` helpers.

```csharp
// Scheduling/Scheduling.Domain.Tests/ApplicationTests/ValidatorTests/DeletePatientCommandValidatorTests.cs
using BuildingBlocks.Enumerations;
using BuildingBlocks.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Application.Patients.Commands;
using Shared.Auth;
using Shouldly;

namespace Scheduling.Tests.ApplicationTests.ValidatorTests;

[TestClass]
public class DeletePatientCommandValidatorTests : SchedulingValidatorTestBase
{
    // --- Existing tests, updated to set up the required role ---

    [TestMethod]
    public async Task Invalid_When_PatientDoesNotExist()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupUserRoles(AppRoles.Admin);
        SetupPatientNotExists(patientId);
        var command = new DeletePatientCommand { Id = patientId };

        // Act
        StartStopwatch();
        var result = await ValidatorFor<DeletePatientCommand>().ValidateAsync(command);
        StopStopwatch();

        // Assert
        result.Errors.ShouldContainValidation(nameof(DeletePatientCommand.Id), ErrorCode.NotFound.Value);
        result.Errors.Count.ShouldBe(1);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Valid_When_PatientExists()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupUserRoles(AppRoles.Admin);
        SetupPatientExists(patientId);
        var command = new DeletePatientCommand { Id = patientId };

        // Act
        var result = await ValidatorFor<DeletePatientCommand>().ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    // --- New tests for the role gate ---

    [TestMethod]
    public async Task Valid_When_UserIsAdmin()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupUserRoles(AppRoles.Admin);
        SetupPatientExists(patientId);
        var command = new DeletePatientCommand { Id = patientId };

        // Act
        var result = await ValidatorFor<DeletePatientCommand>().ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Invalid_When_UserIsDoctor()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupUserRoles(AppRoles.Doctor);
        SetupPatientExists(patientId);
        var command = new DeletePatientCommand { Id = patientId };

        // Act
        var result = await ValidatorFor<DeletePatientCommand>().ValidateAsync(command);

        // Assert
        result.Errors.ShouldContain(e => e.ErrorCode == ErrorCode.Forbidden.Value);
    }

    [TestMethod]
    public async Task Invalid_When_UserIsNurse()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupUserRoles(AppRoles.Nurse);
        SetupPatientExists(patientId);
        var command = new DeletePatientCommand { Id = patientId };

        // Act
        var result = await ValidatorFor<DeletePatientCommand>().ValidateAsync(command);

        // Assert
        result.Errors.ShouldContain(e => e.ErrorCode == ErrorCode.Forbidden.Value);
    }

    [TestMethod]
    public async Task Invalid_When_UserHasNoAllowedRole()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupPatientExists(patientId);
        // No SetupUserRoles call — the mock user has no roles by default
        var command = new DeletePatientCommand { Id = patientId };

        // Act
        var result = await ValidatorFor<DeletePatientCommand>().ValidateAsync(command);

        // Assert
        result.Errors.ShouldContain(e => e.ErrorCode == ErrorCode.Forbidden.Value);
    }
}
```

### Testing a Multi-Role Gate (SuspendPatientCommandValidator — Doctor OR Admin)

For validators with multiple allowed role groups, write one `Valid_When_UserIsX` per group and denial tests for roles outside the set. The patient-existence tests stay largely the same — just add `SetupUserRoles(AppRoles.Doctor)` (or `AppRoles.Admin`) to their Arrange block.

```csharp
[TestMethod]
public async Task Valid_When_UserIsDoctor()
{
    // Arrange
    var patientId = Guid.NewGuid();
    SetupUserRoles(AppRoles.Doctor);
    SetupPatientExists(patientId);
    var command = new SuspendPatientCommand { Id = patientId };

    // Act
    var result = await ValidatorFor<SuspendPatientCommand>().ValidateAsync(command);

    // Assert
    result.IsValid.ShouldBeTrue();
}

[TestMethod]
public async Task Valid_When_UserIsAdmin()
{
    // Arrange
    var patientId = Guid.NewGuid();
    SetupUserRoles(AppRoles.Admin);
    SetupPatientExists(patientId);
    var command = new SuspendPatientCommand { Id = patientId };

    // Act
    var result = await ValidatorFor<SuspendPatientCommand>().ValidateAsync(command);

    // Assert
    result.IsValid.ShouldBeTrue();
}

[TestMethod]
public async Task Invalid_When_UserIsNurse()
{
    // Arrange
    var patientId = Guid.NewGuid();
    SetupUserRoles(AppRoles.Nurse);
    SetupPatientExists(patientId);
    var command = new SuspendPatientCommand { Id = patientId };

    // Act
    var result = await ValidatorFor<SuspendPatientCommand>().ValidateAsync(command);

    // Assert
    result.Errors.ShouldContain(e => e.ErrorCode == ErrorCode.Forbidden.Value);
}
```

Tests for `ActivatePatientCommandValidator` follow the same structure (Doctor OR Admin). Tests for `CreatePatientCommandValidator` add a third happy-path test for Nurse, since that validator permits Nurse OR Doctor OR Admin.

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

> **This replaces `[Authorize(Roles = ...)]`**: Instead of scattering role checks across controller attributes, all authorization logic flows through the MediatR validation pipeline. This makes role requirements explicit in each validator, testable with unit tests, and independent of the HTTP layer.

---

## 9. Error Handling for Authorization Failures

We want authorization failures to look exactly like validation failures to the client — same JSON shape, same pipeline — just with a 403 status code instead of 400. No separate exception types, no separate filter, no different response model.

### The Existing Pipeline

The project already has an `ExceptionToJsonFilter` in `BuildingBlocks.WebApplications.Filters` that catches every `FluentValidation.ValidationException` and wraps it via `ValidationErrorWrapper`. The response body is always:

```json
{
  "Errors": [
    { "Code": "ERR_NOT_FOUND", "Message": "The requested resource was not found" }
  ],
  "Warnings": []
}
```

The filter sets status code to 400 by default. `ValidationErrorWrapper.TrySetCustomHttpStatusCode` allows a single error with a numeric code (e.g., `"404"`) to override that status.

### The Change: Map `ERR_FORBIDDEN` to 403

The role-gate rule on `UserValidator<T>` fails with `ErrorCode.Forbidden.Value` (`"ERR_FORBIDDEN"`). We extend `TrySetCustomHttpStatusCode` so that if any error in the exception has that code, the wrapper returns 403. Everything else — the response body, the error list, the filter plumbing — stays the same.

```csharp
// BuildingBlocks/BuildingBlocks.WebApplications/Filters/ValidationErrorWrapper.cs
private void TrySetCustomHttpStatusCode(ValidationException exception)
{
    // Forbidden takes precedence: if any error is the role-gate failure from
    // UserValidator<T>, return 403 regardless of what else failed. Response body
    // still uses the standard ValidationErrorWrapper shape — only status differs.
    if (exception.Errors.Any(e => e.ErrorCode == ErrorCode.Forbidden.Value))
    {
        HttpStatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    // Existing: single numeric error code → use as HTTP status
    if (exception.Errors.Count() != 1)
        return;

    var singleError = exception.Errors.Single();

    if (int.TryParse(singleError.ErrorCode, out var httpCode) &&
        httpCode >= 100 && httpCode < 600)
    {
        HttpStatusCode = httpCode;
    }
}
```

This requires `BuildingBlocks.WebApplications` to reference `BuildingBlocks.Enumerations` — add the project reference to the `.csproj`.

### Why "any error", not "only error"?

With `CascadeMode.Continue` (FluentValidation's default), a forbidden role plus a missing patient produces two errors: `ERR_FORBIDDEN` and `ERR_NOT_FOUND`. Returning 403 in that case is the right call — if the caller isn't authorized, we don't want to confirm or deny the resource's existence. Forbidden trumps.

### Client Experience

The Angular `authInterceptor` can catch 403 responses generically:

```typescript
if (error.status === 403) {
  this.notifications.error("You don't have permission to perform this action.");
  return EMPTY;
}
```

No special body parsing required — the shape is identical to a 400.

---

## 10. Angular: Role-Based UI

While backend authorization prevents unauthorized API calls, the frontend should hide unavailable actions for better UX. This section shows how to implement role-based UI in Angular using the `AuthService` and role guards created in [doc 05](./05-angular-auth.md).

### Role Matrix Reference

Here's the authorization matrix for patient management actions:

| Action | Nurse | Doctor | Admin |
|--------|-------|--------|-------|
| View patients | Yes | Yes | Yes |
| Create patient | Yes | Yes | Yes |
| Suspend/Activate | No | Yes | Yes |
| Delete patient | No | No | Yes |

**Implementation strategy**:
- **Backend enforcement**: Validators reject unauthorized commands (403 Forbidden)
- **Frontend guidance**: UI hides buttons users can't use (better UX)
- **Defense in depth**: Frontend restrictions don't replace backend validation

> **Note**: Frontend role checks are for UX only. A determined user can still call the API directly. The backend validators (using `UserValidator<T>`) are the authoritative gatekeepers.

---

### Forbidden Component

Create a dedicated page for access denied scenarios:

```typescript
// File: Frontend/Angular/Scheduling.AngularApp/src/app/features/forbidden/forbidden.ts

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-forbidden',
  standalone: true,
  imports: [MatCardModule, MatButtonModule, MatIconModule],
  templateUrl: './forbidden.html',
  styleUrl: './forbidden.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Forbidden {
  private router = inject(Router);

  goBack(): void {
    this.router.navigate(['/patients']);
  }
}
```

```html
<!-- File: Frontend/Angular/Scheduling.AngularApp/src/app/features/forbidden/forbidden.html -->

<div class="forbidden-container">
  <mat-card>
    <mat-card-header>
      <mat-icon mat-card-avatar color="warn">block</mat-icon>
      <mat-card-title>Access Denied</mat-card-title>
      <mat-card-subtitle>403 Forbidden</mat-card-subtitle>
    </mat-card-header>
    <mat-card-content>
      <p>You do not have permission to access this page.</p>
      <p>If you believe this is an error, contact your administrator.</p>
    </mat-card-content>
    <mat-card-actions>
      <button mat-flat-button (click)="goBack()">
        <mat-icon>arrow_back</mat-icon>
        Back to patients
      </button>
    </mat-card-actions>
  </mat-card>
</div>
```

```scss
// File: Frontend/Angular/Scheduling.AngularApp/src/app/features/forbidden/forbidden.scss

.forbidden-container {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 60vh;

  mat-card {
    max-width: 480px;
    text-align: center;
  }
}
```

**Design notes**:
- **Naming convention**: `Forbidden` (no "Component" suffix), matches existing `PatientList`, `PatientDetail`, `CreatePatient` naming
- **OnPush change detection**: Performance optimization (component only updates when inputs change or events fire)
- **Material Design**: Uses Material Card for consistent styling
- **User-friendly**: Clear message + navigation back to safe page

---

### Update app.routes.ts

Add the forbidden route and demonstrate `roleGuard` usage:

```typescript
// File: Frontend/Angular/Scheduling.AngularApp/src/app/app.routes.ts

import { Routes } from '@angular/core';
import { authGuard, roleGuard } from '@core/guards/auth.guard';
import { AppRoles } from '@core/constants/app-roles';

export const routes: Routes = [
  { path: '', redirectTo: '/patients', pathMatch: 'full' },
  {
    path: 'patients',
    loadComponent: () =>
      import('./features/patients/patient-list/patient-list')
        .then(m => m.PatientList),
    canActivate: [authGuard]
  },
  {
    path: 'patients/:id',
    loadComponent: () =>
      import('./features/patients/patient-detail/patient-detail')
        .then(m => m.PatientDetail),
    canActivate: [authGuard]
  },
  {
    path: 'patients/create',
    loadComponent: () =>
      import('./features/patients/create-patient/create-patient')
        .then(m => m.CreatePatient),
    canActivate: [authGuard]
  },
  {
    path: 'admin',
    loadChildren: () => import('./features/admin/admin.routes').then(m => m.routes),
    canActivate: [roleGuard(AppRoles.Admin)]  // Admin-only section
  },
  {
    path: 'forbidden',
    loadComponent: () =>
      import('./features/forbidden/forbidden')
        .then(m => m.Forbidden)
  },
  {
    path: '**',
    redirectTo: ''
  }
];
```

**How `roleGuard` works** (already implemented in `core/guards/auth.guard.ts`):

```typescript
// File: Frontend/Angular/Scheduling.AngularApp/src/app/core/guards/auth.guard.ts

export function roleGuard(role: string): CanActivateFn {
  return (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    if (!authService.isAuthenticated()) {
      authService.login();  // Redirect to IdentityServer
      return false;
    }

    if (!authService.hasRole(role)) {
      router.navigate(['/forbidden']);  // Show access denied page
      return false;
    }

    return true;  // User has required role
  };
}
```

**Usage pattern**:
- `canActivate: [authGuard]` - Any authenticated user can access
- `canActivate: [roleGuard(AppRoles.Admin)]` - Only admins can access
- Route guards run before component loads, preventing unauthorized navigation

> **Note**: The `AppRoles` constants are defined in `core/constants/app-roles.ts` (created in doc 05). Example: `AppRoles.Admin = 'Admin'`, `AppRoles.Doctor = 'Doctor'`.

---

### Role-Based UI in Patient Detail

Update the patient detail component to show/hide actions based on user roles.

**Step 1**: Update component class to inject `AuthService` and expose role constants:

```typescript
// File: Frontend/Angular/Scheduling.AngularApp/src/app/features/patients/patient-detail/patient-detail.ts

import { ChangeDetectionStrategy, Component, inject, OnInit, signal, computed } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CommonModule } from '@angular/common';

import { PatientApi } from '@core/services/patient-api';
import { AuthService } from '@core/services/auth';
import { NotificationService } from '@core/services/notification.service';
import { Patient } from '@core/models/patient.model';
import { AppRoles } from '@core/constants/app-roles';

@Component({
  selector: 'app-patient-detail',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './patient-detail.html',
  styleUrl: './patient-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PatientDetail implements OnInit {
  private patientService = inject(PatientApi);
  private authService = inject(AuthService);
  private route = inject(ActivatedRoute);
  router = inject(Router);
  private notification = inject(NotificationService);

  // Expose AppRoles for template use
  protected readonly AppRoles = AppRoles;

  patient = signal<Patient | null>(null);
  isSuspended = computed(() => this.patient()?.status === 'Suspended');
  isDeleted = computed(() => this.patient()?.status === 'Deleted');
  isLoading = signal<boolean>(false);

  // Role-based permissions
  canDelete = computed(() => this.authService.hasRole(AppRoles.Admin));
  canSuspend = computed(() =>
    this.authService.hasRole(AppRoles.Doctor) || this.authService.hasRole(AppRoles.Admin)
  );

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.loadPatient(id);
    }
  }

  private loadPatient(id: string): void {
    this.isLoading.set(true);
    this.patientService.getPatientById(id).subscribe({
      next: (patient) => {
        this.patient.set(patient);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.notification.error('Failed to load patient.');
        this.isLoading.set(false);
      }
    });
  }

  suspend(): void {
    const id = this.patient()?.id;
    if (!id) return;

    this.patientService.suspendPatient(id).subscribe({
      next: () => {
        this.notification.success('Patient suspended successfully.');
        this.loadPatient(id);
      },
      error: (err) => {
        if (err.status === 403) {
          this.notification.error('You do not have permission to suspend patients.');
        } else {
          this.notification.error('Failed to suspend patient.');
        }
      }
    });
  }

  activate(): void {
    const id = this.patient()?.id;
    if (!id) return;

    this.patientService.activatePatient(id).subscribe({
      next: () => {
        this.notification.success('Patient activated successfully.');
        this.loadPatient(id);
      },
      error: (err) => {
        if (err.status === 403) {
          this.notification.error('You do not have permission to activate patients.');
        } else {
          this.notification.error('Failed to activate patient.');
        }
      }
    });
  }

  delete(): void {
    const id = this.patient()?.id;
    if (!id) return;

    if (!confirm('Are you sure you want to delete this patient?')) return;

    this.patientService.deletePatient(id).subscribe({
      next: () => {
        this.notification.success('Patient deleted successfully.');
        this.router.navigate(['/patients']);
      },
      error: (err) => {
        if (err.status === 403) {
          this.notification.error('You do not have permission to delete patients.');
        } else {
          this.notification.error('Failed to delete patient.');
        }
      }
    });
  }
}
```

**Key additions**:
- **AuthService injection**: `private authService = inject(AuthService)`
- **AppRoles exposure**: `protected readonly AppRoles = AppRoles` allows template access
- **Computed permissions**: `canDelete` and `canSuspend` signals reactively compute based on current user roles
- **403 error handling**: Check `err.status === 403` to show user-friendly messages when backend denies action

**Step 2**: Update template to conditionally show actions:

```html
<!-- File: Frontend/Angular/Scheduling.AngularApp/src/app/features/patients/patient-detail/patient-detail.html -->

@if (isLoading()) {
  <mat-spinner />
} @else if (patient(); as p) {
  <div class="detail-header">
    <h1>{{ p.firstName }} {{ p.lastName }}</h1>
    @if (!isDeleted() && canDelete()) {
      <button mat-icon-button color="warn" (click)="delete()" aria-label="Delete patient">
        <mat-icon>delete</mat-icon>
      </button>
    }
  </div>

  <mat-card>
    <mat-card-content>
      <p><strong>Email:</strong> {{ p.email }}</p>
      <p><strong>Status:</strong> {{ p.status }}</p>
      <p><strong>Date of birth:</strong> {{ p.dateOfBirth | date }}</p>
    </mat-card-content>
    <mat-card-actions>
      @if (!isDeleted() && canSuspend()) {
        <button mat-flat-button color="warn" (click)="isSuspended() ? activate() : suspend()">
          {{ isSuspended() ? 'Activate' : 'Suspend' }}
        </button>
      }
      <button mat-button (click)="router.navigate(['/patients'])">Back to list</button>
    </mat-card-actions>
  </mat-card>
}
```

**Template changes**:
- **Delete button**: Wrapped with `@if (canDelete())` - only admins see this
- **Suspend/Activate button**: Wrapped with `@if (canSuspend())` - doctors and admins see this
- **Graceful degradation**: If user lacks permissions, buttons simply don't render (no broken UI state)

> **Pattern**: Angular's `@if` control flow (new in Angular 17+) is used instead of `*ngIf`. Computed signals (`canDelete`, `canSuspend`) automatically update when user roles change (e.g., after re-login with different account).

---

### Handling 403 from API

Even with frontend checks, users might trigger forbidden actions (e.g., via browser DevTools, race conditions, stale UI state). Handle 403 responses gracefully:

**Error handling pattern** (shown in component above):

```typescript
// In suspend(), activate(), delete() methods
error: (err) => {
  if (err.status === 403) {
    this.notification.error('You do not have permission to perform this action.');
  } else {
    this.notification.error('Failed to suspend patient.');
  }
}
```

**Why this matters**:
- **User sees friendly message**: "You do not have permission..." instead of generic error
- **Logged for debugging**: Backend logs the 403 attempt with user context
- **No UI crash**: Error is caught and handled, app remains stable

**Alternative approach**: Global HTTP error interceptor (created in doc 05) could automatically redirect to `/forbidden` on 403:

```typescript
// File: Frontend/Angular/Scheduling.AngularApp/src/app/core/interceptors/auth.interceptor.ts (modification)

if (err.status === 403) {
  this.router.navigate(['/forbidden']);  // Automatically redirect on any 403
  return throwError(() => err);
}
```

Choose the approach that fits your UX:
- **Component-level handling**: Show notification, keep user on page (less disruptive)
- **Global redirect**: Navigate to Forbidden page (more explicit, better for route guards)

---

## 11. Common Issues

### `User.Roles` is empty even though the IDP issued role claims

**Symptom**: `/auth/current-user` returns `"roles": []`, or `User.IsInRole("Admin")` returns `false`, even after the user logs in with an account that has roles assigned.

**Root cause**: .NET 9's `JsonWebTokenHandler` does not remap JWT claim names to their `ClaimTypes` URI equivalents. The `role` claim from the IDP lands in `ClaimsPrincipal.Claims` with the key `"role"`, not `ClaimTypes.Role`. An implementation that only calls `user.FindAll(ClaimTypes.Role)` will find nothing.

**Fix**: Read both forms and de-duplicate, as shown in the `HttpContextCurrentUser.Roles` implementation in section 2 of this document:

```csharp
var mapped = user.FindAll(ClaimTypes.Role).Select(c => c.Value);
var raw    = user.FindAll("role").Select(c => c.Value);
return mapped.Concat(raw).Distinct().ToList();
```

**Also check**: If `Roles` is still empty after the fix, the `role` claims may not have been included in the cookie at all. This is a separate issue: Duende's lean `id_token` default means only `sub` is returned unless the OIDC middleware is configured to call the userinfo endpoint. Verify that `options.GetClaimsFromUserInfoEndpoint = true` and `options.Scope.Add("roles")` are set in `AuthExtensions.cs`. See doc 03, "Only `sub` claim is present after login".

### `User.Name` or `User.Email` is null

**Symptom**: `ICurrentUser.Name` or `Email` returns null after login.

**Root cause**: Same `JsonWebTokenHandler` naming issue as above. Additionally, the `email` scope must be requested explicitly — it is not part of the `profile` scope.

**Fix**: Confirm `options.Scope.Add("email")` is present alongside `"profile"`, and that `options.GetClaimsFromUserInfoEndpoint = true` is set. See doc 03 for the full scope configuration.

---

## 12. Summary

### What We Built

| Component | Location | Purpose |
|-----------|----------|---------|
| `ICurrentUser` | `BuildingBlocks.Application/Abstractions` | Interface for accessing current user information |
| `HttpContextCurrentUser` | `BuildingBlocks.Infrastructure.Auth` | Implementation reading from HTTP context claims |
| `UserValidator<T>` | `BuildingBlocks.Application/Validators` | Base validator with role-based authorization |
| Role validation | Validators | AND/OR logic for complex role requirements |
| Testing support | Test projects | Mocking `ICurrentUser` for unit tests |
| `Forbidden` component | `Frontend/.../features/forbidden/` | Access denied page |
| Role-based templates | Patient detail, patient list | Show/hide actions by role |

### Key Concepts

1. **Abstraction in Application Layer**: `ICurrentUser` lives in Application, not Domain or Infrastructure. This maintains Clean Architecture principles.

2. **AND/OR Role Logic**:
   - Inner collection = AND (user must have ALL roles)
   - Outer array = OR (user must satisfy at least ONE group)

3. **UserValidator is for role-gated validators only**:
   - `UserValidator(ICurrentUser, params roleGroups)` — pass one or more role groups
   - For "any authenticated user" validators, extend `AbstractValidator<T>` directly — `[Authorize]` on the controller covers the authentication check

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
6. **[06-user-context-and-authorization.md](./06-user-context-and-authorization.md)** *(this document)* - ICurrentUser abstraction, UserValidator activation, role-based authorization, Angular role-based UI

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
