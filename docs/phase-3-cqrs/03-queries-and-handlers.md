# Queries and Query Handlers

## What Are Queries?

Queries represent **requests for data**. They don't change state - they just read and return data.

```csharp
GetPatientByIdQuery       // Get a single patient
GetActivePatientsQuery    // Get list of active patients
SearchPatientsQuery       // Search with filters
```

---

## Why Separate Query Handlers?

### Queries Can Be Optimized Independently

```csharp
// Query handler can:
// 1. Use AsNoTracking() - no change tracking overhead
// 2. Project directly to DTO - no loading entire entity
// 3. Use raw SQL if needed
// 4. Hit a read replica database
// 5. Cache results

public class GetPatientByIdQueryHandler : IRequestHandler<GetPatientByIdQuery, PatientDto?>
{
    public async Task<PatientDto?> Handle(GetPatientByIdQuery query, CancellationToken ct)
    {
        return await _context.Patients
            .AsNoTracking()                              // No tracking
            .Where(p => p.Id == query.PatientId)
            .Select(p => new PatientDto                  // Direct projection
            {
                Id = p.Id,
                FullName = $"{p.FirstName} {p.LastName}",
                Email = p.Email
            })
            .FirstOrDefaultAsync(ct);
    }
}
```

---

## What You Need To Do

### Step 1: Create DTOs folder

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/Dtos/`

DTOs are simple data containers for query results.

### Step 2: Create PatientDto

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/Dtos/PatientDto.cs`

```csharp
namespace Scheduling.Application.Patients.Queries.Dtos;

public class PatientDto
{
    public required Guid Id { get; init; }
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public required string Status { get; init; }
    public required DateTime DateOfBirth { get; init; }
    public string? PhoneNumber { get; init; }
}
```

**Key points:**
- `required` ensures properties are set
- `init` makes them immutable after construction
- No domain logic - just data

### Step 3: Create PatientListDto

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/Dtos/PatientListDto.cs`

```csharp
namespace Scheduling.Application.Patients.Queries.Dtos;

public class PatientListDto
{
    public required Guid Id { get; init; }
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public required string Status { get; init; }
}
```

**Note:** List DTO has fewer fields - only what's needed for the list view.

### Step 4: Create GetPatientByIdQuery

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/GetPatientById/GetPatientByIdQuery.cs`

```csharp
using MediatR;
using Scheduling.Application.Patients.Queries.Dtos;

namespace Scheduling.Application.Patients.Queries.GetPatientById;

public record GetPatientByIdQuery(Guid PatientId) : IRequest<PatientDto?>;
```

### Step 5: Create GetPatientByIdQueryHandler

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/GetPatientById/GetPatientByIdQueryHandler.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Scheduling.Application.Patients.Queries.Dtos;
using Scheduling.Infrastructure.Persistence;

namespace Scheduling.Application.Patients.Queries.GetPatientById;

public class GetPatientByIdQueryHandler : IRequestHandler<GetPatientByIdQuery, PatientDto?>
{
    private readonly SchedulingDbContext _context;

    public GetPatientByIdQueryHandler(SchedulingDbContext context)
    {
        _context = context;
    }

    public async Task<PatientDto?> Handle(GetPatientByIdQuery query, CancellationToken cancellationToken)
    {
        return await _context.Patients
            .AsNoTracking()
            .Where(p => p.Id == query.PatientId)
            .Select(p => new PatientDto
            {
                Id = p.Id,
                FullName = $"{p.FirstName} {p.LastName}",
                Email = p.Email!,
                Status = p.Status.ToString(),
                DateOfBirth = p.DateOfBirth,
                PhoneNumber = p.PhoneNumber
            })
            .FirstOrDefaultAsync(cancellationToken);
    }
}
```

**Key points:**
- Uses `DbContext` directly (not UnitOfWork) - queries don't need change tracking
- `AsNoTracking()` - better performance for reads
- Projects directly to DTO - no loading entire entity then mapping

### Step 6: Create GetAllPatientsQuery

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/GetAllPatients/GetAllPatientsQuery.cs`

```csharp
using MediatR;
using Scheduling.Application.Patients.Queries.Dtos;

namespace Scheduling.Application.Patients.Queries.GetAllPatients;

public record GetAllPatientsQuery : IRequest<IReadOnlyList<PatientListDto>>;
```

### Step 7: Create GetAllPatientsQueryHandler

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/GetAllPatients/GetAllPatientsQueryHandler.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Scheduling.Application.Patients.Queries.Dtos;
using Scheduling.Infrastructure.Persistence;

namespace Scheduling.Application.Patients.Queries.GetAllPatients;

public class GetAllPatientsQueryHandler : IRequestHandler<GetAllPatientsQuery, IReadOnlyList<PatientListDto>>
{
    private readonly SchedulingDbContext _context;

    public GetAllPatientsQueryHandler(SchedulingDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<PatientListDto>> Handle(
        GetAllPatientsQuery query,
        CancellationToken cancellationToken)
    {
        return await _context.Patients
            .AsNoTracking()
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Select(p => new PatientListDto
            {
                Id = p.Id,
                FullName = $"{p.FirstName} {p.LastName}",
                Email = p.Email!,
                Status = p.Status.ToString()
            })
            .ToListAsync(cancellationToken);
    }
}
```

### Step 8: Create Paginated Query (Optional but common)

Location: `Core/Scheduling/Scheduling.Application/Common/PagedResult.cs`

```csharp
namespace Scheduling.Application.Common;

public class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
```

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/GetPatientsPaged/GetPatientsPagedQuery.cs`

```csharp
using MediatR;
using Scheduling.Application.Common;
using Scheduling.Application.Patients.Queries.Dtos;

namespace Scheduling.Application.Patients.Queries.GetPatientsPaged;

public record GetPatientsPagedQuery(
    int Page = 1,
    int PageSize = 10,
    string? StatusFilter = null
) : IRequest<PagedResult<PatientListDto>>;
```

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/GetPatientsPaged/GetPatientsPagedQueryHandler.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Scheduling.Application.Common;
using Scheduling.Application.Patients.Queries.Dtos;
using Scheduling.Domain.Patients;
using Scheduling.Infrastructure.Persistence;

namespace Scheduling.Application.Patients.Queries.GetPatientsPaged;

public class GetPatientsPagedQueryHandler
    : IRequestHandler<GetPatientsPagedQuery, PagedResult<PatientListDto>>
{
    private readonly SchedulingDbContext _context;

    public GetPatientsPagedQueryHandler(SchedulingDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<PatientListDto>> Handle(
        GetPatientsPagedQuery query,
        CancellationToken cancellationToken)
    {
        var patientsQuery = _context.Patients.AsNoTracking();

        // Apply filter if provided
        if (!string.IsNullOrEmpty(query.StatusFilter) &&
            Enum.TryParse<PatientStatus>(query.StatusFilter, out var status))
        {
            patientsQuery = patientsQuery.Where(p => p.Status == status);
        }

        // Get total count
        var totalCount = await patientsQuery.CountAsync(cancellationToken);

        // Get page of items
        var items = await patientsQuery
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new PatientListDto
            {
                Id = p.Id,
                FullName = $"{p.FirstName} {p.LastName}",
                Email = p.Email!,
                Status = p.Status.ToString()
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<PatientListDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }
}
```

### Step 9: Update Application project reference

The query handlers need access to `SchedulingDbContext`. Add reference to Infrastructure:

Location: `Core/Scheduling/Scheduling.Application/Scheduling.Application.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net9.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <RootNamespace>Scheduling.Application</RootNamespace>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="MediatR" />
        <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
        <PackageReference Include="Microsoft.EntityFrameworkCore" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\Scheduling.Domain\Scheduling.Domain.csproj" />
        <ProjectReference Include="..\Scheduling.Infrastructure\Scheduling.Infrastructure.csproj" />
    </ItemGroup>

</Project>
```

**Note:** This creates a reference from Application → Infrastructure, which some architects avoid. Alternative approaches are discussed below.

### Step 10: Update the Controller

Location: `WebApi/Controllers/PatientsController.cs`

```csharp
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Application.Common;
using Scheduling.Application.Patients.Commands.CreatePatient;
using Scheduling.Application.Patients.Commands.SuspendPatient;
using Scheduling.Application.Patients.Queries.Dtos;
using Scheduling.Application.Patients.Queries.GetAllPatients;
using Scheduling.Application.Patients.Queries.GetPatientById;
using Scheduling.Application.Patients.Queries.GetPatientsPaged;

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

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PatientDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientDto>> GetById(Guid id)
    {
        var patient = await _mediator.Send(new GetPatientByIdQuery(id));
        return patient is null ? NotFound() : Ok(patient);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PatientListDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PatientListDto>>> GetAll()
    {
        var patients = await _mediator.Send(new GetAllPatientsQuery());
        return Ok(patients);
    }

    [HttpGet("paged")]
    [ProducesResponseType<PagedResult<PatientListDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PatientListDto>>> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? status = null)
    {
        var result = await _mediator.Send(new GetPatientsPagedQuery(page, pageSize, status));
        return Ok(result);
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

---

## Query Design Guidelines

### 1. Queries Are Read-Only

```csharp
// GOOD - just reads data
public async Task<PatientDto?> Handle(GetPatientByIdQuery query, CancellationToken ct)
{
    return await _context.Patients
        .AsNoTracking()
        .Select(...)
        .FirstOrDefaultAsync(ct);
}

// BAD - modifying state in query
public async Task<PatientDto?> Handle(GetPatientByIdQuery query, CancellationToken ct)
{
    var patient = await _context.Patients.FindAsync(query.PatientId);
    patient.LastAccessedAt = DateTime.UtcNow;  // WRONG! Queries don't modify
    await _context.SaveChangesAsync(ct);
    return MapToDto(patient);
}
```

### 2. Use Direct Projection

```csharp
// GOOD - project directly to DTO
.Select(p => new PatientDto
{
    Id = p.Id,
    FullName = $"{p.FirstName} {p.LastName}"
})

// BAD - load entity then map
var patient = await _context.Patients.FindAsync(id);
return new PatientDto
{
    Id = patient.Id,
    FullName = $"{patient.FirstName} {patient.LastName}"
};
```

### 3. Always Use AsNoTracking()

```csharp
// GOOD - no tracking overhead
await _context.Patients.AsNoTracking().ToListAsync();

// BAD - tracking entities we'll never modify
await _context.Patients.ToListAsync();
```

### 4. Return DTOs, Not Entities

```csharp
// GOOD - return DTO
public record GetPatientByIdQuery(Guid Id) : IRequest<PatientDto?>;

// BAD - returning domain entity
public record GetPatientByIdQuery(Guid Id) : IRequest<Patient?>;
```

---

## Alternative: Avoiding Application → Infrastructure Reference

If you want to keep Application independent of Infrastructure:

### Option 1: Read-only DbContext interface

```csharp
// In Application layer
public interface ISchedulingReadContext
{
    IQueryable<Patient> Patients { get; }
}

// In Infrastructure layer
public class SchedulingDbContext : DbContext, ISchedulingReadContext
{
    public DbSet<Patient> Patients => Set<Patient>();

    IQueryable<Patient> ISchedulingReadContext.Patients =>
        Set<Patient>().AsNoTracking();
}
```

### Option 2: Dedicated read repository

```csharp
// In Application layer
public interface IPatientReadRepository
{
    Task<PatientDto?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<PatientListDto>> GetAllAsync(CancellationToken ct);
}

// In Infrastructure layer
public class PatientReadRepository : IPatientReadRepository { ... }
```

For this learning project, we'll use the direct DbContext approach for simplicity.

---

## Verification Checklist

- [ ] `PatientDto` and `PatientListDto` created
- [ ] `GetPatientByIdQuery` and handler created
- [ ] `GetAllPatientsQuery` and handler created
- [ ] `GetPatientsPagedQuery` and handler created (optional)
- [ ] `PagedResult<T>` class created
- [ ] Query handlers use `AsNoTracking()`
- [ ] Query handlers project directly to DTOs
- [ ] Controller updated with GET endpoints
- [ ] Application references Infrastructure (or uses abstraction)

---

## Folder Structure After This Step

```
Core/Scheduling/
└── Scheduling.Application/
    ├── Common/
    │   └── PagedResult.cs
    ├── Exceptions/
    │   └── PatientNotFoundException.cs
    ├── Patients/
    │   ├── Commands/
    │   │   ├── CreatePatient/
    │   │   │   ├── CreatePatientCommand.cs
    │   │   │   └── CreatePatientCommandHandler.cs
    │   │   └── SuspendPatient/
    │   │       ├── SuspendPatientCommand.cs
    │   │       └── SuspendPatientCommandHandler.cs
    │   ├── Queries/
    │   │   ├── Dtos/
    │   │   │   ├── PatientDto.cs
    │   │   │   └── PatientListDto.cs
    │   │   ├── GetPatientById/
    │   │   │   ├── GetPatientByIdQuery.cs
    │   │   │   └── GetPatientByIdQueryHandler.cs
    │   │   ├── GetAllPatients/
    │   │   │   ├── GetAllPatientsQuery.cs
    │   │   │   └── GetAllPatientsQueryHandler.cs
    │   │   └── GetPatientsPaged/
    │   │       ├── GetPatientsPagedQuery.cs
    │   │       └── GetPatientsPagedQueryHandler.cs
    │   └── EventHandlers/
    │       └── PatientCreatedEventHandler.cs
    └── ServiceCollectionExtensions.cs
```

---

→ Next: [04-validation.md](./04-validation.md) - Validating commands with FluentValidation
