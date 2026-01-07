# CQRS - Command Query Responsibility Segregation

## What Is CQRS?

CQRS separates **reading data** (queries) from **writing data** (commands) into different models.

```
Traditional Approach:
┌─────────────────────────────────────┐
│           Single Model              │
│  (Same classes for read & write)    │
└─────────────────────────────────────┘

CQRS Approach:
┌─────────────────┐   ┌─────────────────┐
│  Command Model  │   │   Query Model   │
│  (Write Side)   │   │  (Read Side)    │
└─────────────────┘   └─────────────────┘
```

---

## Why CQRS?

### Problem: Reads and Writes Have Different Needs

**Writes (Commands):**
- Need validation
- Need business rules
- Need domain events
- Need consistency
- Usually single entity

**Reads (Queries):**
- Need speed
- Need flexibility (joins, projections)
- Need caching
- Don't need validation
- Often span multiple entities

### Without CQRS

```csharp
// Controller does too much
[HttpPost]
public async Task<ActionResult<PatientDto>> CreatePatient(CreatePatientRequest request)
{
    // Validation mixed with business logic
    if (string.IsNullOrEmpty(request.Email))
        return BadRequest("Email required");

    // Domain logic in controller
    var patient = new Patient { ... };

    // Infrastructure in controller
    _context.Patients.Add(patient);
    await _context.SaveChangesAsync();

    // Mapping in controller
    return Ok(new PatientDto { ... });
}

[HttpGet("{id}")]
public async Task<ActionResult<PatientDetailDto>> GetPatient(Guid id)
{
    // Complex query with multiple joins
    var patient = await _context.Patients
        .Include(p => p.Appointments)
        .Include(p => p.MedicalRecords)
        .FirstOrDefaultAsync(p => p.Id == id);

    // Complex mapping
    return Ok(MapToDetailDto(patient));
}
```

**Problems:**
- Controllers are bloated
- Hard to test business logic
- Validation scattered everywhere
- No clear separation of concerns
- Can't optimize reads independently

### With CQRS

```csharp
// Controller is thin
[HttpPost]
public async Task<ActionResult<Guid>> CreatePatient(CreatePatientCommand command)
{
    var patientId = await _mediator.Send(command);
    return Ok(patientId);
}

[HttpGet("{id}")]
public async Task<ActionResult<PatientDetailDto>> GetPatient(Guid id)
{
    var patient = await _mediator.Send(new GetPatientByIdQuery(id));
    return patient is null ? NotFound() : Ok(patient);
}

// Command handler - focused on write logic
public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    public async Task<Guid> Handle(CreatePatientCommand command, CancellationToken ct)
    {
        var patient = Patient.Create(command.FirstName, command.LastName, ...);
        _unitOfWork.RepositoryFor<Patient>().Add(patient);
        await _unitOfWork.SaveChangesAsync(ct);
        return patient.Id;
    }
}

// Query handler - focused on read optimization
public class GetPatientByIdQueryHandler : IRequestHandler<GetPatientByIdQuery, PatientDetailDto?>
{
    public async Task<PatientDetailDto?> Handle(GetPatientByIdQuery query, CancellationToken ct)
    {
        return await _context.Patients
            .Where(p => p.Id == query.PatientId)
            .Select(p => new PatientDetailDto { ... })  // Project directly, no tracking
            .FirstOrDefaultAsync(ct);
    }
}
```

**Benefits:**
- Thin controllers (just dispatch)
- Testable handlers
- Commands and queries are explicit
- Can optimize reads independently
- Clear separation of concerns

---

## CQRS Components

### Commands

Commands represent **intent to change** state:

```csharp
public record CreatePatientCommand(
    string FirstName,
    string LastName,
    string Email,
    DateTime DateOfBirth,
    string? PhoneNumber
) : IRequest<Guid>;

public record SuspendPatientCommand(Guid PatientId) : IRequest;
```

**Naming:** Verb + Noun + "Command" → `CreatePatientCommand`, `CancelAppointmentCommand`

### Command Handlers

Handle the command, apply business logic, persist changes:

```csharp
public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    private readonly IUnitOfWork _unitOfWork;

    public async Task<Guid> Handle(CreatePatientCommand command, CancellationToken ct)
    {
        // Use domain model
        var patient = Patient.Create(
            command.FirstName,
            command.LastName,
            command.Email,
            command.DateOfBirth,
            command.PhoneNumber);

        // Persist via repository
        _unitOfWork.RepositoryFor<Patient>().Add(patient);
        await _unitOfWork.SaveChangesAsync(ct);

        return patient.Id;
    }
}
```

### Queries

Queries represent **requests for data**:

```csharp
public record GetPatientByIdQuery(Guid PatientId) : IRequest<PatientDto?>;

public record GetActivePatientsQuery(int Page, int PageSize) : IRequest<PagedResult<PatientListDto>>;
```

**Naming:** "Get" + What + "Query" → `GetPatientByIdQuery`, `GetActivePatientsQuery`

### Query Handlers

Fetch and project data (no domain logic):

```csharp
public class GetPatientByIdQueryHandler : IRequestHandler<GetPatientByIdQuery, PatientDto?>
{
    private readonly SchedulingDbContext _context;

    public async Task<PatientDto?> Handle(GetPatientByIdQuery query, CancellationToken ct)
    {
        return await _context.Patients
            .AsNoTracking()
            .Where(p => p.Id == query.PatientId)
            .Select(p => new PatientDto
            {
                Id = p.Id,
                FullName = $"{p.FirstName} {p.LastName}",
                Email = p.Email,
                Status = p.Status.ToString()
            })
            .FirstOrDefaultAsync(ct);
    }
}
```

---

## MediatR - The Dispatcher

MediatR routes commands/queries to their handlers:

```csharp
// Registration (in Program.cs or ServiceCollectionExtensions)
services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(CreatePatientCommandHandler).Assembly));

// Usage
public class PatientsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PatientsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreatePatientCommand command)
    {
        var id = await _mediator.Send(command);  // Routes to CreatePatientCommandHandler
        return Ok(id);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PatientDto>> Get(Guid id)
    {
        var patient = await _mediator.Send(new GetPatientByIdQuery(id));  // Routes to GetPatientByIdQueryHandler
        return patient is null ? NotFound() : Ok(patient);
    }
}
```

---

## Folder Structure

```
Core/Scheduling/
├── Scheduling.Domain/
│   └── Patients/
│       ├── Patient.cs
│       └── Events/
├── Scheduling.Application/
│   ├── Patients/
│   │   ├── Commands/
│   │   │   ├── CreatePatientCommand.cs
│   │   │   ├── CreatePatientCommandHandler.cs
│   │   │   ├── SuspendPatientCommand.cs
│   │   │   └── SuspendPatientCommandHandler.cs
│   │   ├── Queries/
│   │   │   ├── GetPatientByIdQuery.cs
│   │   │   ├── GetPatientByIdQueryHandler.cs
│   │   │   └── Dtos/
│   │   │       └── PatientDto.cs
│   │   └── EventHandlers/
│   │       └── PatientCreatedEventHandler.cs
│   └── ServiceCollectionExtensions.cs
└── Scheduling.Infrastructure/
    └── ...
```

---

## Key Principles

1. **Commands change state** - They modify data
2. **Queries return data** - They don't modify anything
3. **One handler per command/query** - Single responsibility
4. **Handlers are testable** - Inject dependencies
5. **DTOs for queries** - Don't expose domain entities

---

## What You'll Build in This Phase

1. **Commands and Handlers** - Write side for Patient operations
2. **Queries and Handlers** - Read side with DTOs
3. **Validation** - FluentValidation for commands
4. **Pipeline Behaviors** - Cross-cutting concerns (logging, validation, etc.)

---

→ Next: [02-commands-and-handlers.md](./02-commands-and-handlers.md) - Implementing the write side
