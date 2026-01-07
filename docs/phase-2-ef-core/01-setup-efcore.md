# Phase 2: Setting Up EF Core

## Overview

In Phase 1, we built the domain layer with:
- Patient aggregate with encapsulation
- Domain events
- `IEntityBase` interface (in shared Repositories project)

Now we'll implement the **Infrastructure layer** with:
- EF Core DbContext
- Entity configurations (Fluent API)
- Generic Repository and UnitOfWork (see [02-repository-implementation.md](./02-repository-implementation.md))
- Domain event dispatching

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

        // Ignore domain events - they're not persisted
        builder.Ignore(p => p.DomainEvents);
    }
}
```

**Key points:**
- **Fluent API over attributes** - Keeps domain clean, no EF dependencies in domain
- **`HasConversion<string>()`** - Stores enum as readable string, not int
- **`Ignore(DomainEvents)`** - Domain events are transient, not stored in DB

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
Core/
├── Repositories/                          ← Shared building blocks
│   ├── Entity.cs
│   ├── Repository.cs
│   ├── UnitOfWork.cs
│   ├── Events/
│   │   ├── IDomainEvent.cs
│   │   ├── IHasDomainEvents.cs
│   │   ├── IDomainEventDispatcher.cs
│   │   └── DomainEventDispatcher.cs
│   ├── Interfaces/
│   │   ├── IEntityBase.cs
│   │   ├── IRepository.cs
│   │   └── IUnitOfWork.cs
│   └── Repositories.csproj
└── Scheduling/
    ├── Scheduling.Domain/
    │   └── Patients/
    │       ├── Patient.cs
    │       ├── PatientStatus.cs
    │       └── Events/
    │           ├── PatientCreatedEvent.cs
    │           └── PatientSuspendedEvent.cs
    └── Scheduling.Infrastructure/
        ├── Persistence/
        │   ├── SchedulingDbContext.cs
        │   ├── Configurations/
        │   │   └── PatientConfiguration.cs
        │   └── Migrations/
        │       └── (generated migration files)
        ├── ServiceCollectionExtensions.cs
        └── Scheduling.Infrastructure.csproj
```

### Step 6: Verify it compiles

```bash
cd C:/projects/ddd/agfa
dotnet build AGFA.sln
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
- [ ] Domain events ignored in configuration
- [ ] Domain entities inherit from `Entity` (which implements `IEntityBase`)
- [ ] Solution builds

---

## Next Steps

We haven't created a database yet. Next we'll:
- Implement generic Repository and UnitOfWork
- Register services for DI
- Create database migrations

→ Next: [02-repository-implementation.md](./02-repository-implementation.md) - Generic Repository and UnitOfWork
