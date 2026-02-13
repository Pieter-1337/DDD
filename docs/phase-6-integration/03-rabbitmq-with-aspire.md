# RabbitMQ with .NET Aspire

This document covers migrating RabbitMQ from docker-compose to .NET Aspire, simplifying configuration and gaining automatic health checks, connection string injection, and dashboard integration.

---

## 1. Overview: Why Aspire for RabbitMQ?

### Current Setup (docker-compose + Manual Config)

With docker-compose, you must:
1. Manually start `docker-compose up -d` before running the app
2. Configure connection strings in `appsettings.json`
3. Set up health checks manually
4. Access RabbitMQ Management UI at a separate URL

### With Aspire

Aspire eliminates this manual setup:
1. Container starts automatically with F5
2. Connection strings injected automatically
3. Health checks included out of the box
4. Management UI accessible through Aspire Dashboard

| Aspect | docker-compose | Aspire |
|--------|---------------|--------|
| **Container startup** | Manual (`docker-compose up -d`) | Automatic with F5 |
| **Connection strings** | Manual in `appsettings.json` | Injected automatically |
| **Health checks** | Manual setup | Built-in |
| **Management UI** | Separate URL (localhost:15672) | Integrated in Dashboard |
| **Service discovery** | Not available | Built-in |
| **Observability** | Manual setup | OpenTelemetry included |

---

## 2. Architecture: Aspire RabbitMQ Integration

### How It Works

```
AppHost (Orchestrator)
    |
    +-- AddRabbitMQ("messaging")
    |       |
    |       +-- Starts RabbitMQ container automatically
    |       +-- Generates connection string
    |       +-- Exposes resource reference
    |
    +-- AddProject<WebApi>()
            .WithReference(messaging)
            .WaitFor(messaging)
                |
                +-- Injects ConnectionStrings__messaging
                +-- Waits for RabbitMQ health check
```

### Package Dependencies

| Project | Package | Purpose |
|---------|---------|---------|
| **AppHost** | `Aspire.Hosting.RabbitMQ` | Orchestrates RabbitMQ container |
| **WebApi** | `Aspire.RabbitMQ.Client` | RabbitMQ client with health checks |

---

## 3. Implementation Steps

### Step 1: Add NuGet Packages

Add the hosting package to the AppHost.

```bash
cd DDD.AppHost
dotnet add package Aspire.Hosting.RabbitMQ
```

Add the client package to WebApi.

```bash
cd WebApi
dotnet add package Aspire.RabbitMQ.Client
```

Verify `Directory.Packages.props` contains:
```xml
<PackageVersion Include="Aspire.Hosting.RabbitMQ" Version="9.*" />
<PackageVersion Include="Aspire.RabbitMQ.Client" Version="9.*" />
```

### Step 2: Add RabbitMQ to AppHost

Update `DDD.AppHost/Program.cs`:

**Before (without RabbitMQ):**
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

**After (with RabbitMQ):**
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("sql")
    .WithDataVolume()
    .AddDatabase("scheduling-db");

// Add RabbitMQ with management plugin enabled
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithDataVolume();

var webApi = builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(sqlServer)
    .WithReference(messaging)
    .WaitFor(sqlServer)
    .WaitFor(messaging);

builder.Build().Run();
```

**What each method does:**

| Method | Purpose |
|--------|---------|
| `AddRabbitMQ("messaging")` | Creates RabbitMQ resource with logical name "messaging" |
| `WithManagementPlugin()` | Enables the RabbitMQ Management UI |
| `WithDataVolume()` | Persists RabbitMQ data across container restarts |
| `WithReference(messaging)` | Injects connection string into the project |
| `WaitFor(messaging)` | Delays startup until RabbitMQ is healthy |

### Step 3: Register Aspire RabbitMQ Client in WebApi

Update `WebApi/Program.cs` to use Aspire's RabbitMQ client.

**Before (manual configuration):**
```csharp
using BuildingBlocks.Infrastructure.MassTransit.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Manual RabbitMQ connection from appsettings.json
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});
```

**After (Aspire-managed):**
```csharp
using BuildingBlocks.Infrastructure.MassTransit.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Register Aspire's RabbitMQ client (provides IConnection, health checks)
builder.AddRabbitMQClient("messaging");

// MassTransit now uses Aspire's connection
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});
```

**Note:** `builder.AddRabbitMQClient("messaging")` (extension on `IHostApplicationBuilder`) versus `builder.Services.Add...` (extension on `IServiceCollection`). Aspire client extensions use the builder directly.

### Step 4: Update MassTransit to Use Aspire's Connection

Aspire provides the connection string via `ConnectionStrings__messaging`. Update MassTransit to read from this.

**Option A: Read from Aspire's injected connection string**

Update `BuildingBlocks.Infrastructure.MassTransit/MassTransitExtensions.cs`:

**Before:**
```csharp
public static IServiceCollection AddMassTransitEventBus(
    this IServiceCollection services,
    IConfiguration configuration,
    Action<IRegistrationConfigurator>? configureConsumers = null)
{
    services.AddMassTransit(x =>
    {
        configureConsumers?.Invoke(x);

        x.UsingRabbitMq((context, cfg) =>
        {
            var rabbitMqSettings = configuration.GetSection("RabbitMQ");

            cfg.Host(
                rabbitMqSettings["Host"] ?? "localhost",
                rabbitMqSettings["VirtualHost"] ?? "/",
                h =>
                {
                    h.Username(rabbitMqSettings["Username"] ?? "guest");
                    h.Password(rabbitMqSettings["Password"] ?? "guest");
                });

            cfg.UseMessageRetry(r => { /* ... */ });
            cfg.ConfigureEndpoints(context);
        });
    });

    services.AddScoped<IEventBus, MassTransitEventBus>();
    return services;
}
```

**After:**
```csharp
public static IServiceCollection AddMassTransitEventBus(
    this IServiceCollection services,
    IConfiguration configuration,
    Action<IRegistrationConfigurator>? configureConsumers = null)
{
    services.AddMassTransit(x =>
    {
        configureConsumers?.Invoke(x);

        x.UsingRabbitMq((context, cfg) =>
        {
            // Try Aspire connection string first, fall back to manual config
            var connectionString = configuration.GetConnectionString("messaging");

            if (!string.IsNullOrEmpty(connectionString))
            {
                // Aspire provides: amqp://guest:guest@localhost:5672
                cfg.Host(new Uri(connectionString));
            }
            else
            {
                // Fallback for non-Aspire environments (CI, production)
                var rabbitMqSettings = configuration.GetSection("RabbitMQ");
                cfg.Host(
                    rabbitMqSettings["Host"] ?? "localhost",
                    rabbitMqSettings["VirtualHost"] ?? "/",
                    h =>
                    {
                        h.Username(rabbitMqSettings["Username"] ?? "guest");
                        h.Password(rabbitMqSettings["Password"] ?? "guest");
                    });
            }

            cfg.UseMessageRetry(r =>
            {
                r.Intervals(
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(15),
                    TimeSpan.FromSeconds(30)
                );

                r.Ignore<ValidationException>();
                r.Ignore<ArgumentException>();
            });

            cfg.ConfigureEndpoints(context);
        });
    });

    services.AddScoped<IEventBus, MassTransitEventBus>();
    return services;
}
```

**Option B: Use MassTransit's Aspire integration (recommended)**

MassTransit has built-in Aspire support via `Aspire.MassTransit.RabbitMQ`.

```bash
cd BuildingBlocks/BuildingBlocks.Infrastructure.MassTransit
dotnet add package Aspire.MassTransit.RabbitMQ
```

This package automatically configures MassTransit to use Aspire's connection string when available.

---

## 4. Connection String Injection

### How Aspire Injects Connection Strings

When you call `.WithReference(messaging)`, Aspire sets environment variables:

```
ConnectionStrings__messaging=amqp://guest:guest@localhost:5672
```

The `IConfiguration` system reads these automatically:
```csharp
configuration.GetConnectionString("messaging")
// Returns: "amqp://guest:guest@localhost:5672"
```

### Connection String Format

Aspire provides an AMQP URI:
```
amqp://username:password@host:port/vhost
```

Example:
```
amqp://guest:guest@localhost:55432
```

MassTransit's `cfg.Host(new Uri(connectionString))` parses this format directly.

---

## 5. Health Checks

### Automatic Health Checks with Aspire

`Aspire.RabbitMQ.Client` automatically registers health checks.

```csharp
// This is automatic - no manual setup required
builder.AddRabbitMQClient("messaging");
```

The health check endpoint reports RabbitMQ status:
```json
{
  "status": "Healthy",
  "results": {
    "RabbitMQ": {
      "status": "Healthy",
      "description": "RabbitMQ connection is established"
    }
  }
}
```

### Manual Health Check Registration (if needed)

If you need custom health check configuration:

```csharp
builder.AddRabbitMQClient("messaging", configureSettings: settings =>
{
    settings.DisableHealthChecks = false;
    settings.HealthCheckTimeout = 5000; // 5 seconds
});
```

### Exposing Health Endpoints

Ensure health endpoints are mapped:

```csharp
// In Program.cs
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => !check.Tags.Contains("ready")
});
```

---

## 6. RabbitMQ Management UI

### Accessing via Aspire Dashboard

With `WithManagementPlugin()`, the Management UI is accessible through the Aspire Dashboard.

1. Run the AppHost (F5)
2. Open Aspire Dashboard (https://localhost:17178 or similar)
3. Click on the "messaging" resource
4. Click the Management UI endpoint link

### Direct Access (development)

The Management UI is also available directly. Aspire assigns a random port; check the dashboard for the URL.

Typical format:
```
http://localhost:{random-port}
```

Login credentials (default):
- Username: `guest`
- Password: `guest`

### Viewing in Dashboard

The Aspire Dashboard shows:
- Container status (running/stopped)
- Port mappings
- Logs (real-time)
- Health status
- Endpoint URLs (click to open)

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
| Production | No (use managed service) | No |
| Team members without Aspire | Yes (fallback) | No |

### Recommended Approach

Keep `docker-compose.yml` as a fallback but update it with a comment:

```yaml
# docker-compose.yml
# NOTE: For local development, prefer running via Aspire (F5 on AppHost).
# This file is kept for CI/CD and environments without Aspire.

services:
  rabbitmq:
    image: rabbitmq:3-management
    container_name: ddd-rabbitmq
    ports:
      - "0.0.0.0:5672:5672"
      - "0.0.0.0:15672:15672"
    environment:
      - RABBITMQ_DEFAULT_USER=guest
      - RABBITMQ_DEFAULT_PASS=guest
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "check_running"]
      interval: 10s
      timeout: 5s
      retries: 5
    restart: unless-stopped

volumes:
  rabbitmq_data:
```

### Disable MSBuild Docker Target

If you added the `EnsureDockerServices` target from Phase 5, update it to skip when running via Aspire:

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
    "DefaultConnection": "Data Source=..."
  },
  "RabbitMQ": {
    "Host": "localhost",
    "VirtualHost": "/",
    "Username": "guest",
    "Password": "guest"
  }
}
```

**After (Aspire-managed):**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=..."
  }
  // RabbitMQ config removed - injected by Aspire
}
```

### Program.cs (WebApi)

**Before:**
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});

var app = builder.Build();
app.Run();
```

**After:**
```csharp
var builder = WebApplication.CreateBuilder(args);

// Aspire RabbitMQ client with health checks
builder.AddRabbitMQClient("messaging");

builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
```

### AppHost (Program.cs)

**Before (no RabbitMQ):**
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("sql")
    .WithDataVolume()
    .AddDatabase("scheduling-db");

builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(sqlServer)
    .WaitFor(sqlServer);

builder.Build().Run();
```

**After (with RabbitMQ):**
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("sql")
    .WithDataVolume()
    .AddDatabase("scheduling-db");

var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithDataVolume();

builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(sqlServer)
    .WithReference(messaging)
    .WaitFor(sqlServer)
    .WaitFor(messaging);

builder.Build().Run();
```

### Developer Workflow

**Before:**
```bash
# Terminal 1: Start infrastructure
docker-compose up -d

# Wait for RabbitMQ to be healthy
docker-compose ps  # Check status

# Terminal 2: Run application
dotnet run --project WebApi
```

**After:**
```bash
# Just press F5 in Visual Studio or:
dotnet run --project DDD.AppHost

# Everything starts automatically
# Dashboard shows all services
# Health checks verify readiness
```

---

## 9. Troubleshooting

### Common Issues

**Issue: Connection refused**
```
RabbitMQ.Client.Exceptions.BrokerUnreachableException: None of the specified endpoints were reachable
```

**Solution:** Ensure `.WaitFor(messaging)` is set in AppHost. This ensures the WebApi waits for RabbitMQ health check.

---

**Issue: Connection string not found**
```
System.InvalidOperationException: Connection string 'messaging' not found
```

**Solution:** Ensure `.WithReference(messaging)` is set. Run via AppHost, not WebApi directly.

---

**Issue: MassTransit not connecting**

Check if MassTransit is reading the Aspire connection string:

```csharp
// Debug: Log the connection string
var connectionString = configuration.GetConnectionString("messaging");
Console.WriteLine($"RabbitMQ connection: {connectionString}");
```

---

**Issue: Management UI not accessible**

Ensure `WithManagementPlugin()` is called:
```csharp
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin();  // Required for Management UI
```

---

### Viewing Logs

RabbitMQ logs are visible in:
1. Aspire Dashboard - Click on "messaging" resource, then "Logs"
2. Docker Desktop - View container logs
3. Console output when running AppHost

---

## 10. Reference

### Aspire RabbitMQ Methods

| Method | Purpose |
|--------|---------|
| `AddRabbitMQ(name)` | Add RabbitMQ resource to AppHost |
| `WithManagementPlugin()` | Enable Management UI |
| `WithDataVolume()` | Persist data across restarts |
| `WithDataBindMount(path)` | Bind mount for data persistence |
| `WithEnvironment(key, value)` | Set environment variables |

### Aspire Client Configuration

```csharp
builder.AddRabbitMQClient("messaging", configureSettings: settings =>
{
    settings.DisableHealthChecks = false;
    settings.HealthCheckTimeout = 5000;
    settings.DisableTracing = false;
});
```

### Useful Commands

```bash
# Run AppHost (starts all services)
dotnet run --project DDD.AppHost

# View Aspire Dashboard
# URL shown in console output, e.g., https://localhost:17178

# Check container status
docker ps | grep rabbitmq

# View RabbitMQ logs
docker logs ddd-rabbitmq
```

---

## Verification Checklist

- [ ] `Aspire.Hosting.RabbitMQ` added to AppHost
- [ ] `Aspire.RabbitMQ.Client` added to WebApi
- [ ] RabbitMQ added to AppHost with `AddRabbitMQ()`
- [ ] Management plugin enabled with `WithManagementPlugin()`
- [ ] WebApi references messaging with `.WithReference(messaging)`
- [ ] WebApi waits for messaging with `.WaitFor(messaging)`
- [ ] MassTransit reads Aspire connection string
- [ ] Health checks registered and accessible
- [ ] RabbitMQ Management UI accessible via Dashboard
- [ ] Application starts successfully via F5 on AppHost
- [ ] Messages publish and consume correctly

---

## Summary

Migrating RabbitMQ to Aspire provides:

1. **Simplified configuration** - No manual connection strings in appsettings.json
2. **Automatic container management** - No more `docker-compose up -d`
3. **Built-in health checks** - Readiness verification out of the box
4. **Integrated dashboard** - View all services, logs, and endpoints in one place
5. **Service discovery** - Connection strings injected automatically
6. **Consistent developer experience** - Same workflow for all infrastructure

The key changes are:
- AppHost: `AddRabbitMQ("messaging").WithManagementPlugin()`
- WebApi: `builder.AddRabbitMQClient("messaging")`
- MassTransit: Read from `configuration.GetConnectionString("messaging")`

Keep `docker-compose.yml` as a fallback for CI/CD and team members who may not have Aspire set up yet.

---

Next: [04-billing-bounded-context.md](./04-billing-bounded-context.md) - Creating the Billing bounded context with cross-context integration events
