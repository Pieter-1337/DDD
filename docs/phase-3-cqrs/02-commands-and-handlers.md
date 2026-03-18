# Commands and Command Handlers

## What Are Commands?

Commands represent an **intent to change** the system state. They're named imperatively (verb + noun).

```csharp
CreatePatientCommand      // Intent: Create a new patient
SuspendPatientCommand     // Intent: Suspend an existing patient
ActivatePatientCommand    // Intent: Activate an existing patient
UpdateContactInfoCommand  // Intent: Update patient's contact information
```

---

## What You Need To Do

### Step 1: Create SuccessOrFailureDto Base Class

Location: `BuildingBlocks.Application/SuccessOrFailureDto.cs`

```csharp
namespace BuildingBlocks.Application;

public class SuccessOrFailureDto
{
    public bool Success { get; set; }
    public string Message { get; set; }

    /// <summary>
    /// Update this dto with values of another dto.
    /// Useful for combining results from multiple operations.
    /// </summary>
    public void Update(SuccessOrFailureDto dto)
    {
        Success = Success && dto.Success;
        Message = string.IsNullOrWhiteSpace(Message)
            ? dto.Message
            : string.Join("\r\n", Message, dto.Message);
    }
}
```

**Key points:**
- Base class for all command responses
- `Success` - indicates if operation succeeded
- `Message` - human-readable result message
- `Update()` - combines multiple results (ANDs success, concatenates messages)

### Step 2: Create the Commands folder structure

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/`

```
Scheduling.Application/
└── Patients/
    └── Commands/
        ├── CreatePatient/
        │   ├── CreatePatientCommand.cs
        │   └── CreatePatientCommandHandler.cs
        ├── SuspendPatient/
        │   ├── SuspendPatientCommand.cs
        │   └── SuspendPatientCommandHandler.cs
        ├── ActivatePatient/
        │   ├── ActivatePatientCommand.cs
        │   └── ActivatePatientCommandHandler.cs
        └── UpdateContactInfo/
            ├── UpdateContactInfoCommand.cs
            └── UpdateContactInfoCommandHandler.cs
```

**Note:** Each command gets its own folder with command + handler together.

### Step 3: Create Request DTO, Command, and Response

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/CreatePatientCommand.cs`

```csharp
using BuildingBlocks.Application;
using MediatR;

namespace Scheduling.Application.Patients.Commands;

// Command - immutable record wrapping the request
public record CreatePatientCommand(CreatePatientRequest Patient) : IRequest<CreatePatientCommandResponse>;

// Request DTO - input from API
public class CreatePatientRequest
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string? PhoneNumber { get; set; }
}

// Response DTO - inherits from SuccessOrFailureDto
public class CreatePatientCommandResponse : SuccessOrFailureDto
{
    public Guid PatientId { get; set; }
}
```

**Key points:**
- `CreatePatientRequest` - input DTO from API
- `CreatePatientCommand` - immutable record wrapping the request
- `CreatePatientCommandResponse` - inherits `SuccessOrFailureDto`, adds entity-specific data
- Response includes `Success`, `Message`, and the created entity's `PatientId` (Guid)

### Step 4: Create CreatePatientCommandHandler

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/CreatePatientCommandHandler.cs`

```csharp
using BuildingBlocks.Application;
using MediatR;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands;

internal class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, CreatePatientCommandResponse>
{
    private readonly IUnitOfWork _uow;

    public CreatePatientCommandHandler(IUnitOfWork unitOfWork)
    {
        _uow = unitOfWork;
    }

    public async Task<CreatePatientCommandResponse> Handle(CreatePatientCommand cmd, CancellationToken cancellationToken)
    {
        var dto = cmd.Patient;

        // Patient.Create() adds PatientCreatedEvent internally
        var patient = Patient.Create(
            dto.FirstName,
            dto.LastName,
            dto.Email,
            dto.DateOfBirth,
            dto.PhoneNumber);

        // Add to repository
        _uow.RepositoryFor<Patient>().Add(patient);

        // Save changes - events auto-dispatched after save
        await _uow.SaveChangesAsync(cancellationToken);

        // Return response with success info and created entity ID
        return new CreatePatientCommandResponse
        {
            Success = true,
            Message = "Patient successfully saved",
            PatientId = patient.Id
        };
    }
}
```

**Key points:**
- Implements `IRequestHandler<TRequest, TResponse>`
- Injects `IUnitOfWork` only (no IMediator needed)
- Returns `CreatePatientCommandResponse` inheriting from `SuccessOrFailureDto`
- Response includes success status, message, and the created entity's ID (Guid)
- Entity adds events internally, auto-dispatched after save

### Step 5: Create SuspendPatientCommand

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/SuspendPatient/SuspendPatientCommand.cs`

```csharp
using MediatR;

namespace Scheduling.Application.Patients.Commands.SuspendPatient;

public record SuspendPatientCommand(Guid PatientId) : IRequest;
```

**Note:** `IRequest` without generic = returns `Unit` (void).

### Step 6: Create SuspendPatientCommandHandler

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/SuspendPatient/SuspendPatientCommandHandler.cs`

```csharp
using BuildingBlocks.Application;
using MediatR;
using Scheduling.Application.Exceptions;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands.SuspendPatient;

public class SuspendPatientCommandHandler : IRequestHandler<SuspendPatientCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public SuspendPatientCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(SuspendPatientCommand command, CancellationToken cancellationToken)
    {
        var patient = await _unitOfWork.RepositoryFor<Patient>()
            .GetByIdAsync(command.PatientId, cancellationToken);

        if (patient is null)
            throw new PatientNotFoundException(command.PatientId);

        // Call domain method - adds PatientSuspendedEvent internally
        patient.Suspend();

        // Save changes - events auto-dispatched after save
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
```

### Step 9: Create Application Exception

Location: `Core/Scheduling/Scheduling.Application/Exceptions/PatientNotFoundException.cs`

```csharp
namespace Scheduling.Application.Exceptions;

public class PatientNotFoundException : Exception
{
    public PatientNotFoundException(Guid patientId)
        : base($"Patient with ID {patientId} was not found.")
    {
        PatientId = patientId;
    }

    public Guid PatientId { get; }
}
```

### Step 8: Create UpdateContactInfoCommand

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/UpdateContactInfo/UpdateContactInfoCommand.cs`

```csharp
using MediatR;

namespace Scheduling.Application.Patients.Commands.UpdateContactInfo;

public record UpdateContactInfoCommand(
    Guid PatientId,
    string? NewEmail,
    string? NewPhoneNumber
) : IRequest;
```

### Step 9: Create UpdateContactInfoCommandHandler

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/UpdateContactInfo/UpdateContactInfoCommandHandler.cs`

```csharp
using BuildingBlocks.Application;
using MediatR;
using Scheduling.Application.Exceptions;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands.UpdateContactInfo;

public class UpdateContactInfoCommandHandler : IRequestHandler<UpdateContactInfoCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public UpdateContactInfoCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(UpdateContactInfoCommand command, CancellationToken cancellationToken)
    {
        var patient = await _unitOfWork.RepositoryFor<Patient>()
            .GetByIdAsync(command.PatientId, cancellationToken);

        if (patient is null)
            throw new PatientNotFoundException(command.PatientId);

        // Call domain method
        patient.UpdateContactInfo(command.NewEmail, command.NewPhoneNumber);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
```

### Step 10: Ensure MediatR is registered

Location: `Core/Scheduling/Scheduling.Application/ServiceCollectionExtensions.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ServiceCollectionExtensions).Assembly));

        return services;
    }
}
```

### Step 11: Configure ASP.NET Core

By default, ASP.NET Core strips the "Async" suffix from action method names. To use `nameof(GetPatientAsync)` in `CreatedAtAction`, disable this:

Location: `WebApplications/Scheduling.WebApi/Program.cs`

```csharp
builder.Services.AddControllers(options =>
{
    options.SuppressAsyncSuffixInActionNames = false;
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new SmartEnumJsonConverterFactory());
});
```

### Step 12: Update the Controller

Location: `WebApplications/Scheduling.WebApi/Controllers/PatientsController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using BuildingBlocks.Application;
using Scheduling.Domain.Patients;
using MediatR;
using Scheduling.Application.Patients.Commands;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Application.Patients.Queries;

namespace Scheduling.WebApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PatientsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PatientsController(IUnitOfWork uow, IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{patientId}")]
    [ProducesResponseType<PatientDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPatientAsync(Guid patientId)
    {
        var response = await _mediator.Send(new GetPatientQuery { Id = patientId });
        return Ok(response);
    }

    [HttpPost("")]
    [ProducesResponseType<CreatePatientCommandResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePatientAsync(CreatePatientRequest request)
    {
        var response = await _mediator.Send(new CreatePatientCommand(request));
        return CreatedAtAction(nameof(GetPatientAsync), new { patientId = response.PatientId }, response);
    }
}
```

**Key points:**
- Controller injects `IMediator` (IUnitOfWork injected but not used directly)
- Controller receives request DTO, wraps in command, dispatches to MediatR
- Returns `CreatePatientCommandResponse` with `Success`, `Message`, and `PatientId` (Guid)
- Uses `CreatedAtAction` with `nameof()` for type-safe action references
- Query endpoints use `GetPatientQuery` dispatched through MediatR
- `SuppressAsyncSuffixInActionNames = false` allows `nameof(GetPatientAsync)` to work

---

## Command Response Pattern

### SuccessOrFailureDto Base Class

All command responses inherit from `SuccessOrFailureDto`:

```csharp
public class CreatePatientCommandResponse : SuccessOrFailureDto
{
    public Guid PatientId { get; set; }
}

public class SuspendPatientCommandResponse : SuccessOrFailureDto
{
    // No additional data needed
}

public class ActivatePatientCommandResponse : SuccessOrFailureDto
{
    // No additional data needed
}
```

### Combining Multiple Results

Use the `Update()` method when a handler performs multiple operations:

```csharp
public async Task<BatchOperationResponse> Handle(BatchOperationCommand cmd, CancellationToken ct)
{
    var response = new BatchOperationResponse { Success = true };

    foreach (var item in cmd.Items)
    {
        var result = await ProcessItem(item);
        response.Update(result);  // ANDs success, concatenates messages
    }

    return response;
}
```

**How `Update()` works:**
- `Success = Success && dto.Success` - All must succeed for overall success
- `Message` - Concatenates with newline separator

### JSON Response Example

```json
{
    "success": true,
    "message": "Patient successfully saved",
    "patientId": "5affa374-ca0c-43c2-8266-078c20ae50ce"
}
```

---

## Command Design Guidelines

### 1. Commands Are Immutable

Use `record` types:

```csharp
// GOOD - immutable record
public record CreatePatientCommand(string Name) : IRequest<Guid>;

// BAD - mutable class
public class CreatePatientCommand : IRequest<Guid>
{
    public string Name { get; set; }  // Mutable!
}
```

### 2. Commands Should Be Self-Contained

Include everything needed:

```csharp
// GOOD - self-contained
public record CreateAppointmentCommand(
    Guid PatientId,
    Guid DoctorId,
    DateTime ScheduledAt,
    string Reason
) : IRequest<Guid>;

// BAD - requires external context
public record CreateAppointmentCommand(DateTime ScheduledAt) : IRequest<Guid>;
// Where does PatientId come from?
```

### 3. Handlers Should Be Focused

One command = one handler = one operation:

```csharp
// GOOD - focused handler
public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
    {
        var patient = Patient.Create(...);
        _unitOfWork.RepositoryFor<Patient>().Add(patient);
        await _unitOfWork.SaveChangesAsync(ct);
        return patient.Id;
    }
}

// BAD - doing too much
public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
    {
        var patient = Patient.Create(...);
        _unitOfWork.RepositoryFor<Patient>().Add(patient);
        await _unitOfWork.SaveChangesAsync(ct);

        // Side effects in handler - should be in event handler!
        await _emailService.SendWelcomeEmail(patient.Email);
        await _analyticsService.TrackPatientCreation(patient.Id);

        return patient.Id;
    }
}
```

### 4. FluentValidation Validates, Domain Behaves

```csharp
// GOOD - FluentValidation handles all input validation
// Handler trusts data is valid by the time it runs
public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
{
    var req = cmd.Request;
    var patient = Patient.Create(req.FirstName!, req.Email!, ...);  // Data guaranteed valid
    ...
}

// GOOD - Domain focuses on behavior
public void Suspend()
{
    if (Status == PatientStatus.Suspended)
        return; // Idempotent state transition
    Status = PatientStatus.Suspended;
}

public void Activate()
{
    if (Status == PatientStatus.Active)
        return; // Idempotent state transition
    Status = PatientStatus.Active;
}
```

---

## Testing Commands

Integration tests with MSTest, Shouldly, and NBuilder:

```csharp
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
        response.PatientId.ShouldNotBe(default);

        // Verify persisted to database
        var reloadedPatient = await Uow.RepositoryFor<Patient>().GetByIdAsync(response.PatientId);
        reloadedPatient.ShouldNotBeNull();
        reloadedPatient!.FirstName.ShouldBe("John");

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

        // Verify phone number is null
        var reloaded = await Uow.RepositoryFor<Patient>().GetByIdAsync(response.PatientId);
        reloaded.ShouldNotBeNull();
        reloaded!.PhoneNumber.ShouldBeNull();
    }
}
```

**Key points:**
- Extends `SchedulingTestBase` (which extends generic `TestBase<SchedulingDbContext>`)
- Uses NBuilder for test data construction
- Uses Shouldly for fluent assertions
- Tests run in transactions (rolled back after each test)
- Full integration tests through MediatR pipeline

---

## Verification Checklist

- [x] `SuccessOrFailureDto` base class exists in `BuildingBlocks.Application`
- [x] `CreatePatientRequest` DTO class created (inline with command)
- [x] `CreatePatientCommand` record wraps the request DTO
- [x] `CreatePatientCommandResponse` inherits from `SuccessOrFailureDto`
- [x] `CreatePatientCommandHandler` implements `IRequestHandler` and returns response
- [x] `SuspendPatientCommand` class created
- [x] `SuspendPatientCommandHandler` implements `IRequestHandler`
- [x] `ActivatePatientCommand` class created
- [x] `ActivatePatientCommandHandler` implements `IRequestHandler`
- [x] MediatR registered in DI
- [x] `SuppressAsyncSuffixInActionNames = false` configured
- [x] Controller receives request DTO, wraps in command
- [x] Handlers inject `IUnitOfWork` and queue integration events
- [x] FluentValidation validates (not domain)

---

## Folder Structure After This Step

Commands, requests, responses, and validators are all in the same file:

```
Core/Scheduling/
+-- Scheduling.Domain/
|   +-- Patients/
|       +-- Patient.cs                           <- Pure domain entity (no event collection)
|       +-- PatientStatus.cs
+-- Scheduling.Application/
    +-- Patients/
    |   +-- Commands/
    |   |   +-- CreatePatientCommand.cs          <- Command + Request + Response + Validators
    |   |   +-- CreatePatientCommandHandler.cs   <- Queues integration events
    |   |   +-- SuspendPatientCommand.cs         <- Command + Response + Validator
    |   |   +-- SuspendPatientCommandHandler.cs
    |   |   +-- ActivatePatientCommand.cs        <- Command + Response + Validator
    |   |   +-- ActivatePatientCommandHandler.cs
    |   +-- Dtos/
    |       +-- PatientDto.cs
    +-- ServiceCollectionExtensions.cs

Shared/
+-- IntegrationEvents/
    +-- Scheduling/
        +-- PatientCreatedIntegrationEvent.cs
        +-- PatientSuspendedIntegrationEvent.cs
        +-- PatientActivatedIntegrationEvent.cs
```

**Note:** All related types (Command, Request DTO, Response DTO, Validators) are in the same file, organized with `#region Validators`. This keeps related code together and makes it easier to understand the full contract.

**Integration events** are queued in command handlers via `_uow.QueueIntegrationEvent()` and published to RabbitMQ after `SaveChangesAsync()` succeeds.

---

→ Next: [03-queries-and-handlers.md](./03-queries-and-handlers.md) - Implementing the read side
