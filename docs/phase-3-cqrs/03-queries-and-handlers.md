# Queries and Query Handlers

## What Are Queries?

Queries represent **requests for data**. They don't change state - they just read and return data.

```csharp
GetPatientQuery           // Get a single patient by ID
GetActivePatientsQuery    // Get list of active patients
SearchPatientsQuery       // Search with filters
```

---

## Why Separate Query Handlers?

### Queries Can Be Optimized Independently

```csharp
// Query handler can:
// 1. Project directly to DTO via IEntityDto - efficient SQL projection
// 2. Use IUnitOfWork.RepositoryFor<T>() for consistent access
// 3. Leverage generic FirstOrDefaultAsDtoAsync<TDto>() method
// 4. Hit a read replica database
// 5. Cache results

public class GetPatientQueryHandler : IRequestHandler<GetPatientQuery, PatientDto?>
{
    private readonly IUnitOfWork _uow;

    public GetPatientQueryHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<PatientDto?> Handle(GetPatientQuery query, CancellationToken ct)
    {
        return await _uow.RepositoryFor<Patient>()
            .FirstOrDefaultAsDtoAsync<PatientDto>(p => p.Id == query.Id, ct);
    }
}
```

**Key points:**
- Uses `IUnitOfWork` for repository access (consistent with commands)
- `FirstOrDefaultAsDtoAsync<TDto>` uses `TDto.ToDto` expression for SQL projection
- No manual mapping - the DTO's `ToDto` expression handles projection

---

## What You Need To Do

### Step 1: Create DtoBase and IEntityDto

Location: `BuildingBlocks.Application/DtoBase.cs`

```csharp
namespace BuildingBlocks.Application;

public class DtoBase
{
    public Guid Id { get; set; }
}
```

Location: `BuildingBlocks.Application/IEntityDto.cs`

```csharp
using System.Linq.Expressions;

namespace BuildingBlocks.Application;

public interface IEntityDto<TEntity, TDto> where TDto : IEntityDto<TEntity, TDto>
{
    static abstract Expression<Func<TEntity, TDto>> ToDto { get; }
    static abstract TDto FromEntity(TEntity entity);
}
```

**Key points:**
- `DtoBase` provides common `Id` property for all DTOs
- `IEntityDto<TEntity, TDto>` uses C# 11 static abstract members
- `ToDto` - Expression for SQL projection (used by EF Core)
- `FromEntity` - In-memory mapping when entity is already loaded

### Step 2: Create PatientDto with IEntityDto

Location: `Core/Scheduling/Scheduling.Application/Patients/Dtos/PatientDto.cs`

```csharp
using BuildingBlocks.Application;
using Scheduling.Domain.Patients;
using System.Linq.Expressions;

namespace Scheduling.Application.Patients.Dtos;

public class PatientDto : DtoBase, IEntityDto<Patient, PatientDto>
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string? PhoneNumber { get; set; }
    public PatientStatus Status { get; set; }

    public static PatientDto FromEntity(Patient patient) => new()
    {
        Id = patient.Id,
        FirstName = patient.FirstName,
        LastName = patient.LastName,
        Email = patient.Email,
        DateOfBirth = patient.DateOfBirth,
        PhoneNumber = patient.PhoneNumber,
        Status = patient.Status,
    };

    public static Expression<Func<Patient, PatientDto>> ToDto => p => new PatientDto
    {
        Id = p.Id,
        FirstName = p.FirstName,
        LastName = p.LastName,
        Email = p.Email,
        PhoneNumber = p.PhoneNumber,
        DateOfBirth = p.DateOfBirth,
        Status = p.Status
    };
}
```

**Key points:**
- Inherits from `DtoBase` for common `Id`
- Implements `IEntityDto<Patient, PatientDto>` for generic projection support
- `ToDto` is an `Expression` - EF Core translates it to SQL (only selects needed columns)
- `FromEntity` is for in-memory mapping when you already have the entity

### Step 3: Update Repository with Generic DTO Projection

Location: `BuildingBlocks/BuildingBlocks.Infrastructure/Repository.cs`

Add this method to enable generic DTO projection:

```csharp
public async Task<TDto?> FirstOrDefaultAsDtoAsync<TDto>(
    Expression<Func<TEntity, bool>> filter,
    CancellationToken ct = default)
    where TDto : class, IEntityDto<TEntity, TDto>
{
    return await GetAll(filter)
        .Select(TDto.ToDto)  // Uses static abstract member
        .FirstOrDefaultAsync(ct);
}
```

**How it works:**
- `TDto.ToDto` accesses the static abstract `Expression` property
- EF Core translates the expression to efficient SQL
- Only columns needed by the DTO are selected

### Step 4: Create GetPatientQuery

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/GetPatientQuery.cs`

```csharp
using MediatR;
using Scheduling.Application.Patients.Dtos;

namespace Scheduling.Application.Patients.Queries;

public class GetPatientQuery : IRequest<PatientDto?>
{
    public Guid Id { get; set; }
}
```

**Note:** Using a class with settable properties allows easy initialization: `new GetPatientQuery { Id = patientId }`.

### Step 5: Create GetPatientQueryHandler

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/GetPatientQueryHandler.cs`

```csharp
using BuildingBlocks.Application;
using MediatR;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries;

internal class GetPatientQueryHandler : IRequestHandler<GetPatientQuery, PatientDto?>
{
    private readonly IUnitOfWork _uow;

    public GetPatientQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<PatientDto?> Handle(GetPatientQuery query, CancellationToken cancellationToken)
    {
        return await _uow.RepositoryFor<Patient>()
            .FirstOrDefaultAsDtoAsync<PatientDto>(p => p.Id == query.Id, cancellationToken);
    }
}
```

**Key points:**
- Uses `IUnitOfWork` for repository access (consistent with commands)
- `FirstOrDefaultAsDtoAsync<PatientDto>` automatically uses `PatientDto.ToDto` for projection
- Handler is `internal` - only MediatR needs to access it
- No manual mapping - the DTO's expression handles efficient SQL projection

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
using Microsoft.AspNetCore.Mvc;
using BuildingBlocks.Application;
using Scheduling.Domain.Patients;
using MediatR;
using Scheduling.Application.Patients.Commands;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Application.Patients.Queries;

namespace WebApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PatientsController : ControllerBase
{
    private readonly IUnitOfWork _uow;
    private readonly IMediator _mediator;

    public PatientsController(IUnitOfWork uow, IMediator mediator)
    {
        _uow = uow;
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
        return CreatedAtAction(nameof(GetPatientAsync), new { patientId = response.PatientDto.Id }, response);
    }
}
```

**Key points:**
- Controller injects both `IUnitOfWork` and `IMediator`
- `GetPatientQuery` uses object initializer syntax: `new GetPatientQuery { Id = patientId }`
- `CreatePatientAsync` returns `CreatedAtAction` with location header for REST semantics
- Response includes the full `CreatePatientCommandResponse` with success info and DTO

---

## Query Design Guidelines

### 1. Queries Are Read-Only

```csharp
// GOOD - just reads data using IEntityDto projection
public async Task<PatientDto?> Handle(GetPatientQuery query, CancellationToken ct)
{
    return await _uow.RepositoryFor<Patient>()
        .FirstOrDefaultAsDtoAsync<PatientDto>(p => p.Id == query.Id, ct);
}

// BAD - modifying state in query
public async Task<PatientDto?> Handle(GetPatientQuery query, CancellationToken ct)
{
    var patient = await _uow.RepositoryFor<Patient>().GetByIdAsync(query.Id, ct);
    patient.LastAccessedAt = DateTime.UtcNow;  // WRONG! Queries don't modify
    await _uow.SaveChangesAsync(ct);
    return PatientDto.FromEntity(patient);
}
```

### 2. Use IEntityDto for Projection

```csharp
// GOOD - use IEntityDto.ToDto expression for SQL projection
public async Task<PatientDto?> Handle(GetPatientQuery query, CancellationToken ct)
{
    return await _uow.RepositoryFor<Patient>()
        .FirstOrDefaultAsDtoAsync<PatientDto>(p => p.Id == query.Id, ct);
}

// ALSO GOOD - use FromEntity when entity already loaded
var patient = await _uow.RepositoryFor<Patient>().GetByIdAsync(id, ct);
if (patient is null) return null;
return PatientDto.FromEntity(patient);

// BAD - manual inline mapping (not reusable)
return await _context.Patients
    .Where(p => p.Id == id)
    .Select(p => new PatientDto { Id = p.Id, ... })  // Duplicated everywhere
    .FirstOrDefaultAsync(ct);
```

### 3. ToDto vs FromEntity

```csharp
// ToDto - Expression for SQL projection (efficient, columns selected at DB level)
public static Expression<Func<Patient, PatientDto>> ToDto => p => new PatientDto
{
    Id = p.Id,
    FirstName = p.FirstName,
    // Only these columns selected in SQL
};

// FromEntity - In-memory mapping when entity already loaded
public static PatientDto FromEntity(Patient patient) => new()
{
    Id = patient.Id,
    FirstName = patient.FirstName,
    // Maps already-loaded entity
};
```

### 4. Return DTOs, Not Entities

```csharp
// GOOD - return DTO
public class GetPatientQuery : IRequest<PatientDto?> { ... }

// BAD - returning domain entity (leaks domain to presentation)
public class GetPatientQuery : IRequest<Patient?> { ... }
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

- [ ] `DtoBase` class created in `BuildingBlocks.Application`
- [ ] `IEntityDto<TEntity, TDto>` interface created with `ToDto` and `FromEntity`
- [ ] `PatientDto` implements `IEntityDto<Patient, PatientDto>`
- [ ] `PatientDto` has both `ToDto` expression and `FromEntity` method
- [ ] `FirstOrDefaultAsDtoAsync<TDto>` added to `IRepository` and `Repository`
- [ ] `GetPatientQuery` and `GetPatientQueryHandler` created
- [ ] Query handler uses `IUnitOfWork` with `FirstOrDefaultAsDtoAsync`
- [ ] Controller injects both `IUnitOfWork` and `IMediator`
- [ ] Controller uses `GetPatientQuery { Id = patientId }` syntax

---

## Folder Structure After This Step

```
BuildingBlocks/
└── BuildingBlocks.Application/
    ├── DtoBase.cs                    ← Base class for DTOs with Id
    ├── IEntityDto.cs                 ← Interface for entity DTOs with ToDto/FromEntity
    ├── IRepository.cs                ← Repository interface with FirstOrDefaultAsDtoAsync
    ├── IUnitOfWork.cs
    └── SuccessOrFailureDto.cs

Core/Scheduling/
└── Scheduling.Application/
    ├── Patients/
    │   ├── Commands/
    │   │   ├── CreatePatientCommand.cs
    │   │   ├── CreatePatientCommandHandler.cs
    │   │   ├── SuspendPatientCommand.cs
    │   │   └── SuspendPatientCommandHandler.cs
    │   ├── Dtos/
    │   │   └── PatientDto.cs         ← Implements IEntityDto<Patient, PatientDto>
    │   ├── Queries/
    │   │   ├── GetPatientQuery.cs
    │   │   └── GetPatientQueryHandler.cs
    │   └── EventHandlers/
    │       └── PatientCreatedEventHandler.cs
    └── ServiceCollectionExtensions.cs

WebApi/
└── Controllers/
    └── PatientsController.cs         ← Injects IUnitOfWork and IMediator
```

---

→ Next: [04-validation.md](./04-validation.md) - Validating commands with FluentValidation
