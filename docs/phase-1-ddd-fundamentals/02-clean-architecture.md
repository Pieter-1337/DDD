# Clean Architecture - Project Structure

## Why This Matters

In traditional projects, domain classes often depend on infrastructure:

```csharp
// Domain class depends on EF Core - BAD
public class Patient
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Email { get; set; }  // Database concerns leaked into domain!
}
```

**Problems:**
- Domain is tied to Entity Framework
- Can't test domain logic without a database
- Changing database = changing domain classes
- Domain expresses "how to store" instead of "what the business does"

## The Goal

Make the **domain layer depend on NOTHING**:

```
        ┌─────────────────────────┐
        │      Presentation       │  ← Blazor, API, CLI
        └───────────┬─────────────┘
                    │
        ┌───────────▼─────────────┐
        │      Application        │  ← Use cases, orchestration
        └───────────┬─────────────┘
                    │
        ┌───────────▼─────────────┐
        │        Domain           │  ← Entities, Value Objects
        │   (depends on NOTHING)  │  ← THE CORE
        └─────────────────────────┘
                    ▲
        ┌───────────┴─────────────┐
        │     Infrastructure      │  ← EF Core, RabbitMQ
        └─────────────────────────┘
```

---

## What You Need To Do

### Step 1: Create the folder structure

Create the following structure:
```
BuildingBlocks/                       ← Shared building blocks (split by concern)
├── BuildingBlocks.Domain/            ← Entity, interfaces
├── BuildingBlocks.Infrastructure/    ← Repository, UnitOfWork implementations
└── BuildingBlocks.WebApplications/   ← Web infrastructure (OpenAPI, JSON converters)
Core/
└── Scheduling/                       ← Bounded context
    ├── Scheduling.Domain/
    ├── Scheduling.Application/
    ├── Scheduling.Infrastructure/
    └── Scheduling.Domain.Tests/
WebApi/                               ← API project
```

### Step 2: Create BuildingBlocks.Domain project (pure domain abstractions)

Location: `BuildingBlocks/BuildingBlocks.Domain/BuildingBlocks.Domain.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MediatR" />
  </ItemGroup>

</Project>
```

This project contains pure domain building blocks: `Entity`, `IEntityBase`.

**Note:** This is a minimal project with only the base `Entity` class and `IEntityBase` interface. No MediatR dependency needed since we use integration events only (published via MassTransit, not MediatR).

### Step 3: Create BuildingBlocks.Infrastructure project

Location: `BuildingBlocks/BuildingBlocks.Infrastructure/BuildingBlocks.Infrastructure.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MediatR" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\BuildingBlocks.Domain\BuildingBlocks.Domain.csproj" />
  </ItemGroup>

</Project>
```

This project contains infrastructure implementations: `EfCoreRepository<TContext, TEntity>`, `EfCoreUnitOfWork<TContext>`.

### Step 3a: Create BuildingBlocks.WebApplications project

Location: `BuildingBlocks/BuildingBlocks.WebApplications/BuildingBlocks.WebApplications.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <PackageReference Include="Scalar.AspNetCore" />
  </ItemGroup>

</Project>
```

This project contains shared web application infrastructure including OpenAPI configuration and JSON converters.

**OpenApi/OpenApiExtensions.cs:**

```csharp
using Microsoft.AspNetCore.Builder;
using Scalar.AspNetCore;

namespace BuildingBlocks.WebApplications.OpenApi;

public static class OpenApiExtensions
{
    public static IApplicationBuilder UseOpenApiWithScalar(this WebApplication app, string title)
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.WithTitle(title);
            options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
        });

        return app;
    }
}
```

**Why Scalar over Swagger?**
- Modern, actively maintained UI built specifically for OpenAPI 3.x
- Better developer experience with cleaner, more intuitive interface
- Superior code generation examples (multiple languages, frameworks)
- Lightweight and performant
- Each bounded context passes its own title for clear API documentation

### Step 4: Create Scheduling.Domain.csproj

Location: `Core/Scheduling/Scheduling.Domain/Scheduling.Domain.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net9.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <RootNamespace>Scheduling.Domain</RootNamespace>
    </PropertyGroup>

    <ItemGroup>
      <ProjectReference Include="..\..\..\BuildingBlocks\BuildingBlocks.Domain\BuildingBlocks.Domain.csproj" />
    </ItemGroup>

    <!--
    IMPORTANT: Only reference BuildingBlocks.Domain for base classes.
    No infrastructure dependencies - business logic in pure C#.
    -->

</Project>
```

**Why reference BuildingBlocks.Domain?** Domain entities inherit from `Entity` base class which implements `IEntityBase`.

### Step 5: Create Scheduling.Application.csproj

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
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\Scheduling.Domain\Scheduling.Domain.csproj" />
    </ItemGroup>

</Project>
```

**Why MediatR?** Application layer uses MediatR for commands, queries, and event handlers.

### Step 6: Create Scheduling.Infrastructure.csproj

Location: `Core/Scheduling/Scheduling.Infrastructure/Scheduling.Infrastructure.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net9.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <RootNamespace>Scheduling.Infrastructure</RootNamespace>
    </PropertyGroup>

    <ItemGroup>
        <ProjectReference Include="..\Scheduling.Domain\Scheduling.Domain.csproj" />
        <ProjectReference Include="..\Scheduling.Application\Scheduling.Application.csproj" />
        <ProjectReference Include="..\..\..\BuildingBlocks\BuildingBlocks.Infrastructure\BuildingBlocks.Infrastructure.csproj" />
    </ItemGroup>

</Project>
```

**Why reference BuildingBlocks.Infrastructure?** Infrastructure uses `UnitOfWork<TContext>` and `Repository<TContext, TEntity>` from the shared project.

### Step 7: Create Scheduling.Domain.Tests.csproj

Location: `Core/Scheduling/Scheduling.Domain.Tests/Scheduling.Domain.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net9.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <IsPackable>false</IsPackable>
        <RootNamespace>Scheduling.Domain.Tests</RootNamespace>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.NET.Test.Sdk" />
        <PackageReference Include="xunit" />
        <PackageReference Include="xunit.runner.visualstudio">
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
            <PrivateAssets>all</PrivateAssets>
        </PackageReference>
        <PackageReference Include="FluentAssertions" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\Scheduling.Domain\Scheduling.Domain.csproj" />
    </ItemGroup>

</Project>
```

**Note:** No version numbers - uses Central Package Management (see Step 8).

### Step 8: Add projects to solution

```bash
dotnet sln DDD.sln add BuildingBlocks/BuildingBlocks.Domain/BuildingBlocks.Domain.csproj
dotnet sln DDD.sln add BuildingBlocks/BuildingBlocks.Infrastructure/BuildingBlocks.Infrastructure.csproj
dotnet sln DDD.sln add BuildingBlocks/BuildingBlocks.WebApplications/BuildingBlocks.WebApplications.csproj
dotnet sln DDD.sln add Core/Scheduling/Scheduling.Domain/Scheduling.Domain.csproj
dotnet sln DDD.sln add Core/Scheduling/Scheduling.Application/Scheduling.Application.csproj
dotnet sln DDD.sln add Core/Scheduling/Scheduling.Infrastructure/Scheduling.Infrastructure.csproj
dotnet sln DDD.sln add Core/Scheduling/Scheduling.Domain.Tests/Scheduling.Domain.Tests.csproj
dotnet sln DDD.sln add WebApi/WebApi.csproj
```

### Step 9: Setup Central Package Management

Create `Directory.Packages.props` at solution root to manage package versions centrally:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <!-- EF Core -->
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="9.0.11" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.11" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.11" />

    <!-- MediatR -->
    <PackageVersion Include="MediatR" Version="12.4.1" />

    <!-- ASP.NET Core -->
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="9.0.11" />
    <PackageVersion Include="Scalar.AspNetCore" Version="2.0.36" />

    <!-- Testing -->
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.0.1" />
    <PackageVersion Include="FluentAssertions" Version="8.0.1" />
  </ItemGroup>
</Project>
```

This allows `<PackageReference Include="MediatR" />` without version in individual projects.

### Step 10: Create domain folder structure

Inside `Core/Scheduling/Scheduling.Domain/`, create:
```
Patients/
├── Patient.cs
├── PatientStatus.cs
└── Events/
    ├── PatientCreatedEvent.cs
    └── PatientSuspendedEvent.cs
```

**Why organize by aggregate?** All Patient-related code lives together. If Patient ever becomes its own microservice, it's easy to extract.

### Step 11: Configure WebApi Project

Location: `WebApi/Program.cs`

```csharp
using BuildingBlocks.WebApplications.OpenApi;
using Scheduling.Application;
using Scheduling.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();

// Add OpenAPI document generation
builder.Services.AddOpenApi();

// Get connection string
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Add Infrastructure (DbContext, UnitOfWork)
builder.Services.AddSchedulingInfrastructure(connectionString);

// Add Application (MediatR handlers, validators)
builder.Services.AddSchedulingApplication();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Use Scalar for OpenAPI documentation UI
    app.UseOpenApiWithScalar("Scheduling API");
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

**Key configuration:**
- `AddSchedulingInfrastructure` - Registers DbContext and UnitOfWork
- `AddSchedulingApplication` - Registers MediatR handlers
- `UseOpenApiWithScalar("Scheduling API")` - Configures OpenAPI document and Scalar UI
  - Each bounded context passes its own title (e.g., "Scheduling API", "Billing API")
  - Scalar UI available at `/scalar/v1` in development
  - Raw OpenAPI JSON document remains at `/openapi/v1.json`

### Step 12: Build and verify

```bash
dotnet build DDD.sln
```

Should build with 0 errors.

---

## Verification Checklist

After completing the steps, verify:

- [ ] `BuildingBlocks.Domain.csproj` exists with MediatR package only
- [ ] `BuildingBlocks.Infrastructure.csproj` exists with EF Core packages
- [ ] `BuildingBlocks.WebApplications.csproj` exists with Scalar.AspNetCore
- [ ] `Scheduling.Domain.csproj` only references `BuildingBlocks.Domain`
- [ ] `Scheduling.Application.csproj` references `Scheduling.Domain` and has MediatR
- [ ] `Scheduling.Infrastructure.csproj` references Domain, Application, and `BuildingBlocks.Infrastructure`
- [ ] `Directory.Packages.props` exists at solution root
- [ ] Solution builds successfully
- [ ] Scalar UI accessible at `/scalar/v1` when running in development

---

## What You Learned

1. **Dependency Inversion** - Domain defines interfaces, Infrastructure implements them
2. **Layer Separation** - Each layer has a single responsibility
3. **Testability** - Domain can be tested without database or web framework

→ Next: [03-building-patient-aggregate.md](./03-building-patient-aggregate.md) - Building the Patient Aggregate
