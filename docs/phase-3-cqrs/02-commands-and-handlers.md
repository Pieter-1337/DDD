# Commands and Command Handlers

## What Are Commands?

Commands represent an **intent to change** the system state. They're named imperatively (verb + noun).

```csharp
CreatePatientCommand      // Intent: Create a new patient
SuspendPatientCommand     // Intent: Suspend an existing patient
UpdateContactInfoCommand  // Intent: Update patient's contact information
```

---

## What You Need To Do

### Step 1: Create the Commands folder structure

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
        └── UpdateContactInfo/
            ├── UpdateContactInfoCommand.cs
            └── UpdateContactInfoCommandHandler.cs
```

**Note:** Each command gets its own folder with command + handler together.

### Step 2: Create CreatePatientCommand

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/CreatePatient/CreatePatientCommand.cs`

```csharp
using MediatR;

namespace Scheduling.Application.Patients.Commands.CreatePatient;

public record CreatePatientCommand(
    string FirstName,
    string LastName,
    string Email,
    DateTime DateOfBirth,
    string? PhoneNumber
) : IRequest<Guid>;
```

**Key points:**
- `record` type for immutability
- `IRequest<Guid>` - returns the new Patient's ID
- Properties match what the domain needs

### Step 3: Create CreatePatientCommandHandler

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/CreatePatient/CreatePatientCommandHandler.cs`

```csharp
using BuildingBlocks.Domain.Interfaces;
using MediatR;
using Scheduling.Application.Patients.Events;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands.CreatePatient;

public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;

    public CreatePatientCommandHandler(IUnitOfWork unitOfWork, IMediator mediator)
    {
        _unitOfWork = unitOfWork;
        _mediator = mediator;
    }

    public async Task<Guid> Handle(CreatePatientCommand command, CancellationToken cancellationToken)
    {
        // Create via domain factory method (validates)
        var patient = Patient.Create(
            command.FirstName,
            command.LastName,
            command.Email,
            command.DateOfBirth,
            command.PhoneNumber);

        // Add to repository
        _unitOfWork.RepositoryFor<Patient>().Add(patient);

        // Save changes
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Publish event explicitly after successful save
        await _mediator.Publish(new PatientCreatedEvent(
            patient.Id,
            patient.FirstName!,
            patient.LastName!,
            patient.Email!), cancellationToken);

        return patient.Id;
    }
}
```

**Key points:**
- Implements `IRequestHandler<TRequest, TResponse>`
- Injects both `IUnitOfWork` and `IMediator`
- Calls domain factory method - domain validates
- Publishes event explicitly after successful save
- Returns the created ID

### Step 4: Create SuspendPatientCommand

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/SuspendPatient/SuspendPatientCommand.cs`

```csharp
using MediatR;

namespace Scheduling.Application.Patients.Commands.SuspendPatient;

public record SuspendPatientCommand(Guid PatientId) : IRequest;
```

**Note:** `IRequest` without generic = returns `Unit` (void).

### Step 5: Create SuspendPatientCommandHandler

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/SuspendPatient/SuspendPatientCommandHandler.cs`

```csharp
using BuildingBlocks.Domain.Interfaces;
using MediatR;
using Scheduling.Application.Exceptions;
using Scheduling.Application.Patients.Events;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands.SuspendPatient;

public class SuspendPatientCommandHandler : IRequestHandler<SuspendPatientCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;

    public SuspendPatientCommandHandler(IUnitOfWork unitOfWork, IMediator mediator)
    {
        _unitOfWork = unitOfWork;
        _mediator = mediator;
    }

    public async Task Handle(SuspendPatientCommand command, CancellationToken cancellationToken)
    {
        var patient = await _unitOfWork.RepositoryFor<Patient>()
            .GetByIdAsync(command.PatientId, cancellationToken);

        if (patient is null)
            throw new PatientNotFoundException(command.PatientId);

        // Check if already suspended (no event needed)
        if (patient.Status == PatientStatus.Suspended)
            return;

        // Call domain method
        patient.Suspend();

        // Save changes
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Publish event explicitly
        await _mediator.Publish(new PatientSuspendedEvent(patient.Id), cancellationToken);
    }
}
```

### Step 6: Create Application Exception

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

### Step 7: Create UpdateContactInfoCommand

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

### Step 8: Create UpdateContactInfoCommandHandler

Location: `Core/Scheduling/Scheduling.Application/Patients/Commands/UpdateContactInfo/UpdateContactInfoCommandHandler.cs`

```csharp
using BuildingBlocks.Domain.Interfaces;
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

### Step 9: Ensure MediatR is registered

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

### Step 10: Update the Controller

Location: `WebApi/Controllers/PatientsController.cs`

```csharp
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Application.Patients.Commands.CreatePatient;
using Scheduling.Application.Patients.Commands.SuspendPatient;

namespace WebApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PatientsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PatientsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Guid>> Create(CreatePatientCommand command)
    {
        var patientId = await _mediator.Send(command);
        return Ok(patientId);
    }

    [HttpPost("{id:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Suspend(Guid id)
    {
        await _mediator.Send(new SuspendPatientCommand(id));
        return Ok();
    }
}
```

**Note:** Controller is thin - just dispatches to MediatR.

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

### 4. Let Domain Validate

```csharp
// GOOD - domain validates
public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
{
    var patient = Patient.Create(cmd.FirstName, ...);  // Domain throws if invalid
    ...
}

// BAD - handler validates domain rules
public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
{
    if (string.IsNullOrEmpty(cmd.FirstName))
        throw new ValidationException("First name required");  // Domain rule in handler!

    var patient = new Patient { FirstName = cmd.FirstName };  // Bypasses domain
    ...
}
```

---

## Testing Commands

```csharp
public class CreatePatientCommandHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_ReturnsPatientId()
    {
        // Arrange
        var unitOfWork = new MockUnitOfWork();
        var handler = new CreatePatientCommandHandler(unitOfWork);
        var command = new CreatePatientCommand(
            "John", "Doe", "john@example.com",
            DateTime.UtcNow.AddYears(-30), null);

        // Act
        var patientId = await handler.Handle(command, CancellationToken.None);

        // Assert
        patientId.Should().NotBeEmpty();
        unitOfWork.SaveChangesWasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_InvalidEmail_ThrowsDomainException()
    {
        // Arrange
        var unitOfWork = new MockUnitOfWork();
        var handler = new CreatePatientCommandHandler(unitOfWork);
        var command = new CreatePatientCommand(
            "John", "Doe", "invalid-email",  // No @
            DateTime.UtcNow.AddYears(-30), null);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.Handle(command, CancellationToken.None));
    }
}
```

---

## Verification Checklist

- [ ] `CreatePatientCommand` record created
- [ ] `CreatePatientCommandHandler` implements `IRequestHandler`
- [ ] `SuspendPatientCommand` record created
- [ ] `SuspendPatientCommandHandler` implements `IRequestHandler`
- [ ] `PatientNotFoundException` exception created
- [ ] MediatR registered in DI
- [ ] Controller uses `IMediator.Send()`
- [ ] Handlers inject both `IUnitOfWork` and `IMediator`
- [ ] Events published explicitly after `SaveChangesAsync()`
- [ ] Domain validates (not handlers)

---

## Folder Structure After This Step

```
Core/Scheduling/
└── Scheduling.Application/
    ├── Exceptions/
    │   └── PatientNotFoundException.cs
    ├── Patients/
    │   ├── Commands/
    │   │   ├── CreatePatient/
    │   │   │   ├── CreatePatientCommand.cs
    │   │   │   └── CreatePatientCommandHandler.cs
    │   │   ├── SuspendPatient/
    │   │   │   ├── SuspendPatientCommand.cs
    │   │   │   └── SuspendPatientCommandHandler.cs
    │   │   └── UpdateContactInfo/
    │   │       ├── UpdateContactInfoCommand.cs
    │   │       └── UpdateContactInfoCommandHandler.cs
    │   ├── Events/                        ← Events live in Application
    │   │   ├── PatientCreatedEvent.cs
    │   │   └── PatientSuspendedEvent.cs
    │   └── EventHandlers/
    │       └── PatientCreatedEventHandler.cs
    └── ServiceCollectionExtensions.cs
```

---

→ Next: [03-queries-and-handlers.md](./03-queries-and-handlers.md) - Implementing the read side
