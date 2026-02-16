# SQL Server with .NET Aspire

This document covers migrating SQL Server from docker-compose to .NET Aspire, simplifying configuration and gaining automatic connection string injection, health checks, and dashboard integration.

**Key Insight:** Entity Framework Core is already your SQL Server client. You don't need `Aspire.Microsoft.EntityFrameworkCore.SqlServer` - it would create a redundant second connection. Instead, configure EF Core to read the connection string that Aspire injects.

---

## 1. Overview: Why Aspire for SQL Server?

### Current Setup (docker-compose + Manual Config)

With docker-compose, you must:
1. Manually start `docker-compose up -d` before running the app
2. Configure connection strings in `appsettings.json`
3. Set up health checks manually
4. Manage database volumes separately

### With Aspire

Aspire eliminates this manual setup:
1. Container starts automatically with F5
2. Connection strings injected automatically
3. Health checks included out of the box
4. Data persistence managed automatically
5. Database initialization visible in dashboard

| Aspect | docker-compose | Aspire |
|--------|---------------|--------|
| **Container startup** | Manual (`docker-compose up -d`) | Automatic with F5 |
| **Connection strings** | Manual in `appsettings.json` | Injected automatically |
| **Health checks** | Manual setup | Built-in |
| **Data persistence** | Manual volume configuration | `.WithDataVolume()` |
| **Service discovery** | Not available | Built-in |
| **Observability** | Manual setup | OpenTelemetry included |

---

## 2. Architecture: Aspire SQL Server Integration

### How It Works

```
AppHost (Orchestrator)
    |
    +-- AddSqlServer("sql")
    |       |
    |       +-- Starts SQL Server container automatically
    |       +-- Generates server connection string
    |       +-- Exposes resource reference
    |       |
    |       +-- AddDatabase("scheduling-db")
    |               |
    |               +-- Creates database automatically
    |               +-- Generates database connection string
    |
    +-- AddProject<WebApi>()
            .WithReference(schedulingDb)
            .WaitFor(schedulingDb)
                |
                +-- Injects ConnectionStrings__scheduling-db
                +-- Waits for SQL Server health check
```

### Package Dependencies

| Project | Package | Purpose |
|---------|---------|---------|
| **AppHost** | `Aspire.Hosting.SqlServer` | Orchestrates SQL Server container |

**Note:** You do NOT need `Aspire.Microsoft.EntityFrameworkCore.SqlServer` in WebApi. Entity Framework Core is your SQL Server client and manages its own connections. Adding the Aspire client package would create two separate connections to SQL Server, which is wasteful and confusing.

---

## 3. Implementation Steps

### Step 1: Add NuGet Package

Add the hosting package to the AppHost.

```bash
cd Aspire.AppHost
dotnet add package Aspire.Hosting.SqlServer
```

Verify `Directory.Packages.props` contains:
```xml
<PackageVersion Include="Aspire.Hosting.SqlServer" Version="13.1.1" />
```

**Note:** This project uses Central Package Management (`ManagePackageVersionsCentrally=true`), which does not allow floating versions such as `9.*`. Always pin Aspire package versions to match the Aspire SDK version declared in `global.json` or `Directory.Build.props`. The current Aspire SDK version is `13.1.1`.

**Important:** Do NOT add `Aspire.Microsoft.EntityFrameworkCore.SqlServer` to WebApi. Entity Framework Core handles all SQL Server client functionality.

### Step 2: Add SQL Server to AppHost

Update `Aspire.AppHost/AppHost.cs`:

**Before (without SQL Server):**
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var webApi = builder.AddProject<Projects.WebApi>("webapi");

builder.Build().Run();
```

**After (with SQL Server):**
```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Add SQL Server with persistent data volume
var sqlServer = builder.AddSqlServer("sql")
    .WithDataVolume()
    .AddDatabase("scheduling-db");

var webApi = builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(sqlServer)
    .WaitFor(sqlServer);

builder.Build().Run();
```

**What each method does:**

| Method | Purpose |
|--------|---------|
| `AddSqlServer("sql")` | Creates SQL Server resource with logical name "sql" |
| `WithDataVolume()` | Persists database data across container restarts |
| `AddDatabase("scheduling-db")` | Creates database and generates connection string |
| `WithReference(sqlServer)` | Injects connection string into the project |
| `WaitFor(sqlServer)` | Delays startup until SQL Server is healthy |

### Step 3: Update WebApi to Use Aspire's Connection String

Aspire injects the connection string as `ConnectionStrings__scheduling-db` when you use `.WithReference(sqlServer)`. Update the WebApi to read from this.

Update `WebApi/Program.cs`:

**Before:**
```csharp
var builder = WebApplication.CreateBuilder(args);

// Add infrastructure
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddSchedulingInfrastructure(connectionString);
```

**After:**
```csharp
var builder = WebApplication.CreateBuilder(args);

// Try Aspire connection string first, fall back to DefaultConnection
var connectionString = builder.Configuration.GetConnectionString("scheduling-db")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'scheduling-db' or 'DefaultConnection' not found.");

builder.Services.AddSchedulingInfrastructure(connectionString);
```

**Why this approach?**
- Entity Framework Core is already your SQL Server client - no need for a separate Aspire client
- Fallback to `DefaultConnection` works for non-Aspire environments (CI/CD, production)
- Single connection to SQL Server, managed by EF Core
- EF Core provides its own health checks (covered in the next section)

### Step 4: Verify EF Core DbContext Registration

No changes needed in `Scheduling.Infrastructure/ServiceCollectionExtensions.cs`. The existing registration already uses the connection string:

```csharp
public static IServiceCollection AddSchedulingInfrastructure(
    this IServiceCollection services,
    string connectionString)
{
    services.AddDbContext<SchedulingDbContext>(options =>
        options.UseSqlServer(connectionString));

    services.AddScoped<IUnitOfWork, EfCoreUnitOfWork<SchedulingDbContext>>();

    return services;
}
```

This works because:
- The connection string is passed from `Program.cs`
- Aspire injects `ConnectionStrings__scheduling-db` automatically
- EF Core reads this via `configuration.GetConnectionString("scheduling-db")`

---

## 4. Connection String Injection

### How Aspire Injects Connection Strings

When you call `.WithReference(sqlServer)`, Aspire sets environment variables:

```
ConnectionStrings__scheduling-db=Server=localhost,54321;User ID=sa;Password=P@ssw0rd;Database=scheduling-db;TrustServerCertificate=True
```

The `IConfiguration` system reads these automatically:
```csharp
configuration.GetConnectionString("scheduling-db")
// Returns: "Server=localhost,54321;User ID=sa;Password=P@ssw0rd;Database=scheduling-db;TrustServerCertificate=True"
```

### Connection String Format

Aspire provides a standard SQL Server connection string:
```
Server={host},{port};User ID=sa;Password={generated-password};Database={database-name};TrustServerCertificate=True
```

Example:
```
Server=localhost,54321;User ID=sa;Password=P@ssw0rd1234!;Database=scheduling-db;TrustServerCertificate=True
```

Entity Framework Core's `UseSqlServer()` accepts this format directly.

### Security: Generated Passwords

Aspire generates a secure password for the SQL Server SA account automatically. You don't need to manage credentials manually - they're injected at runtime and visible in the Aspire Dashboard.

---

## 5. Data Volume Persistence

### WithDataVolume() Method

The `.WithDataVolume()` method ensures database data persists across container restarts:

```csharp
var sqlServer = builder.AddSqlServer("sql")
    .WithDataVolume()  // Persists data in a Docker volume
    .AddDatabase("scheduling-db");
```

**What it does:**
- Creates a named Docker volume (e.g., `ddd-sql-data`)
- Mounts volume to `/var/opt/mssql` in the container
- Database files persist even after `docker-compose down`

**Without `.WithDataVolume()`:**
- Data is lost when the container stops
- Suitable for testing scenarios only

### Alternative: Bind Mounts

For absolute control over data location, use `.WithDataBindMount()`:

```csharp
var sqlServer = builder.AddSqlServer("sql")
    .WithDataBindMount(@"C:\SqlServerData")  // Bind mount to specific path
    .AddDatabase("scheduling-db");
```

**When to use:**
- Need to access database files directly from host
- Want to back up files using host OS tools
- Debugging database issues

**Recommendation:** Use `.WithDataVolume()` for development - it's simpler and Docker-managed.

---

## 6. Health Checks

### Entity Framework Core Provides Built-In Health Checks

When you add EF Core and Aspire ServiceDefaults, health checks are automatically registered. No additional packages or configuration needed.

The ServiceDefaults project (referenced in WebApi) includes:
```csharp
builder.AddServiceDefaults();  // Includes health checks
```

And `Program.cs` maps the health endpoints:
```csharp
app.MapDefaultEndpoints();  // Maps /health and /alive endpoints
```

### Health Check Response

The health check endpoint will report EF Core status:
```json
{
  "status": "Healthy",
  "results": {
    "SchedulingDbContext": {
      "status": "Healthy",
      "description": "Database connection is healthy"
    }
  }
}
```

### How It Works

1. Aspire's `.WaitFor(sqlServer)` ensures WebApi waits for SQL Server health check
2. EF Core registers a health check that tests database connectivity
3. Health endpoint at `/health` exposes this status
4. Aspire Dashboard displays health status in real-time

### Aspire Dashboard Integration

The Aspire Dashboard automatically discovers and displays health check results. You'll see:
- Green status when SQL Server connection is healthy
- Red status when connection fails
- Health check details in the resource view

---

## 7. What Happens to docker-compose.yml?

### Development: Aspire Replaces docker-compose

For local development, Aspire handles container orchestration. You no longer need:
```bash
docker-compose up -d  # Not needed - Aspire does this
```

### Keep docker-compose.yml for:

| Scenario | Use docker-compose | Use Aspire |
|----------|-------------------|------------|
| Local development | No | Yes |
| CI/CD pipelines | Yes (or Testcontainers) | Maybe |
| Production | No (use Azure SQL) | No |
| Team members without Aspire | Yes (fallback) | No |

### Recommended Approach

Keep `docker-compose.yml` as a fallback but update it with a comment:

```yaml
# docker-compose.yml
# NOTE: For local development, prefer running via Aspire (F5 on AppHost).
# This file is kept for CI/CD and environments without Aspire.

services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: ddd-sqlserver
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourStrong@Password123
      - MSSQL_PID=Developer
    ports:
      - "0.0.0.0:1433:1433"
    volumes:
      - sqlserver_data:/var/opt/mssql
    healthcheck:
      test: ["CMD", "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", "YourStrong@Password123", "-C", "-Q", "SELECT 1"]
      interval: 10s
      timeout: 5s
      retries: 5
    restart: unless-stopped

volumes:
  sqlserver_data:
```

### Disable MSBuild Docker Target

If you added an `EnsureDockerServices` target for Phase 5, update it to skip when running via Aspire:

```xml
<!-- Directory.Build.targets -->
<Target Name="EnsureDockerServices"
        BeforeTargets="Build"
        Condition="'$(ASPIRE_LAUNCHER)' == ''">
  <!-- Only runs when NOT launched by Aspire -->
  <Exec Command="powershell -ExecutionPolicy Bypass -File &quot;$(MSBuildThisFileDirectory)scripts\ensure-docker.ps1&quot;"
        IgnoreExitCode="false" />
</Target>
```

---

## 8. Before/After Comparison

### Configuration (appsettings.json)

**Before (manual):**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=MSI;Initial Catalog=DDD;Integrated Security=true;MultipleActiveResultSets=true;TrustServerCertificate=True"
  }
}
```

**After (Aspire-managed):**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=MSI;Initial Catalog=DDD;Integrated Security=true;MultipleActiveResultSets=true;TrustServerCertificate=True"
  }
  // Aspire injects ConnectionStrings__scheduling-db automatically
  // Keep DefaultConnection as fallback for non-Aspire environments
}
```

### Program.cs (WebApi)

**Before:**
```csharp
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddSchedulingInfrastructure(connectionString);
```

**After:**
```csharp
var builder = WebApplication.CreateBuilder(args);

// Try Aspire connection string first, fall back to DefaultConnection
var connectionString = builder.Configuration.GetConnectionString("scheduling-db")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string not found.");

builder.Services.AddSchedulingInfrastructure(connectionString);
```

**What changed?**
- WebApi reads `scheduling-db` connection string first (injected by Aspire)
- Falls back to `DefaultConnection` for non-Aspire environments
- Health checks are provided by EF Core (no separate client needed)

### AppHost (AppHost.cs)

**Before (no SQL Server):**
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var webApi = builder.AddProject<Projects.WebApi>("webapi");

builder.Build().Run();
```

**After (with SQL Server):**
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("sql")
    .WithDataVolume()
    .AddDatabase("scheduling-db");

var webApi = builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(sqlServer)
    .WaitFor(sqlServer);

builder.Build().Run();
```

### Developer Workflow

**Before:**
```bash
# Terminal 1: Start infrastructure
docker-compose up -d

# Wait for SQL Server to be healthy
docker-compose ps  # Check status

# Terminal 2: Run migrations
dotnet ef database update --project WebApi

# Terminal 3: Run application
dotnet run --project WebApi
```

**After:**
```bash
# Just press F5 in Visual Studio or:
dotnet run --project Aspire.AppHost

# Everything starts automatically
# Database is created automatically
# Dashboard shows all services
# Health checks verify readiness
```

---

## 9. Database Migrations with Aspire

### Running Migrations

Entity Framework Core migrations work the same with Aspire. The connection string is injected automatically.

**Option 1: Run migrations via dotnet ef CLI (when WebApi is running via Aspire)**

When WebApi is running via Aspire, the connection string is available:
```bash
dotnet ef database update --project WebApi
```

**Option 2: Apply migrations on startup (recommended for development)**

Update `Program.cs` to apply migrations automatically:

```csharp
var app = builder.Build();

// Apply migrations on startup (development only)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
    dbContext.Database.Migrate();
}

app.MapDefaultEndpoints();
app.Run();
```

**Option 3: Use Aspire's WithReference to expose connection string**

If you need to run migrations outside of WebApi, you can access the connection string via Aspire:

```bash
# Get the connection string from Aspire Dashboard
# Or add a migration initialization service to the AppHost
```

### Creating Migrations

Create migrations as usual:
```bash
dotnet ef migrations add InitialCreate --project Core/Scheduling/Scheduling.Infrastructure --startup-project WebApi
```

The migration commands work because:
- WebApi project has `appsettings.json` with `DefaultConnection` as fallback
- When running via Aspire, `scheduling-db` connection string is injected
- EF Core tools read from either source

---

## 10. Troubleshooting

### Common Issues

**Issue: Connection refused**
```
Microsoft.Data.SqlClient.SqlException: A connection was successfully established with the server, but then an error occurred during the login process.
```

**Solution:** Ensure `.WaitFor(sqlServer)` is set in AppHost. This ensures the WebApi waits for SQL Server health check.

---

**Issue: Connection string not found**
```
System.InvalidOperationException: Connection string 'scheduling-db' or 'DefaultConnection' not found
```

**Solution:** Ensure `.WithReference(sqlServer)` is set. Run via AppHost, not WebApi directly.

---

**Issue: Database not created**

Aspire's `.AddDatabase("scheduling-db")` creates the database automatically, but if it fails:

1. Check SQL Server logs in Aspire Dashboard
2. Verify SQL Server container is healthy
3. Ensure database name is valid (no special characters)

---

**Issue: Migrations not applied**

If the database exists but tables are missing:

```bash
# Apply migrations manually
dotnet ef database update --project WebApi
```

Or add automatic migration on startup (see section 9).

---

### Viewing Logs

SQL Server logs are visible in:
1. Aspire Dashboard - Click on "sql" resource, then "Logs"
2. Docker Desktop - View container logs
3. Console output when running AppHost

---

## 11. Reference

### Aspire SQL Server Methods

| Method | Purpose |
|--------|---------|
| `AddSqlServer(name)` | Add SQL Server resource to AppHost |
| `WithDataVolume()` | Persist data across restarts |
| `WithDataBindMount(path)` | Bind mount for data persistence |
| `AddDatabase(name)` | Create database and generate connection string |
| `WithEnvironment(key, value)` | Set environment variables |
| `WithReference(sqlServer)` | Inject connection string into consuming project |
| `WaitFor(sqlServer)` | Delay startup until SQL Server is healthy |

### Useful Commands

```bash
# Run AppHost (starts all services)
dotnet run --project Aspire.AppHost

# View Aspire Dashboard
# URL shown in console output, e.g., https://localhost:17178

# Check container status
docker ps | grep sqlserver

# View SQL Server logs
docker logs {container-id}

# Run migrations
dotnet ef database update --project WebApi

# Create migration
dotnet ef migrations add MigrationName --project Core/Scheduling/Scheduling.Infrastructure --startup-project WebApi
```

---

## Verification Checklist

- [ ] `Aspire.Hosting.SqlServer` added to AppHost (version `13.1.1`)
- [ ] SQL Server added to AppHost with `AddSqlServer("sql")`
- [ ] Database created with `.AddDatabase("scheduling-db")`
- [ ] Data persistence enabled with `.WithDataVolume()`
- [ ] WebApi references SQL Server with `.WithReference(sqlServer)`
- [ ] WebApi waits for SQL Server with `.WaitFor(sqlServer)`
- [ ] WebApi reads Aspire connection string via `configuration.GetConnectionString("scheduling-db")`
- [ ] Fallback to `DefaultConnection` configured for non-Aspire environments
- [ ] Health checks accessible at `/health` endpoint (provided by EF Core)
- [ ] Application starts successfully via F5 on AppHost
- [ ] Database is created automatically
- [ ] EF Core migrations apply successfully
- [ ] Verify only ONE connection to SQL Server (from EF Core, not a separate Aspire client)

---

## Summary

Migrating SQL Server to Aspire provides:

1. **Simplified configuration** - No manual connection strings in appsettings.json
2. **Automatic container management** - No more `docker-compose up -d`
3. **Built-in health checks** - EF Core provides health checks out of the box
4. **Integrated dashboard** - View all services, logs, and endpoints in one place
5. **Service discovery** - Connection strings injected automatically via `.WithReference()`
6. **Consistent developer experience** - Same workflow for all infrastructure
7. **Automatic database creation** - `.AddDatabase()` creates the database on startup

The key changes are:
- **AppHost**: `AddSqlServer("sql").WithDataVolume().AddDatabase("scheduling-db")` and `.WithReference(sqlServer)`
- **WebApi**: Read from `configuration.GetConnectionString("scheduling-db")` with fallback to `DefaultConnection`
- **No Aspire SQL Server client needed** - Entity Framework Core is already your SQL Server client

**Why no `Aspire.Microsoft.EntityFrameworkCore.SqlServer`?**

Entity Framework Core is a full-featured ORM that manages its own SQL Server connections. Adding `Aspire.Microsoft.EntityFrameworkCore.SqlServer` would create a second, separate connection to SQL Server that goes unused. The Aspire hosting package (`Aspire.Hosting.SqlServer`) handles container orchestration and connection string injection - that's all you need. EF Core reads the injected connection string and manages the actual SQL Server client connection.

Keep `docker-compose.yml` as a fallback for CI/CD and team members who may not have Aspire set up yet.

---

Next: [04-rabbitmq-with-aspire.md](./04-rabbitmq-with-aspire.md) - Migrating RabbitMQ to Aspire
