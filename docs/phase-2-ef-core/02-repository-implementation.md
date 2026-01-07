# Implementing the Repository with EF Core

## Overview

We use a **generic repository pattern** with a **Unit of Work** that provides repositories on demand via `RepositoryFor<T>()`. This eliminates the need for entity-specific repository classes like `PatientRepository`.

**Key components:**
- `IRepository<T>` - Interface in BuildingBlocks.Domain
- `Repository<TContext, TEntity>` - Generic implementation in BuildingBlocks.Infrastructure
- `IUnitOfWork` - Interface with `RepositoryFor<T>()` and `SaveChangesAsync()`
- `UnitOfWork<TContext>` - Generic implementation with domain event dispatching

---

## What You Need To Do

### Step 1: Shared BuildingBlocks Project Structure

The generic repository interfaces and implementations are split across two projects:

```
BuildingBlocks/
├── BuildingBlocks.Domain/                 ← Pure domain abstractions
│   ├── Entity.cs
│   ├── Events/
│   │   ├── IDomainEvent.cs
│   │   └── IHasDomainEvents.cs
│   ├── Interfaces/
│   │   ├── IEntityBase.cs
│   │   ├── IRepository.cs
│   │   └── IUnitOfWork.cs
│   └── BuildingBlocks.Domain.csproj
└── BuildingBlocks.Infrastructure/         ← Infrastructure implementations
    ├── Repository.cs
    ├── UnitOfWork.cs
    ├── Events/
    │   ├── IDomainEventDispatcher.cs
    │   └── DomainEventDispatcher.cs
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

### Step 3: IRepository Interface

Location: `BuildingBlocks/BuildingBlocks.Domain/Interfaces/IRepository.cs`

```csharp
using System.Linq.Expressions;

namespace BuildingBlocks.Domain.Interfaces;

public interface IRepository<TEntity>
{
    IQueryable<TEntity> GetAll(Expression<Func<TEntity, bool>>? filter = null);
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> filter, CancellationToken ct = default);
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
    void Add(TEntity entity);
    void Remove(TEntity entity);
}
```

**Key design decisions:**
- `GetAll()` is the foundation - all query methods build on it
- `FirstOrDefaultAsync()` is a convenience method that uses `GetAll()` internally
- No `Update()` method - EF Core change tracking handles updates automatically

### Step 4: Generic Repository Implementation

Location: `BuildingBlocks/BuildingBlocks.Infrastructure/Repository.cs`

```csharp
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Domain.Interfaces;

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

**Why `GetAll()` as the foundation?**
- All query utilities (FirstOrDefault, pagination, etc.) can build on it
- Easy to enrich later with features like `IgnoreQueryFilters`, `AsSplitQuery`, etc.
- Keeps the internal implementation consistent

### Step 5: IUnitOfWork Interface

Location: `BuildingBlocks/BuildingBlocks.Domain/Interfaces/IUnitOfWork.cs`

```csharp
namespace BuildingBlocks.Domain.Interfaces;

public interface IUnitOfWork
{
    IRepository<T> RepositoryFor<T>() where T : class, IEntityBase;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

**Note:** No `new()` constraint - entities can have private constructors (required for EF Core).

**Key feature:** `RepositoryFor<T>()` provides a repository for any entity type on demand.

### Step 6: Generic UnitOfWork Implementation

Location: `BuildingBlocks/BuildingBlocks.Infrastructure/UnitOfWork.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Domain.Interfaces;
using BuildingBlocks.Infrastructure.Events;

namespace BuildingBlocks.Infrastructure;

public class UnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public UnitOfWork(TContext context, IDomainEventDispatcher domainEventDispatcher)
    {
        _context = context;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Save first (so events only fire on success)
        var result = await _context.SaveChangesAsync(cancellationToken);

        // Then dispatch events
        await _domainEventDispatcher.DispatchEventsAsync(_context, cancellationToken);

        return result;
    }

    public IRepository<T> RepositoryFor<T>() where T : class, IEntityBase
    {
       return new Repository<TContext, T>(_context);
    }
}
```

**Key points:**
- UnitOfWork handles domain event dispatching after save
- Events only fire if save succeeds (no orphan events)
- See [04-domain-event-dispatching.md](./04-domain-event-dispatching.md) for event details

### Step 7: Register Services

Location: `Core/Scheduling/Scheduling.Infrastructure/ServiceCollectionExtensions.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Domain.Interfaces;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Events;
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

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IUnitOfWork, UnitOfWork<SchedulingDbContext>>();

        return services;
    }
}
```

**Note:** No repository registrations needed - `UnitOfWork.RepositoryFor<T>()` creates them.

### Step 8: Folder Structure

```
BuildingBlocks/
├── BuildingBlocks.Domain/                 ← Pure domain abstractions
│   ├── Entity.cs
│   ├── Events/
│   │   ├── IDomainEvent.cs
│   │   └── IHasDomainEvents.cs
│   ├── Interfaces/
│   │   ├── IEntityBase.cs
│   │   ├── IRepository.cs
│   │   └── IUnitOfWork.cs
│   └── BuildingBlocks.Domain.csproj
└── BuildingBlocks.Infrastructure/         ← Infrastructure implementations
    ├── Repository.cs
    ├── UnitOfWork.cs
    ├── Events/
    │   ├── IDomainEventDispatcher.cs
    │   └── DomainEventDispatcher.cs
    └── BuildingBlocks.Infrastructure.csproj
Core/
└── Scheduling/
    └── Scheduling.Infrastructure/
        ├── Persistence/
        │   ├── SchedulingDbContext.cs
        │   └── Configurations/
        │       └── PatientConfiguration.cs
        ├── ServiceCollectionExtensions.cs
        └── Scheduling.Infrastructure.csproj
```

**Note:** No `PatientRepository.cs` needed!

---

## Usage Pattern

In your Application layer handlers:

```csharp
public class CreatePatientHandler
{
    private readonly IUnitOfWork _unitOfWork;

    public CreatePatientHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
    {
        // Check if email already exists
        var existing = await _unitOfWork.RepositoryFor<Patient>()
            .FirstOrDefaultAsync(p => p.Email == cmd.Email.ToLowerInvariant(), ct);

        if (existing is not null)
            throw new EmailAlreadyExistsException(cmd.Email);

        // Create patient (domain validates)
        var patient = Patient.Create(
            cmd.FirstName,
            cmd.LastName,
            cmd.Email,
            cmd.DateOfBirth,
            cmd.PhoneNumber);

        // Add to repository
        _unitOfWork.RepositoryFor<Patient>().Add(patient);

        // Save changes
        await _unitOfWork.SaveChangesAsync(ct);

        return patient.Id;
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

**Query with chaining:**
```csharp
var patients = await _unitOfWork.RepositoryFor<Patient>()
    .GetAll(p => p.Status == PatientStatus.Active)
    .OrderBy(p => p.LastName)
    .Take(10)
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
- Extensible - add utilities to `GetAll()` once, all entities benefit

---

## Verification Checklist

- [ ] `IEntityBase` interface exists with `Id` property
- [ ] `IRepository<T>` interface with `GetAll`, `FirstOrDefaultAsync`, etc.
- [ ] `Repository<TContext, TEntity>` implements `IRepository<T>`
- [ ] `IUnitOfWork` interface with `RepositoryFor<T>()` and `SaveChangesAsync()`
- [ ] `UnitOfWork<TContext>` implements `IUnitOfWork`
- [ ] `DependencyInjection.cs` registers `IUnitOfWork`
- [ ] Domain entities implement `IEntityBase`
- [ ] Solution builds

---

→ Next: [03-database-migrations.md](./03-database-migrations.md) - Creating the database
