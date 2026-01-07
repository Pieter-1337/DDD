# Repository Pattern

## What Is a Repository?

A repository is an **abstraction over data access**. It hides how entities are stored and retrieved.

```csharp
// Shared building blocks - defines generic repository interface
public interface IRepository<TEntity>
{
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Add(TEntity entity);
}

// Unit of Work provides repositories
public interface IUnitOfWork
{
    IRepository<T> RepositoryFor<T>() where T : class, IEntityBase;
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

---

## Why Repositories?

**Without repository:**
```csharp
// Application layer directly uses EF Core
public class CreatePatientHandler
{
    private readonly AppDbContext _context;  // Infrastructure dependency!

    public async Task Handle(CreatePatientCommand cmd)
    {
        var patient = Patient.Create(...);
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();
    }
}
```

**Problems:**
- Application layer depends on EF Core
- Hard to test without database
- Can't easily switch data access technology

**With repository:**
```csharp
// Application layer uses abstraction
public class CreatePatientHandler
{
    private readonly IUnitOfWork _unitOfWork;  // Interface only

    public async Task Handle(CreatePatientCommand cmd)
    {
        var patient = Patient.Create(...);
        _unitOfWork.RepositoryFor<Patient>().Add(patient);
        await _unitOfWork.SaveChangesAsync();
    }
}
```

**Benefits:**
- Application layer only knows the interface
- Easy to test with mock/fake implementation
- Implementation can change without affecting domain/application

---

## What You Need To Do

The repository infrastructure lives in the shared `BuildingBlocks` projects and uses a **generic repository pattern** with **Unit of Work**.

### Step 1: Generic repository interface (already exists in BuildingBlocks.Domain)

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

**Note:** This is a generic interface that works with any entity type. No entity-specific interfaces needed.

### Step 2: Unit of Work interface (already exists in BuildingBlocks.Domain)

Location: `BuildingBlocks/BuildingBlocks.Domain/Interfaces/IUnitOfWork.cs`

```csharp
namespace BuildingBlocks.Domain.Interfaces;

public interface IUnitOfWork
{
    IRepository<T> RepositoryFor<T>() where T : class, IEntityBase;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

**Key points:**
- `RepositoryFor<T>()` - Get a repository for any entity type dynamically
- `SaveChangesAsync()` - Commits all changes and dispatches domain events

### Step 3: Understand the design decisions

**Why generic repository instead of entity-specific?**

Using `IUnitOfWork.RepositoryFor<Patient>()` instead of `IPatientRepository` means:
- No need to create/register a repository interface per entity
- Consistent API across all entities
- Less boilerplate code
- Easy to add new entities without DI changes

**Why no `Update()` method?**

EF Core tracks changes automatically. When you modify an entity that's tracked, EF knows to update it on `SaveChanges()`. No explicit `Update()` call needed.

**Why `void Add()` instead of `Task AddAsync()`?**

EF Core's `DbSet.Add()` is synchronous - it just marks the entity for insertion. The actual database operation happens during `SaveChangesAsync()`.

**Why `CancellationToken`?**

Allows cancelling long-running database operations. Good practice for async methods.

### Step 4: Using repositories in handlers

Example usage in an Application layer handler:

```csharp
using BuildingBlocks.Domain.Interfaces;
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

### Step 5: Querying with filters

The repository supports flexible queries via `GetAll()` and `FirstOrDefaultAsync()`:

```csharp
// Get by email
var patient = await _unitOfWork
    .RepositoryFor<Patient>()
    .FirstOrDefaultAsync(p => p.Email == email, ct);

// Get all active patients
var activePatients = _unitOfWork
    .RepositoryFor<Patient>()
    .GetAll(p => p.Status == PatientStatus.Active);
```

---

## Key Principles

### One Repository Per Aggregate Root

```csharp
// CORRECT - Repository for aggregate roots
_unitOfWork.RepositoryFor<Patient>()
_unitOfWork.RepositoryFor<Appointment>()
_unitOfWork.RepositoryFor<Doctor>()

// WRONG - Repository for child entity
_unitOfWork.RepositoryFor<MedicalRecord>()  // Part of Patient aggregate
```

Child entities are accessed through their aggregate root.

### Repository Returns Domain Objects

Repositories work with domain entities. Mapping to DTOs happens in the Application layer (handlers, queries).

---

## Verification Checklist

- [ ] `IRepository<TEntity>` interface exists in `BuildingBlocks/BuildingBlocks.Domain/Interfaces/`
- [ ] `IUnitOfWork` interface exists with `RepositoryFor<T>()` method
- [ ] `IEntityBase` interface exists for entity identity
- [ ] Application layer can use `IUnitOfWork` to access repositories

---

## Folder Structure After This Step

```
BuildingBlocks/
├── BuildingBlocks.Domain/                 ← Pure domain abstractions
│   ├── Entity.cs
│   ├── Events/
│   │   ├── IDomainEvent.cs
│   │   └── IHasDomainEvents.cs
│   └── Interfaces/
│       ├── IEntityBase.cs
│       ├── IRepository.cs
│       └── IUnitOfWork.cs
└── BuildingBlocks.Infrastructure/         ← Infrastructure implementations
    ├── Repository.cs
    ├── UnitOfWork.cs
    └── Events/
        ├── IDomainEventDispatcher.cs
        └── DomainEventDispatcher.cs
Core/
└── Scheduling/
    ├── Scheduling.Domain/
    │   └── Patients/
    │       ├── Patient.cs
    │       ├── PatientStatus.cs
    │       └── Events/
    │           ├── PatientCreatedEvent.cs
    │           └── PatientSuspendedEvent.cs
    ├── Scheduling.Application/
    │   └── ServiceCollectionExtensions.cs
    └── Scheduling.Infrastructure/
        └── ServiceCollectionExtensions.cs
```

---

## What You Learned

1. **Generic repository** - One interface works for all entities
2. **Unit of Work pattern** - Coordinates repositories and saves changes
3. **One repository per aggregate root** - Access child entities through the root
4. **EF Core change tracking** - No explicit `Update()` needed

---

## Phase 1 Complete!

You've now built:
- Clean Architecture project structure
- Patient aggregate with encapsulation
- Domain events
- Generic repository and Unit of Work interfaces

**Next: Phase 2 - Persistence with EF Core**

We'll implement:
- Repository implementation using EF Core
- DbContext configuration
- Mapping entities to tables
- Domain event dispatching on save
