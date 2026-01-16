# Database Migrations

## Overview

Now we'll create the actual database using EF Core migrations.

---

## What You Need To Do

### Step 1: Add Design package to WebApi project

The migrations tool needs a startup project. Add the design package:

```bash
cd DDD/WebApi
dotnet add package Microsoft.EntityFrameworkCore.Design
```

### Step 2: Add connection string to appsettings.json

Location: `DDD/appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=DDD;Trusted_Connection=True;MultipleActiveResultSets=true"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

**Using LocalDB** - comes with Visual Studio, no separate SQL Server install needed.

### Step 3: Register Infrastructure in Program.cs

Update `DDD/Program.cs`:

```csharp
using Scheduling.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddOpenApi();

// Add Infrastructure (EF Core, Repositories)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddSchedulingInfrastructure(connectionString);

var app = builder.Build();

// ... rest of file
```

### Step 4: Add WebApi reference to Infrastructure

```bash
cd DDD/WebApi
dotnet add reference ../src/Scheduling.Infrastructure/Scheduling.Infrastructure.csproj
```

### Step 5: Create the initial migration

From the solution root:

```bash
cd C:/projects/ddd/DDD
dotnet ef migrations add InitialCreate --project DDD/src/Scheduling.Infrastructure --startup-project DDD/WebApi --output-dir Persistence/Migrations
```

**What this does:**
- `--project` - Where the DbContext lives
- `--startup-project` - Where the configuration (connection string) lives
- `--output-dir` - Where to put migration files

### Step 6: Review the migration

Check `Scheduling.Infrastructure/Persistence/Migrations/` for the generated files.

The `Up()` method should create the Patients table:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateTable(
        name: "Patients",
        columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
            PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
            DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: false),
            Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_Patients", x => x.Id);
        });

    migrationBuilder.CreateIndex(
        name: "IX_Patients_Email",
        table: "Patients",
        column: "Email",
        unique: true);
}
```

### Step 7: Apply the migration

```bash
dotnet ef database update --project DDD/src/Scheduling.Infrastructure --startup-project DDD/WebApi
```

This creates the database and tables.

### Step 8: Verify in SQL Server Object Explorer

In Visual Studio:
1. View → SQL Server Object Explorer
2. Expand (localdb)\MSSQLLocalDB
3. Databases → DDD → Tables
4. You should see `dbo.Patients`

---

## Useful EF Core Commands

```bash
# Create a new migration
dotnet ef migrations add MigrationName --project DDD/src/Scheduling.Infrastructure --startup-project DDD/WebApi --output-dir Persistence/Migrations

# Apply migrations
dotnet ef database update --project DDD/src/Scheduling.Infrastructure --startup-project DDD/WebApi

# Remove last migration (if not applied)
dotnet ef migrations remove --project DDD/src/Scheduling.Infrastructure --startup-project DDD/WebApi

# Generate SQL script (for production deployments)
dotnet ef migrations script --project DDD/src/Scheduling.Infrastructure --startup-project DDD/WebApi
```

---

## Migrations with Multiple Bounded Contexts

When using DDD with multiple bounded contexts, each context has its own DbContext. Here's what you need to know:

### Each DbContext Needs Separate Migrations

EF Core tracks migrations **per DbContext**. If you have:
- `SchedulingDbContext` (Scheduling bounded context)
- `BillingDbContext` (Billing bounded context)
- `MedicalRecordsDbContext` (Medical Records bounded context)

You must create and apply migrations for **each one separately**.

### Using the `--context` Parameter

When you have multiple DbContexts in the same project, use `--context` to specify which one:

```bash
# Create migration for Scheduling context
dotnet ef migrations add InitialCreate \
  --project DDD/src/Scheduling.Infrastructure \
  --startup-project DDD/WebApi \
  --context SchedulingDbContext \
  --output-dir Persistence/Migrations

# Create migration for Billing context
dotnet ef migrations add InitialCreate \
  --project DDD/src/Billing.Infrastructure \
  --startup-project DDD/WebApi \
  --context BillingDbContext \
  --output-dir Persistence/Migrations

# Apply migrations for specific context
dotnet ef database update \
  --project DDD/src/Scheduling.Infrastructure \
  --startup-project DDD/WebApi \
  --context SchedulingDbContext
```

**Note:** If you only have one DbContext in the project, `--context` is optional.

### How EF Core Tracks Migrations

EF Core creates a `__EFMigrationsHistory` table in your database that tracks applied migrations:

| MigrationId | ProductVersion |
|-------------|----------------|
| 20240115_InitialCreate_SchedulingDbContext | 9.0.0 |
| 20240115_InitialCreate_BillingDbContext | 9.0.0 |

Each context's migrations are tracked separately, allowing them to evolve independently.

### One Database, Multiple Contexts

```
┌─────────────────────────────────────────────────────────────┐
│                          DDD                                 │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  SchedulingDbContext        BillingDbContext                │
│  ┌─────────────────┐       ┌─────────────────┐              │
│  │ Patients        │       │ Invoices        │              │
│  │ Appointments    │       │ Payments        │              │
│  │ Doctors         │       │ PaymentMethods  │              │
│  └─────────────────┘       └─────────────────┘              │
│                                                              │
│  MedicalRecordsDbContext                                     │
│  ┌─────────────────┐                                         │
│  │ MedicalRecords  │                                         │
│  │ Diagnoses       │                                         │
│  └─────────────────┘                                         │
│                                                              │
│  __EFMigrationsHistory (shared, tracks all contexts)         │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

Each DbContext:
- Manages only its own tables
- Has its own entity configurations
- Lives in its own Infrastructure project
- Can be migrated independently

### Best Practices for Multi-Context Migrations

1. **Separate Infrastructure projects**: Each bounded context gets its own Infrastructure project with its own DbContext
2. **Consistent naming**: Use `{BoundedContext}DbContext` naming convention
3. **Independent evolution**: Contexts can add migrations at different times
4. **Same connection string**: All contexts can share the same database connection string
5. **No cross-context references**: Never reference entities from another context's tables

---

## Optional: Create a Test Controller

Create a controller to test the infrastructure setup (before implementing CQRS).

Location: `WebApi/Controllers/PatientsController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using Repositories.Interfaces;
using Scheduling.Domain.Patients;

namespace WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;

    public PatientsController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    [HttpPost]
    public async Task<bool> Create(CreatePatientRequest request)
    {
        var patient = Patient.Create(
            request.FirstName,
            request.LastName,
            request.Email,
            request.DateOfBirth,
            request.PhoneNumber);

        _unitOfWork.RepositoryFor<Patient>().Add(patient);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }
}

public record CreatePatientRequest(
    string FirstName,
    string LastName,
    string Email,
    DateTime DateOfBirth,
    string? PhoneNumber);
```

**Note:** This is a minimal test controller. In Phase 3, we'll implement proper CQRS with Commands, Queries, and DTOs.

### Test via Postman

1. Run the WebApi project: `dotnet run --project WebApi`
2. Note the port from the console output (e.g., `https://localhost:7xxx`)
3. In Postman, create a new request:
   - **Method:** POST
   - **URL:** `https://localhost:<port>/api/patients`
   - **Headers:** `Content-Type: application/json`
   - **Body (raw JSON):**
```json
{
  "firstName": "John",
  "lastName": "Doe",
  "email": "john@example.com",
  "dateOfBirth": "1990-01-15",
  "phoneNumber": "555-1234"
}
```
4. Click "Send"

### Test via curl

```bash
curl -X POST https://localhost:5001/api/patients \
  -H "Content-Type: application/json" \
  -d '{"firstName":"John","lastName":"Doe","email":"john@example.com","dateOfBirth":"1990-01-15"}'
```

---

## Verification Checklist

- [ ] Connection string in appsettings.json
- [ ] Infrastructure registered in Program.cs
- [ ] Migration created successfully
- [ ] Database created with Patients table
- [ ] Email column has unique index
- [ ] (Optional) Test endpoint works

---

→ Next: [04-domain-event-dispatching.md](./04-domain-event-dispatching.md) - Publishing events on save
