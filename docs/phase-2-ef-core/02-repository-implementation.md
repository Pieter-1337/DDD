# Implementing the Repository with EF Core

## Overview

We use a **generic repository pattern** with a **Unit of Work** that provides repositories on demand via `RepositoryFor<T>()`. This eliminates the need for entity-specific repository classes like `PatientRepository`.

**Key components:**
- `IRepository<T>` - Interface in BuildingBlocks.Application
- `Repository<TContext, TEntity>` - Generic implementation in BuildingBlocks.Infrastructure
- `IUnitOfWork` - Interface with `RepositoryFor<T>()` and `SaveChangesAsync()`
- `UnitOfWork<TContext>` - Generic implementation with domain event dispatching
- `IEntityDto<TEntity, TDto>` - Interface for DTO projections

---

## What You Need To Do

### Step 1: Shared BuildingBlocks Project Structure

The generic repository interfaces and implementations are split across three projects:

```
BuildingBlocks/
├── BuildingBlocks.Domain/                 ← Pure domain abstractions
│   ├── Entity.cs
│   ├── Events/
│   │   ├── IDomainEvent.cs
│   │   └── IHasDomainEvents.cs
│   ├── Interfaces/
│   │   └── IEntityBase.cs                 ← Only marker interface
│   └── BuildingBlocks.Domain.csproj
│
BuildingBlocks.Application/                ← Application layer contracts
├── IEntityDto.cs                          ← DTO projection interface
├── IRepository.cs                         ← Repository interface
├── IUnitOfWork.cs                         ← Unit of Work interface
└── BuildingBlocks.Application.csproj
│
BuildingBlocks/
└── BuildingBlocks.Infrastructure/         ← Infrastructure implementations
    ├── Repository.cs
    ├── UnitOfWork.cs
    └── BuildingBlocks.Infrastructure.csproj
```

### Step 2: IEntityBase Interface

Location: `BuildingBlocks/BuildingBlocks.Domain/Interfaces/IEntityBase.cs`

```csharp
namespace BuildingBlocks.Domain.Interfaces;

public interface IEntityBase
{
    Guid Id { get; set; }
}
```

All domain entities inherit from `Entity` which implements this interface.

### Step 3: IEntityDto Interface

Location: `BuildingBlocks.Application/IEntityDto.cs`

```csharp
using System.Linq.Expressions;

namespace BuildingBlocks.Application;

public interface IEntityDto<TEntity, TDto> where TDto : IEntityDto<TEntity, TDto>
{
    static abstract Expression<Func<TEntity, TDto>> Projection { get; }
    static abstract TDto FromEntity(TEntity entity);
}
```

**Key points:**
- `Projection` - Expression tree for EF Core SQL translation (efficient)
- `FromEntity` - For in-memory mapping when entity already loaded

### Step 4: IRepository Interface

Location: `BuildingBlocks.Application/IRepository.cs`

```csharp
using System.Linq.Expressions;
using BuildingBlocks.Domain.Interfaces;

namespace BuildingBlocks.Application;

public interface IRepository<TEntity> where TEntity : class, IEntityBase
{
    IQueryable<TEntity> GetAll(Expression<Func<TEntity, bool>>? filter = null);
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> filter, CancellationToken ct = default);

    Task<TDto?> FirstOrDefaultAsDtoAsync<TDto>(
        Expression<Func<TEntity, bool>> filter,
        CancellationToken ct = default)
        where TDto : class, IEntityDto<TEntity, TDto>;

    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
    void Add(TEntity entity);
    void Remove(TEntity entity);
}
```

**Key design decisions:**
- `GetAll()` is the foundation - all query methods build on it
- `FirstOrDefaultAsync()` is a convenience method that uses `GetAll()` internally
- `FirstOrDefaultAsDtoAsync()` - Projects directly to DTO for efficient queries
- No `Update()` method - EF Core change tracking handles updates automatically

### Step 5: Generic Repository Implementation

Location: `BuildingBlocks/BuildingBlocks.Infrastructure/Repository.cs`

```csharp
using System.Linq.Expressions;
using BuildingBlocks.Application;
using BuildingBlocks.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure;

public class Repository<TContext, TEntity> : IRepository<TEntity>
    where TEntity : class, IEntityBase
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly DbSet<TEntity> _dbSet;

    public Repository(TContext context)
    {
        _context = context;
        _dbSet = _context.Set<TEntity>();
    }

    public IQueryable<TEntity> GetAll(Expression<Func<TEntity, bool>>? filter = null)
    {
        var query = _dbSet.AsQueryable();

        if (filter != null)
            query = query.Where(filter);

        return query;
    }

    public async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> filter, CancellationToken ct = default)
    {
        return await GetAll(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<TDto?> FirstOrDefaultAsDtoAsync<TDto>(
        Expression<Func<TEntity, bool>> filter,
        CancellationToken ct = default)
        where TDto : class, IEntityDto<TEntity, TDto>
    {
        return await GetAll(filter)
            .Select(TDto.Projection)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbSet.FindAsync([id], ct);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbSet.AnyAsync(e => e.Id == id, ct);
    }

    public void Add(TEntity entity)
    {
        _dbSet.Add(entity);
    }

    public void Remove(TEntity entity)
    {
        _dbSet.Remove(entity);
    }
}
```

**Key points:**
- `FirstOrDefaultAsDtoAsync` uses `TDto.Projection` (static abstract member)
- EF Core translates the projection expression to efficient SQL
- Only the columns needed for the DTO are selected

### Step 6: IUnitOfWork Interface

Location: `BuildingBlocks.Application/IUnitOfWork.cs`

```csharp
using BuildingBlocks.Domain.Interfaces;

namespace BuildingBlocks.Application;

public interface IUnitOfWork
{
    IRepository<T> RepositoryFor<T>() where T : class, IEntityBase;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

**Key feature:** `RepositoryFor<T>()` provides a repository for any entity type on demand.

### Step 7: Generic UnitOfWork Implementation

Location: `BuildingBlocks/BuildingBlocks.Infrastructure/UnitOfWork.cs`

```csharp
using BuildingBlocks.Application;
using BuildingBlocks.Domain.Events;
using BuildingBlocks.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure;

public class UnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IMediator _mediator;

    public UnitOfWork(TContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _context.SaveChangesAsync(cancellationToken);
        await DispatchDomainEventsAsync(cancellationToken);
        return result;
    }

    public IRepository<T> RepositoryFor<T>() where T : class, IEntityBase
    {
        return new Repository<TContext, T>(_context);
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        var entitiesWithEvents = _context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = entitiesWithEvents
            .SelectMany(e => e.DomainEvents)
            .ToList();

        foreach (var entity in entitiesWithEvents)
        {
            entity.ClearDomainEvents();
        }

        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }
    }
}
```

**Key points:**
- UnitOfWork injects `IMediator` for domain event dispatching
- Events are auto-dispatched after `SaveChangesAsync()` succeeds
- Events only fire if save succeeds (no orphan events)

### Step 8: Register Services

Location: `Core/Scheduling/Scheduling.Infrastructure/ServiceCollectionExtensions.cs`

```csharp
using BuildingBlocks.Application;
using BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduling.Infrastructure.Persistence;

namespace Scheduling.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<SchedulingDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IUnitOfWork, UnitOfWork<SchedulingDbContext>>();

        return services;
    }
}
```

**Note:** No repository registrations needed - `UnitOfWork.RepositoryFor<T>()` creates them.

---

## Usage Pattern

In your Application layer handlers:

### Command Handler (Create)

```csharp
using BuildingBlocks.Application;
using Scheduling.Domain.Patients;

public class CreatePatientHandler
{
    private readonly IUnitOfWork _unitOfWork;

    public CreatePatientHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
    {
        var patient = Patient.Create(
            cmd.FirstName,
            cmd.LastName,
            cmd.Email,
            cmd.DateOfBirth,
            cmd.PhoneNumber);

        _unitOfWork.RepositoryFor<Patient>().Add(patient);
        await _unitOfWork.SaveChangesAsync(ct);

        return patient.Id;
    }
}
```

### Query Handler (with DTO projection)

```csharp
using BuildingBlocks.Application;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

public class GetPatientQueryHandler
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPatientQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PatientDto?> Handle(GetPatientQuery query, CancellationToken ct)
    {
        return await _unitOfWork.RepositoryFor<Patient>()
            .FirstOrDefaultAsDtoAsync<PatientDto>(p => p.Id == query.Id, ct);
    }
}
```

### Common Operations

**Query with filter:**
```csharp
var activePatients = await _unitOfWork.RepositoryFor<Patient>()
    .GetAll(p => p.Status == PatientStatus.Active)
    .ToListAsync(ct);
```

**Update (via change tracking):**
```csharp
var patient = await _unitOfWork.RepositoryFor<Patient>()
    .FirstOrDefaultAsync(p => p.Id == id, ct);

patient.UpdateContactInfo("new@email.com", null);

await _unitOfWork.SaveChangesAsync(ct);  // Change tracker handles it
```

**Delete:**
```csharp
var patient = await _unitOfWork.RepositoryFor<Patient>()
    .FirstOrDefaultAsync(p => p.Id == id, ct);

_unitOfWork.RepositoryFor<Patient>().Remove(patient);

await _unitOfWork.SaveChangesAsync(ct);
```

---

## Why This Pattern?

| Old Pattern | New Pattern |
|-------------|-------------|
| `IPatientRepository` | `IRepository<Patient>` via `RepositoryFor<T>()` |
| `PatientRepository.cs` | Not needed |
| `IAppointmentRepository` | `IRepository<Appointment>` via `RepositoryFor<T>()` |
| Register each repository | Register only `IUnitOfWork` |

**Benefits:**
- Less boilerplate - no entity-specific repositories
- Single injection - only `IUnitOfWork` needed
- Consistent API - same methods for all entities
- Efficient DTO projections - `IEntityDto` enables SQL-level projections

---

## Verification Checklist

- [ ] `IEntityBase` interface exists in `BuildingBlocks.Domain`
- [ ] `IEntityDto<TEntity, TDto>` interface exists in `BuildingBlocks.Application`
- [ ] `IRepository<T>` interface exists in `BuildingBlocks.Application`
- [ ] `Repository<TContext, TEntity>` implements `IRepository<T>`
- [ ] `IUnitOfWork` interface exists in `BuildingBlocks.Application`
- [ ] `UnitOfWork<TContext>` implements `IUnitOfWork`
- [ ] ServiceCollectionExtensions registers `IUnitOfWork`
- [ ] Domain entities implement `IEntityBase`
- [ ] Entity DTOs implement `IEntityDto`
- [ ] Solution builds

---

→ Next: [03-database-migrations.md](./03-database-migrations.md) - Creating the database
