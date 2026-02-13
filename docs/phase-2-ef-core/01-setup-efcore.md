# Phase 2: Setting Up EF Core

## Overview

In Phase 1, we built the domain layer with:
- Patient aggregate with encapsulation
- `IEntityBase` interface (in shared Repositories project)

Now we'll implement the **Infrastructure layer** with:
- EF Core DbContext
- Entity configurations (Fluent API)
- Generic Repository and UnitOfWork (see [02-repository-implementation.md](./02-repository-implementation.md))
- Event publishing

---

## What You Need To Do

### Step 1: EF Core packages (already in Repositories)

The EF Core packages are added via Central Package Management in the `Core/Repositories` project. The `Scheduling.Infrastructure` project references `Repositories` and inherits access to EF Core.

`Directory.Packages.props` (at solution root):
```xml
<PackageVersion Include="Microsoft.EntityFrameworkCore" Version="9.0.11" />
<PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.11" />
```

### Step 2: Create the DbContext

Location: `Core/Scheduling/Scheduling.Infrastructure/Persistence/SchedulingDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace Scheduling.Infrastructure.Persistence;

public class SchedulingDbContext : DbContext
{
    public SchedulingDbContext(DbContextOptions<SchedulingDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply all configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SchedulingDbContext).Assembly);
    }
}
```

**Note:** No `DbSet<Patient>` properties needed! The generic repository uses `context.Set<T>()` which works for any entity that has a configuration in `OnModelCreating`.

**Why this minimal DbContext?**
- `ApplyConfigurationsFromAssembly` finds all `IEntityTypeConfiguration<T>` classes
- Generic repository uses `Set<T>()` to access any entity
- Each bounded context has its own DbContext with its own configurations

### Step 3: Create Patient entity configuration

Location: `Core/Scheduling/Scheduling.Infrastructure/Persistence/Configurations/PatientConfiguration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduling.Domain.Patients;

namespace Scheduling.Infrastructure.Persistence.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Email)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(p => p.Email)
            .IsUnique();

        builder.Property(p => p.PhoneNumber)
            .HasMaxLength(20);

        builder.Property(p => p.DateOfBirth)
            .IsRequired();

        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<string>()  // Store enum as string
            .HasMaxLength(20);

    }
}
```

**Key points:**
- **Fluent API over attributes** - Keeps domain clean, no EF dependencies in domain
- **`HasConversion<string>()`** - Stores enum as readable string, not int

### Step 4: Handle private setters and constructors

EF Core needs to set properties when loading from DB. With private setters, EF Core 7+ handles this automatically. Just make sure you have:

1. **Private parameterless constructor** for EF Core:
```csharp
public class Patient : Entity
{
    private Patient() { }  // EF Core uses this
    // ...
}
```

2. **Inherit from `Entity`** (which implements `IEntityBase`):
```csharp
using Repositories;

public class Patient : Entity
{
    // Id comes from Entity base class
    // Entity implements IEntityBase (Guid Id { get; set; })
}
```

### Step 5: Folder structure

Your project structure should look like:

```
BuildingBlocks/
+-- BuildingBlocks.Domain/                 <- Pure domain abstractions
|   +-- Entity.cs
|   +-- Interfaces/
|   |   +-- IEntityBase.cs
|   +-- BuildingBlocks.Domain.csproj
|
+-- BuildingBlocks.Application/            <- Application layer contracts
|   +-- Interfaces/
|   |   +-- IRepository.cs
|   |   +-- IUnitOfWork.cs
|   +-- Messaging/
|   |   +-- IEventBus.cs
|   |   +-- IIntegrationEvent.cs
|   |   +-- IntegrationEventBase.cs
|   +-- IEntityDto.cs
|   +-- BuildingBlocks.Application.csproj
|
+-- BuildingBlocks.Infrastructure.EfCore/  <- EF Core implementations
    +-- EfCoreRepository.cs
    +-- EfCoreUnitOfWork.cs
    +-- BuildingBlocks.Infrastructure.EfCore.csproj

Shared/
+-- IntegrationEvents/                     <- Integration events (cross-BC contracts)
    +-- Scheduling/
        +-- PatientCreatedIntegrationEvent.cs
        +-- PatientSuspendedIntegrationEvent.cs

Core/Scheduling/
+-- Scheduling.Domain/
|   +-- Patients/
|       +-- Patient.cs                     <- Pure entity (no event collection)
|       +-- PatientStatus.cs
+-- Scheduling.Infrastructure/
    +-- Persistence/
    |   +-- SchedulingDbContext.cs
    |   +-- Configurations/
    |   |   +-- PatientConfiguration.cs
    |   +-- Migrations/
    |       +-- (generated migration files)
    +-- Consumers/                         <- MassTransit consumers
    |   +-- PatientCreatedIntegrationEventHandler.cs
    +-- ServiceCollectionExtensions.cs
    +-- Scheduling.Infrastructure.csproj
```

**Note:** Integration events are defined in `Shared/IntegrationEvents/` and published via MassTransit/RabbitMQ.

### Step 6: Verify it compiles

```bash
cd C:/projects/ddd
dotnet build DDD.sln
```

---

## Understanding the Configuration

### Why Fluent API?

```csharp
// ❌ Data Annotations - pollutes domain with EF concerns
public class Patient
{
    [Key]
    public Guid Id { get; private set; }

    [Required]
    [MaxLength(100)]
    public string FirstName { get; private set; }
}

// ✅ Fluent API - keeps domain clean
builder.Property(p => p.FirstName)
    .IsRequired()
    .HasMaxLength(100);
```

### Why store enum as string?

```sql
-- With HasConversion<int>() (default)
SELECT * FROM Patients WHERE Status = 2  -- What does 2 mean?

-- With HasConversion<string>()
SELECT * FROM Patients WHERE Status = 'Suspended'  -- Clear!
```

Strings are readable in the database. Worth the minor storage overhead.

---

## Verification Checklist

- [ ] `SchedulingDbContext` created (minimal, no DbSet properties)
- [ ] `PatientConfiguration` created with Fluent API
- [ ] Domain entities inherit from `Entity` (which implements `IEntityBase`)
- [ ] Solution builds

---

## Next Steps

We haven't created a database yet. Next we'll:
- Implement generic Repository and UnitOfWork
- Register services for DI
- Create database migrations

→ Next: [02-repository-implementation.md](./02-repository-implementation.md) - Generic Repository and UnitOfWork
