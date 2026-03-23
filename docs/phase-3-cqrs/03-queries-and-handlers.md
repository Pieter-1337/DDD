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
- `FirstOrDefaultAsDtoAsync<TDto>` uses `TDto.Project` expression for SQL projection
- No manual mapping - the DTO's `Project` expression handles projection

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
    static abstract Expression<Func<TEntity, TDto>> Project { get; }
    static abstract TDto ToDto(TEntity entity);
}
```

**Key points:**
- `DtoBase` provides common `Id` property for all DTOs
- `IEntityDto<TEntity, TDto>` uses C# 11 static abstract members
- `Project` - Expression for SQL projection (used by EF Core)
- `ToDto` - In-memory mapping method when entity is already loaded

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

    // In-memory mapping when entity already loaded
    public static PatientDto ToDto(Patient patient) => new()
    {
        Id = patient.Id,
        FirstName = patient.FirstName,
        LastName = patient.LastName,
        Email = patient.Email,
        DateOfBirth = patient.DateOfBirth,
        PhoneNumber = patient.PhoneNumber,
        Status = patient.Status,
    };

    // Expression for SQL projection
    public static Expression<Func<Patient, PatientDto>> Project => p => new PatientDto
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
- `Project` is an `Expression` - EF Core translates it to SQL (only selects needed columns)
- `ToDto` is a method for in-memory mapping when you already have the entity

### Step 3: Update Repository with Query Methods

Location: `BuildingBlocks/BuildingBlocks.Infrastructure/Repository.cs`

Add these methods to enable generic querying and DTO projection:

```csharp
// Get all entities as list
public async Task<IEnumerable<TEntity>> GetAllAsListAsync(
    Expression<Func<TEntity, bool>>? filter = null,
    CancellationToken ct = default)
{
    return await GetAll(filter).ToListAsync(ct);
}

// Get all entities projected to DTOs
public async Task<IEnumerable<TDto>> GetAllAsDtosAsync<TDto>(
    Expression<Func<TEntity, bool>>? filter = null,
    CancellationToken ct = default)
    where TDto : class, IEntityDto<TEntity, TDto>
{
    return await GetAll(filter)
        .Select(TDto.Project)
        .ToListAsync(ct);
}

// Get single entity projected to DTO
public async Task<TDto?> FirstOrDefaultAsDtoAsync<TDto>(
    Expression<Func<TEntity, bool>> filter,
    CancellationToken ct = default)
    where TDto : class, IEntityDto<TEntity, TDto>
{
    return await GetAll(filter)
        .Select(TDto.Project)
        .FirstOrDefaultAsync(ct);
}

// Get single entity via navigation property projected to DTO
public async Task<TDto?> FirstOrDefaultAsDtoAsync<TDto, TNavigation>(
    Expression<Func<TEntity, bool>> filter,
    Expression<Func<TEntity, TNavigation>> navigation,
    CancellationToken ct = default)
    where TNavigation : class
    where TDto : class, IEntityDto<TNavigation, TDto>
{
    return await GetAll(filter)
        .Select(navigation)       // Entity → Navigation property
        .Select(TDto.Project)     // Navigation → DTO
        .FirstOrDefaultAsync(ct);
}

// Inline projection to any type
public async Task<TResult?> FirstOrDefaultWithProjectionAsync<TResult>(
    Expression<Func<TEntity, bool>> filter,
    Expression<Func<TEntity, TResult>> projection,
    CancellationToken ct = default)
{
    return await GetAll(filter)
        .Select(projection)
        .FirstOrDefaultAsync(ct);
}
```

**Method summary:**

| Method | Use Case |
|--------|----------|
| `GetAllAsListAsync` | Get list of entities |
| `GetAllAsDtosAsync<TDto>` | Get list of DTOs |
| `FirstOrDefaultAsDtoAsync<TDto>` | Single entity → DTO |
| `FirstOrDefaultAsDtoAsync<TDto, TNav>` | Entity → Navigation → DTO |
| `FirstOrDefaultWithProjectionAsync` | Inline ad-hoc projection |

**How it works:**
- `TDto.Project` accesses the static abstract `Expression` property
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
- `FirstOrDefaultAsDtoAsync<PatientDto>` automatically uses `PatientDto.Project` for projection
- Handler is `internal` - only MediatR needs to access it
- No manual mapping - the DTO's expression handles efficient SQL projection

### Step 6: Create GetAllPatientsQuery

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/GetAllPatientsQuery.cs`

```csharp
using MediatR;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries;

public class GetAllPatientsQuery : IRequest<IEnumerable<PatientDto>>
{
    public PatientStatus Status { get; set; }
}
```

**Note:** Filter by `PatientStatus` to get only patients with a specific status.

### Step 7: Create GetAllPatientsQueryHandler

Location: `Core/Scheduling/Scheduling.Application/Patients/Queries/GetAllPatientsQueryHandler.cs`

```csharp
using BuildingBlocks.Application;
using MediatR;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries;

internal class GetAllPatientsQueryHandler : IRequestHandler<GetAllPatientsQuery, IEnumerable<PatientDto>>
{
    private readonly IUnitOfWork _uow;

    public GetAllPatientsQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<IEnumerable<PatientDto>> Handle(GetAllPatientsQuery query, CancellationToken cancellationToken)
    {
        return await _uow.RepositoryFor<Patient>()
            .GetAllAsDtosAsync<PatientDto>(p => p.Status == query.Status, cancellationToken);
    }
}
```

**Key points:**
- Uses `IUnitOfWork` for repository access (consistent pattern)
- `GetAllAsDtosAsync<PatientDto>` uses the DTO's `Project` expression
- Filter passed directly to repository method
- Handler is `internal` - only MediatR accesses it

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
        return CreatedAtAction(nameof(GetPatientAsync), new { patientId = response.PatientId }, response);
    }
}
```

**Key points:**
- Controller injects both `IUnitOfWork` and `IMediator`
- `GetPatientQuery` uses object initializer syntax: `new GetPatientQuery { Id = patientId }`
- `CreatePatientAsync` returns `CreatedAtAction` with location header for REST semantics
- Response includes the full `CreatePatientCommandResponse` with success info and entity ID

---

## Dynamic Query Composition with PredicateBuilder

### What Is PredicateBuilder?

`PredicateBuilder` is a utility for dynamically composing LINQ predicate expressions (`Expression<Func<T, bool>>`) at runtime. It lives in `BuildingBlocks.Domain.Specifications` and has zero dependencies—it uses pure expression trees that EF Core can translate to SQL.

### Where Does It Live?

```
BuildingBlocks/
└── BuildingBlocks.Domain/
    └── Specifications/
        └── PredicateBuilder.cs
```

No package references required. It's a self-contained utility using only `System.Linq.Expressions`.

### Why Is It Useful?

Consider a query with optional filters from a search form:

```csharp
// Without PredicateBuilder - this breaks when status is null
public async Task<IEnumerable<PatientDto>> Handle(GetAllPatientsQuery query, CancellationToken ct)
{
    return await _uow.RepositoryFor<Patient>()
        .GetAllAsDtosAsync<PatientDto>(p => p.Status == query.Status, ct);
        // ❌ When query.Status is null, this queries for patients where Status == null
}
```

**Problems:**
- Can't conditionally apply filters
- Hardcoded predicate fails with null values
- No way to combine multiple optional filters dynamically

**Solution with PredicateBuilder:**

```csharp
// Start with a base that matches everything
var predicate = PredicateBuilder.BaseAnd<Patient>();

// Conditionally add filters
if (!string.IsNullOrWhiteSpace(query.Status))
{
    var status = PatientStatus.FromName(query.Status);
    predicate = predicate.And(p => p.Status == status);
}

// If no filters were added, predicate is still valid (matches all)
return await _uow.RepositoryFor<Patient>()
    .GetAllAsDtosAsync<PatientDto>(predicate, ct);
```

**Benefits:**
- Conditional filtering without string-based queries
- Composable AND/OR/NOT operations
- Type-safe at compile time
- Works with EF Core's expression tree translation
- No SQL injection risk

### API Overview

| Method | Purpose | Example |
|--------|---------|---------|
| `BaseAnd<T>()` | Start an AND chain (`p => true`) | `var predicate = PredicateBuilder.BaseAnd<Patient>();` |
| `BaseOr<T>()` | Start an OR chain (`p => false`) | `var predicate = PredicateBuilder.BaseOr<Patient>();` |
| `Create<T>(expr)` | Wrap an expression | `var predicate = PredicateBuilder.Create<Patient>(p => p.IsActive);` |
| `.And(expr)` | Combine with AND | `predicate = predicate.And(p => p.Status == status);` |
| `.Or(expr)` | Combine with OR | `predicate = predicate.Or(p => p.Status == other);` |
| `.Not()` | Negate the predicate | `predicate = predicate.Not();` |
| `.EqualsBaseAnd()` | Check if still `p => true` | `if (!predicate.EqualsBaseAnd()) { /* filters applied */ }` |
| `.EqualsBaseOr()` | Check if still `p => false` | `if (!predicate.EqualsBaseOr()) { /* filters applied */ }` |

### Real-World Example: GetAllPatientsQueryHandler

**Before - Broken with null status:**

```csharp
public async Task<IEnumerable<PatientDto>> Handle(GetAllPatientsQuery query, CancellationToken ct)
{
    // ❌ This queries for Status == null when query.Status is null
    return await _uow.RepositoryFor<Patient>()
        .GetAllAsDtosAsync<PatientDto>(p => p.Status == query.Status, ct);
}
```

**After - Correct conditional filtering:**

```csharp
public async Task<IEnumerable<PatientDto>> Handle(GetAllPatientsQuery query, CancellationToken ct)
{
    // Start with a base predicate that matches all patients (p => true)
    var predicate = PredicateBuilder.BaseAnd<Patient>();

    // Only add the status filter if a status was provided
    if (!string.IsNullOrWhiteSpace(query.Status))
    {
        var status = PatientStatus.FromName(query.Status);
        predicate = predicate.And(p => p.Status == status);
    }

    // If query.Status was null/empty, predicate is still (p => true) - matches all patients
    // If query.Status was provided, predicate is (p => p.Status == status)
    return await _uow.RepositoryFor<Patient>()
        .GetAllAsDtosAsync<PatientDto>(predicate, ct);
}
```

### More Complex Example: Multiple Optional Filters

Search form with many optional fields:

```csharp
public class SearchPatientsQuery : IRequest<IEnumerable<PatientDto>>
{
    public string? Status { get; set; }
    public string? EmailSearchTerm { get; set; }
    public string? LastNameSearchTerm { get; set; }
    public DateTime? RegisteredAfter { get; set; }
}

public class SearchPatientsQueryHandler : IRequestHandler<SearchPatientsQuery, IEnumerable<PatientDto>>
{
    private readonly IUnitOfWork _uow;

    public async Task<IEnumerable<PatientDto>> Handle(SearchPatientsQuery query, CancellationToken ct)
    {
        var predicate = PredicateBuilder.BaseAnd<Patient>();

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = PatientStatus.FromName(query.Status);
            predicate = predicate.And(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.EmailSearchTerm))
        {
            predicate = predicate.And(p => p.Email.Contains(query.EmailSearchTerm));
        }

        if (!string.IsNullOrWhiteSpace(query.LastNameSearchTerm))
        {
            predicate = predicate.And(p => p.LastName.Contains(query.LastNameSearchTerm));
        }

        if (query.RegisteredAfter.HasValue)
        {
            predicate = predicate.And(p => p.CreatedAt >= query.RegisteredAfter.Value);
        }

        // Each filter is conditionally added—only the ones with values are included
        return await _uow.RepositoryFor<Patient>()
            .GetAllAsDtosAsync<PatientDto>(predicate, ct);
    }
}
```

**Generated SQL when only `Status` and `EmailSearchTerm` are provided:**

```sql
SELECT p.Id, p.FirstName, p.LastName, p.Email, p.Status
FROM Patients p
WHERE p.Status = @status
  AND p.Email LIKE '%' + @emailTerm + '%'
```

Only the filters with values are included in the WHERE clause.

### OR Example: Match Any of Multiple Criteria

```csharp
// Find patients who are either Active OR have a recent appointment
var predicate = PredicateBuilder.BaseOr<Patient>();
predicate = predicate.Or(p => p.Status == PatientStatus.Active);
predicate = predicate.Or(p => p.Appointments.Any(a => a.Date >= DateTime.UtcNow.AddDays(-30)));

// Equivalent to: WHERE (p.Status = 'Active') OR (p.Appointments have recent dates)
```

### NOT Example: Exclude Criteria

```csharp
// Find all patients who are NOT suspended
var predicate = PredicateBuilder.Create<Patient>(p => p.Status == PatientStatus.Suspended);
predicate = predicate.Not();

// Equivalent to: WHERE NOT (p.Status = 'Suspended')
```

### When to Use vs When Not to Use

**Use PredicateBuilder when:**
- Query has multiple optional filters from user input (search forms, filter dropdowns)
- Need to combine filters with OR logic
- Need negation (NOT)
- Building complex dynamic queries at runtime

**Don't use PredicateBuilder when:**
- Query has a single required filter → use `.Where(p => p.Id == id)` directly
- Query has one or two optional filters → simple chaining is more readable:
  ```csharp
  var query = _context.Patients.AsQueryable();
  if (status.HasValue)
      query = query.Where(p => p.Status == status);
  return await query.ToListAsync(ct);
  ```

**Readability guideline:** For 1-2 optional filters, simple `.Where()` chaining is clearer. PredicateBuilder shines when you have 3+ dynamic conditions or need OR/NOT combinations.

### How It Works Under the Hood

The core challenge: You can't just combine two `Expression<Func<T, bool>>` instances with `&&` because each expression has its own parameter (`p1` and `p2`). You need a single shared parameter.

**Problem:**

```csharp
Expression<Func<Patient, bool>> expr1 = p1 => p1.Status == PatientStatus.Active;
Expression<Func<Patient, bool>> expr2 = p2 => p2.Email.Contains("example");

// Can't do this - different parameters (p1 vs p2)
var combined = expr1.Body && expr2.Body;  // ❌ Won't compile
```

**Solution: Parameter Rebinding**

PredicateBuilder uses `ReplaceVisitor` to replace the parameter in the second expression with the parameter from the first:

```csharp
public static Expression<Func<T, bool>> And<T>(
    this Expression<Func<T, bool>> expr1,
    Expression<Func<T, bool>> expr2)
{
    // Replace expr2's parameter (p2) with expr1's parameter (p1)
    var secondBody = expr2.Body.Replace(expr2.Parameters[0], expr1.Parameters[0]);

    // Now both bodies use the same parameter - combine with AndAlso
    return Expression.Lambda<Func<T, bool>>(
        Expression.AndAlso(expr1.Body, secondBody),
        expr1.Parameters);
}

private class ReplaceVisitor(Expression from, Expression to) : ExpressionVisitor
{
    public override Expression Visit(Expression? node)
    {
        return node == from ? to : base.Visit(node)!;
    }
}
```

**Result:**

Both expressions now share the same parameter, so they can be combined into a single expression tree that EF Core translates to SQL:

```csharp
// Before: p1 => p1.Status == Active  AND  p2 => p2.Email.Contains("example")
// After:  p => (p.Status == Active) AND (p.Email.Contains("example"))
```

This technique is from [Joseph Albahari's article on LINQ expressions](http://www.albahari.com/expressions).

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
    return PatientDto.ToDto(patient);
}
```

### 2. Use IEntityDto for Projection

```csharp
// GOOD - use IEntityDto.Project expression for SQL projection
public async Task<PatientDto?> Handle(GetPatientQuery query, CancellationToken ct)
{
    return await _uow.RepositoryFor<Patient>()
        .FirstOrDefaultAsDtoAsync<PatientDto>(p => p.Id == query.Id, ct);
}

// ALSO GOOD - use ToDto when entity already loaded
var patient = await _uow.RepositoryFor<Patient>().GetByIdAsync(id, ct);
if (patient is null) return null;
return PatientDto.ToDto(patient);

// BAD - manual inline mapping (not reusable)
return await _context.Patients
    .Where(p => p.Id == id)
    .Select(p => new PatientDto { Id = p.Id, ... })  // Duplicated everywhere
    .FirstOrDefaultAsync(ct);
```

### 3. Project vs ToDto

```csharp
// Project - Expression for SQL projection (efficient, columns selected at DB level)
public static Expression<Func<Patient, PatientDto>> Project => p => new PatientDto
{
    Id = p.Id,
    FirstName = p.FirstName,
    // Only these columns selected in SQL
};

// ToDto - Method for in-memory mapping when entity already loaded
public static PatientDto ToDto(Patient patient) => new()
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
- [ ] `IEntityDto<TEntity, TDto>` interface created with `Project` and `ToDto`
- [ ] `PatientDto` implements `IEntityDto<Patient, PatientDto>`
- [ ] `PatientDto` has both `Project` expression and `ToDto` method
- [ ] Repository methods added: `GetAllAsListAsync`, `GetAllAsDtosAsync`, `FirstOrDefaultAsDtoAsync`, `FirstOrDefaultWithProjectionAsync`
- [ ] `GetPatientQuery` and `GetPatientQueryHandler` created
- [ ] `GetAllPatientsQuery` and `GetAllPatientsQueryHandler` created with Status filter
- [ ] Query handlers use `IUnitOfWork` with repository methods
- [ ] Controller injects both `IUnitOfWork` and `IMediator`

---

## Folder Structure After This Step

```
BuildingBlocks/
└── BuildingBlocks.Application/
    ├── DtoBase.cs                    ← Base class for DTOs with Id
    ├── IEntityDto.cs                 ← Interface with Project/ToDto
    ├── IRepository.cs                ← Repository interface with query methods
    ├── IUnitOfWork.cs
    └── SuccessOrFailureDto.cs

Core/Scheduling/
└── Scheduling.Application/
    ├── Patients/
    │   ├── Commands/
    │   │   ├── CreatePatientCommand.cs
    │   │   ├── CreatePatientCommandHandler.cs
    │   │   ├── SuspendPatientCommand.cs
    │   │   ├── SuspendPatientCommandHandler.cs
    │   │   ├── ActivatePatientCommand.cs
    │   │   ├── ActivatePatientCommandHandler.cs
    │   │   ├── DeletePatientCommand.cs
    │   │   └── DeletePatientCommandHandler.cs
    │   ├── Dtos/
    │   │   └── PatientDto.cs              ← Implements IEntityDto<Patient, PatientDto>
    │   ├── Queries/
    │   │   ├── GetPatientQuery.cs
    │   │   ├── GetPatientQueryHandler.cs
    │   │   ├── GetAllPatientsQuery.cs     ← With Status filter
    │   │   └── GetAllPatientsQueryHandler.cs
    │   └── EventHandlers/
    │       └── PatientCreatedEventHandler.cs
    └── ServiceCollectionExtensions.cs

WebApi/
└── Controllers/
    └── PatientsController.cs              ← Injects IUnitOfWork and IMediator
```

---

→ Next: [04-validation.md](./04-validation.md) - Validating commands with FluentValidation
