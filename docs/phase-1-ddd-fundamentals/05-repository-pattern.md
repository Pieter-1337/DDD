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

### Step 1: IEntityBase interface (in BuildingBlocks.Domain)

Location: `BuildingBlocks/BuildingBlocks.Domain/Interfaces/IEntityBase.cs`

```csharp
namespace BuildingBlocks.Domain.Interfaces;

public interface IEntityBase
{
    Guid Id { get; set; }
}
```

**Note:** This stays in Domain as it's a marker interface for entities.

### Step 2: IEntityDto interface (in BuildingBlocks.Application)

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
- Uses C# 11 static abstract interface members
- `Projection` - Expression for EF Core to translate to SQL (efficient)
- `FromEntity` - For in-memory mapping when entity is already loaded

### Step 3: Generic repository interface (in BuildingBlocks.Application)

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
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> filter, CancellationToken ct = default);
    void Add(TEntity entity);
    void Remove(TEntity entity);
}
```

**Note:** This is a generic interface that works with any entity type. No entity-specific interfaces needed.

### Step 4: Unit of Work interface (in BuildingBlocks.Application)

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

**Key points:**
- `RepositoryFor<T>()` - Get a repository for any entity type dynamically
- `SaveChangesAsync()` - Commits all changes and publishes queued events

### Step 5: Why interfaces are in Application layer?

**Architecture:**
```
Domain:       IEntityBase, Entity, Events (pure business logic)
                  ↑
Application:  IRepository, IUnitOfWork, IEntityDto (use case contracts)
                  ↑
Infrastructure: Repository, UnitOfWork (implementations)
```

**Reasons:**
- `IRepository` needs `IEntityDto` for DTO projections
- `IEntityDto` is an Application layer concern (DTOs live there)
- Domain can't reference Application (would be circular)
- This is a valid Clean Architecture interpretation

### Step 6: Using repositories in handlers

Example usage in an Application layer handler:

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

### Step 7: Querying with DTO projection

The repository supports efficient DTO projection via `FirstOrDefaultAsDtoAsync`:

```csharp
// Efficient - only selects columns needed for DTO
var patientDto = await _unitOfWork
    .RepositoryFor<Patient>()
    .FirstOrDefaultAsDtoAsync<PatientDto>(p => p.Id == id, ct);
```

This uses the `Projection` expression from the DTO, which EF Core translates to SQL.

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

### Repository Returns Domain Objects (or DTOs)

Repositories work with domain entities or project to DTOs. The `IEntityDto` constraint ensures type-safe projections.

---

## Verification Checklist

- [ ] `IEntityBase` interface exists in `BuildingBlocks/BuildingBlocks.Domain/Interfaces/`
- [ ] `IEntityDto<TEntity, TDto>` interface exists in `BuildingBlocks.Application/`
- [ ] `IRepository<TEntity>` interface exists in `BuildingBlocks.Application/`
- [ ] `IUnitOfWork` interface exists in `BuildingBlocks.Application/`
- [ ] Application layer can use `IUnitOfWork` to access repositories

---

## Folder Structure After This Step

```
BuildingBlocks/
+-- BuildingBlocks.Domain/                 <- Pure domain abstractions
|   +-- Entity.cs
|   +-- Interfaces/
|       +-- IEntityBase.cs                 <- Only marker interface
|
+-- BuildingBlocks.Application/            <- Application layer contracts
|   +-- Interfaces/
|   |   +-- IRepository.cs                 <- Repository interface
|   |   +-- IUnitOfWork.cs                 <- Unit of Work interface
|   +-- Messaging/
|   |   +-- IEventBus.cs                   <- Event publishing abstraction
|   |   +-- IIntegrationEvent.cs           <- Event marker interface
|   |   +-- IntegrationEventBase.cs        <- Base class for events
|   +-- IEntityDto.cs                      <- DTO projection interface
|
+-- BuildingBlocks.Infrastructure.EfCore/  <- EF Core implementations
    +-- EfCoreRepository.cs
    +-- EfCoreUnitOfWork.cs
```

**Note:** Integration events are published via MassTransit/RabbitMQ. See Phase 5 documentation for details.

---

## What You Learned

1. **Generic repository** - One interface works for all entities
2. **Unit of Work pattern** - Coordinates repositories and saves changes
3. **DTO projections** - `IEntityDto` enables efficient SQL queries
4. **Clean Architecture** - Interfaces in Application, implementations in Infrastructure
5. **One repository per aggregate root** - Access child entities through the root

---

## Phase 1 Complete!

You've now built:
- Clean Architecture project structure
- Patient aggregate with encapsulation
- Generic repository and Unit of Work interfaces

**Next: Phase 2 - Persistence with EF Core**

We'll implement:
- Repository implementation using EF Core
- DbContext configuration
- Mapping entities to tables
- Event publishing on save
