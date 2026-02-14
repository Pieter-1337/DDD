# .NET Aspire Introduction

## What Is .NET Aspire?

.NET Aspire is an opinionated, cloud-ready stack for building distributed applications. It provides:

1. **Orchestration** - Start, configure, and coordinate multiple services with a single command
2. **Service Discovery** - Services find each other automatically, no hardcoded URLs
3. **Observability** - Built-in dashboard for logs, traces, and metrics
4. **Integrations** - Pre-built components for common services (Redis, RabbitMQ, PostgreSQL, SQL Server, etc.)

```
Traditional Distributed App:
+----------+     +----------+     +----------+
|  WebApi  |---->| RabbitMQ |<----| Worker   |
+----------+     +----------+     +----------+
      |                                 |
      v                                 v
+----------+                      +----------+
| SQL Svr  |                      | Redis    |
+----------+                      +----------+

Problem: How do you start all of this? Configure connection strings? View logs?

With .NET Aspire:
+------------------------------------------------------------------+
|                        AppHost (Orchestrator)                     |
|                                                                   |
|  +----------+     +----------+     +----------+                   |
|  |  WebApi  |---->| RabbitMQ |<----| Worker   |                   |
|  +----------+     +----------+     +----------+                   |
|        |                                 |                        |
|        v                                 v                        |
|  +----------+                      +----------+                   |
|  | SQL Svr  |                      | Redis    |                   |
|  +----------+                      +----------+                   |
|                                                                   |
+------------------------------------------------------------------+
        |
        v
+------------------------------------------------------------------+
|                    Aspire Dashboard                               |
|  - All logs in one place                                          |
|  - Distributed traces across services                             |
|  - Resource health and metrics                                    |
+------------------------------------------------------------------+
```

---

## Why Use Aspire for Distributed Applications?

### The Problem: Distributed Development is Hard

Building distributed applications introduces significant complexity:

| Challenge | Without Aspire | With Aspire |
|-----------|---------------|-------------|
| **Starting services** | Run docker-compose, then start each project manually | `F5` starts everything |
| **Connection strings** | Hardcode or manage in appsettings per environment | Injected automatically |
| **Service discovery** | Manual configuration, environment variables | Built-in, just reference by name |
| **Viewing logs** | Open multiple terminal windows | Single dashboard, all services |
| **Distributed tracing** | Set up Jaeger/Zipkin, configure OpenTelemetry | Built-in dashboard |
| **Health checks** | Implement and wire up manually | Automatic for Aspire components |

### Developer Experience Improvement

Before Aspire:
```bash
# Terminal 1
docker-compose up -d

# Terminal 2
cd WebApi && dotnet run

# Terminal 3
cd WorkerService && dotnet run

# Terminal 4
docker logs -f rabbitmq

# Browser: http://localhost:15672 for RabbitMQ UI
# Browser: http://localhost:5000/swagger for API
# Browser: http://localhost:16686 for Jaeger traces (if configured)
```

With Aspire:
```bash
# Just press F5 in Visual Studio, or:
dotnet run --project Aspire.AppHost

# Browser: Opens Aspire Dashboard automatically
# - All logs
# - All traces
# - All metrics
# - Service endpoints
```

---

## Key Components

### 1. AppHost Project

The **AppHost** is the orchestrator - it defines what services run and how they connect:

```csharp
// Aspire.AppHost/AppHost.cs
var builder = DistributedApplication.CreateBuilder(args);

// Add infrastructure
var rabbitMq = builder.AddRabbitMQ("messaging");
var sqlServer = builder.AddSqlServer("sql")
    .AddDatabase("scheduling");

// Add application services
var webApi = builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(rabbitMq)
    .WithReference(sqlServer);

var worker = builder.AddProject<Projects.WorkerService>("worker")
    .WithReference(rabbitMq);

builder.Build().Run();
```

**Key points:**
- One central place defines the entire system topology
- `WithReference()` creates service discovery and injects connection strings
- Infrastructure (RabbitMQ, SQL Server) can be containers or existing services

### 2. ServiceDefaults Project

The **ServiceDefaults** project contains shared configuration that all services use:

```csharp
// ServiceDefaults/Extensions.cs
public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
{
    // OpenTelemetry for traces, metrics, logs
    builder.ConfigureOpenTelemetry();

    // Default health checks
    builder.AddDefaultHealthChecks();

    // Service discovery
    builder.Services.AddServiceDiscovery();

    // Resilience (Polly) for HTTP clients
    builder.Services.ConfigureHttpClientDefaults(http =>
    {
        http.AddStandardResilienceHandler();
        http.AddServiceDiscovery();
    });

    return builder;
}
```

**Each service project references ServiceDefaults:**
```csharp
// WebApi/Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();  // Add observability, health checks, service discovery
```

### 3. Integrations (Components)

Aspire provides pre-built integrations for common services. Each integration:
- Configures the client library
- Sets up health checks
- Adds OpenTelemetry instrumentation
- Handles connection string injection

| Integration | Package | What It Configures |
|-------------|---------|-------------------|
| SQL Server | `Aspire.Microsoft.EntityFrameworkCore.SqlServer` | EF Core DbContext |
| RabbitMQ | `Aspire.RabbitMQ.Client` | RabbitMQ connection |
| Redis | `Aspire.StackExchange.Redis` | Redis connection |
| PostgreSQL | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` | EF Core with Npgsql |

**Example - SQL Server integration:**

```csharp
// In AppHost
var sqlServer = builder.AddSqlServer("sql")
    .AddDatabase("scheduling");

var webApi = builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(sqlServer);
```

```csharp
// In WebApi/Program.cs
builder.AddSqlServerDbContext<SchedulingDbContext>("scheduling");
// Connection string injected automatically!
```

---

## How Aspire Fits Our Architecture

Our current architecture uses:
- **Scheduling bounded context** with CQRS (MediatR)
- **MassTransit** for messaging
- **RabbitMQ** as message broker
- **SQL Server** for persistence
- **Docker Compose** for local infrastructure

### Current Project Structure

```
DDD/
+-- Core/Scheduling/
|   +-- Scheduling.Domain/
|   +-- Scheduling.Application/     # MediatR commands/queries
|   +-- Scheduling.Infrastructure/  # EF Core, MassTransit consumers
|
+-- BuildingBlocks/
|   +-- BuildingBlocks.Application/
|   +-- BuildingBlocks.Infrastructure.MassTransit/
|   +-- BuildingBlocks.Infrastructure.EfCore/
|
+-- WebApi/                         # ASP.NET Core API
+-- docker-compose.yml              # RabbitMQ, SQL Server
```

### With Aspire Added

```
DDD/
+-- Core/Scheduling/
|   +-- Scheduling.Domain/
|   +-- Scheduling.Application/
|   +-- Scheduling.Infrastructure/
|
+-- BuildingBlocks/
|   +-- BuildingBlocks.Application/
|   +-- BuildingBlocks.Infrastructure.MassTransit/
|   +-- BuildingBlocks.Infrastructure.EfCore/
|
+-- WebApi/                         # References ServiceDefaults
|
+-- Aspire.AppHost/                 # NEW: Aspire orchestrator
|   +-- AppHost.cs                  # Defines entire system topology
|
+-- ServiceDefaults/                # NEW: Shared Aspire configuration
|   +-- Extensions.cs               # OpenTelemetry, health checks, etc.
|
+-- docker-compose.yml              # Can keep for CI/CD, or let Aspire manage
```

### Integration Points

| Component | Before Aspire | With Aspire |
|-----------|--------------|-------------|
| **RabbitMQ** | docker-compose, manual connection string | Aspire container or reference existing |
| **SQL Server** | docker-compose, appsettings.json | Aspire container or reference existing |
| **MassTransit** | Configure in Program.cs | Same, but connection string injected |
| **EF Core** | Configure in Program.cs | Use Aspire integration for DbContext |
| **Health Checks** | Manual setup | Automatic from ServiceDefaults |
| **Logging** | Serilog to console/file | Same, but visible in dashboard |

---

## Before vs After: Orchestration Comparison

### Before Aspire: Manual Docker Compose

**docker-compose.yml:**
```yaml
services:
  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"

  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourStrong@Password
    ports:
      - "1433:1433"
```

**appsettings.json:**
```json
{
  "ConnectionStrings": {
    "SchedulingDb": "Server=localhost;Database=Scheduling;User=sa;Password=YourStrong@Password;TrustServerCertificate=true"
  },
  "RabbitMQ": {
    "Host": "localhost",
    "Username": "guest",
    "Password": "guest"
  }
}
```

**Workflow:**
```
1. docker-compose up -d
2. Wait for containers to be healthy
3. Run WebApi project
4. Check multiple places for logs:
   - Visual Studio Output window (WebApi)
   - docker logs rabbitmq
   - SQL Server logs
```

### After Aspire: Unified Orchestration

**Aspire.AppHost/AppHost.cs:**
```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure as code
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin();  // Enables management UI

var sql = builder.AddSqlServer("sql")
    .AddDatabase("scheduling");

// Application services
builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(messaging)
    .WithReference(sql);

builder.Build().Run();
```

**Workflow:**
```
1. F5 (or dotnet run --project Aspire.AppHost)
2. Aspire Dashboard opens automatically
3. All logs, traces, metrics in one place
4. Service endpoints listed in dashboard
```

### Comparison Table

| Aspect | Docker Compose | .NET Aspire |
|--------|---------------|-------------|
| **Starting** | `docker-compose up` then run projects | Single F5 or `dotnet run` |
| **Connection strings** | Manual in appsettings.json | Injected automatically |
| **Logs** | Multiple terminals/tools | Single dashboard |
| **Distributed tracing** | Manual setup (Jaeger, etc.) | Built-in |
| **Health checks** | Manual implementation | Automatic |
| **Service discovery** | Hardcoded URLs | Automatic by name |
| **Container management** | Separate from app code | Defined in AppHost |
| **Learning curve** | Low (familiar tools) | Medium (new concepts) |
| **Production deployment** | Docker Compose or K8s | Azure Container Apps, K8s, or custom |

---

## Benefits Summary

### 1. Orchestration

Start your entire distributed system with one command:

```csharp
// AppHost defines the complete system
var builder = DistributedApplication.CreateBuilder(args);

var rabbitMq = builder.AddRabbitMQ("messaging");
var sqlServer = builder.AddSqlServer("sql").AddDatabase("scheduling");

builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(rabbitMq)
    .WithReference(sqlServer);

builder.Build().Run();
```

### 2. Service Discovery

No more hardcoded URLs or connection strings:

```csharp
// Before: Hardcoded
services.AddDbContext<SchedulingDbContext>(options =>
    options.UseSqlServer("Server=localhost;Database=Scheduling;..."));

// After: Service discovery
builder.AddSqlServerDbContext<SchedulingDbContext>("scheduling");
// Connection string comes from AppHost automatically
```

### 3. Observability Dashboard

One place to see everything:

```
+------------------------------------------------------------------+
|                    Aspire Dashboard                               |
+------------------------------------------------------------------+
| Resources                                                         |
|   webapi        Running     https://localhost:5001                |
|   messaging     Running     amqp://localhost:5672                 |
|   sql           Running     localhost,1433                        |
+------------------------------------------------------------------+
| Traces                                                            |
|   POST /api/patients  ->  SQL INSERT  ->  RabbitMQ Publish        |
|   [32ms total]        [8ms]          [12ms]                       |
+------------------------------------------------------------------+
| Logs                                                              |
|   [webapi]    info: Handling CreatePatientCommand                 |
|   [webapi]    info: Patient created: abc-123                      |
|   [messaging] info: Message published to patient-created          |
+------------------------------------------------------------------+
```

### 4. Health Checks

Automatic health monitoring for all services:

```csharp
// ServiceDefaults adds health checks automatically
builder.AddDefaultHealthChecks();

// Each Aspire integration adds its own health checks
// - SQL Server: checks database connectivity
// - RabbitMQ: checks broker connectivity
// - Redis: checks cache connectivity
```

### 5. Resilience

Built-in resilience patterns for HTTP clients:

```csharp
// ServiceDefaults configures resilient HTTP clients
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddStandardResilienceHandler();  // Retries, circuit breaker, timeout
    http.AddServiceDiscovery();           // Resolve service names to URLs
});
```

---

## When to Use Aspire

### Good Fit

- Multiple services that need to communicate
- Local development with containers
- Need unified logging and tracing
- Want to reduce configuration complexity
- Azure deployment target (Azure Container Apps)

### May Not Need

- Simple single-project applications
- Already have mature DevOps tooling
- Non-.NET services in your stack
- Strict control over container orchestration needed

---

## What You Will Build in This Phase

1. **Aspire.AppHost project** - Orchestrate WebApi, RabbitMQ, SQL Server
2. **ServiceDefaults project** - Shared observability and resilience
3. **Aspire integrations** - SQL Server and RabbitMQ components
4. **Dashboard exploration** - Logs, traces, metrics
5. **Service discovery** - Connect services by name

### Phase 6 Documentation

1. **01-aspire-introduction.md** - This file
2. **02-aspire-setup.md** - Creating AppHost and ServiceDefaults projects
3. **03-aspire-integrations.md** - Adding SQL Server and RabbitMQ integrations
4. **04-observability-dashboard.md** - Using the Aspire dashboard
5. **05-deployment-considerations.md** - Production deployment options

---

## Key Concepts Summary

| Concept | Description |
|---------|-------------|
| **AppHost** | Orchestrator project that defines system topology |
| **ServiceDefaults** | Shared configuration (telemetry, health checks, resilience) |
| **Integration** | Pre-built component that configures a service (SQL, Redis, etc.) |
| **WithReference()** | Creates dependency and injects connection info |
| **Service Discovery** | Services find each other by name, not URL |
| **Dashboard** | Unified view of logs, traces, and metrics |

---

## Prerequisites

Before starting Phase 6:

- **.NET 9 SDK** - Aspire 9 is distributed as NuGet packages and project templates
- **Docker Desktop** - For running container resources (RabbitMQ, SQL Server, Redis, etc.)
- **Visual Studio 2022 17.9+** or **VS Code with C# Dev Kit**
- **Aspire Project Templates** - Install via `dotnet new install Aspire.ProjectTemplates`
- **Completed Phase 5** - Event-Driven Architecture with MassTransit/RabbitMQ

> **Note**: Starting with Aspire 9, the .NET Aspire workload (`dotnet workload install aspire`) is no longer required. Aspire is now distributed entirely through NuGet packages and `dotnet new` templates, simplifying the installation process.

---

> Next: [02-aspire-setup.md](./02-aspire-setup.md) - Creating the AppHost and ServiceDefaults projects
