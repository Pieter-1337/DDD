# RabbitMQ with .NET Aspire

This document covers migrating RabbitMQ from docker-compose to .NET Aspire, simplifying configuration and gaining automatic connection string injection, health checks, and dashboard integration.

**Key Insight:** MassTransit is already your RabbitMQ client. You don't need `Aspire.RabbitMQ.Client` - it would create a redundant second connection. Instead, configure MassTransit to read the connection string that Aspire injects.

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

**Note:** You do NOT need `Aspire.RabbitMQ.Client` in WebApi. MassTransit is your RabbitMQ client and manages its own connections. Adding the Aspire client package would create two separate connections to RabbitMQ, which is wasteful and confusing.

---

## 3. Implementation Steps

### Step 1: Add NuGet Package

Add the hosting package to the AppHost.

```bash
cd DDD.AppHost
dotnet add package Aspire.Hosting.RabbitMQ
```

Verify `Directory.Packages.props` contains:
```xml
<PackageVersion Include="Aspire.Hosting.RabbitMQ" Version="13.1.1" />
```

**Note:** This project uses Central Package Management (`ManagePackageVersionsCentrally=true`), which does not allow floating versions such as `9.*`. Always pin Aspire package versions to match the Aspire SDK version declared in `global.json` or `Directory.Build.props`. The current Aspire SDK version is `13.1.1`.

**Important:** Do NOT add `Aspire.RabbitMQ.Client` to WebApi. MassTransit handles all RabbitMQ client functionality.

### Step 2: Add RabbitMQ to AppHost

Update `DDD.AppHost/Program.cs`:

**Before (without RabbitMQ):**
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var webApi = builder.AddProject<Projects.WebApi>("webapi");

builder.Build().Run();
```

**After (with RabbitMQ):**
```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Add password parameter (reads from user secrets or generates random)
var messagingPassword = builder.AddParameter("messaging-password");

// Add RabbitMQ with management plugin enabled
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin()
    .WithDataVolume();

var webApi = builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(messaging)
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

### Step 3: Update MassTransit to Use Aspire's Connection String

Aspire injects the connection string as `ConnectionStrings__messaging` when you use `.WithReference(messaging)`. Update MassTransit to read from this.

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

**Why this approach?**
- MassTransit is already your RabbitMQ client - no need for a separate Aspire RabbitMQ client
- Fallback to manual configuration works for non-Aspire environments (CI/CD, production)
- Single connection to RabbitMQ, managed by MassTransit
- MassTransit provides its own health checks (covered in the next section)

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

### MassTransit Provides Built-In Health Checks

MassTransit automatically registers health checks for RabbitMQ when you add the event bus. No additional packages or configuration needed.

When you call:
```csharp
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});
```

MassTransit registers:
- RabbitMQ connection health check
- Consumer health checks
- Bus health check

### Health Check Response

The health check endpoint will report MassTransit status:
```json
{
  "status": "Healthy",
  "results": {
    "masstransit-bus": {
      "status": "Healthy",
      "description": "Bus is ready"
    }
  }
}
```

### Exposing Health Endpoints

Ensure health endpoints are mapped in `Program.cs`:

```csharp
var app = builder.Build();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();
```

### Aspire Dashboard Integration

The Aspire Dashboard automatically discovers and displays health check results. You'll see:
- Green status when RabbitMQ connection is healthy
- Red status when connection fails
- Health check details in the resource view

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

Login credentials:
- Username: `guest`
- Password: `guest` (configured via user secrets - see Section 7 below)

**Note:** By default, Aspire generates a random password for RabbitMQ and stores it in user secrets. For local development convenience, we override this with a known password. See the "User Secrets & Credentials" section below for configuration details.

### Viewing in Dashboard

The Aspire Dashboard shows:
- Container status (running/stopped)
- Port mappings
- Logs (real-time)
- Health status
- Endpoint URLs (click to open)

---

## 7. User Secrets & Credentials

### Default Aspire Behavior

By default, Aspire generates a random password for RabbitMQ and stores it in user secrets under the key `Parameters:messaging-password`. This password is unique per developer machine and is used to secure the RabbitMQ instance.

### Overriding for Local Development

For local development convenience, we override the default random password with a known password (`guest`) so team members can easily access the Management UI without hunting for auto-generated credentials.

### AppHost Configuration

Update `DDD.AppHost/Program.cs` to use a parameterized password:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Add password parameter (reads from user secrets or generates random)
var messagingPassword = builder.AddParameter("messaging-password");

// Add RabbitMQ with management plugin and parameterized password
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin()
    .WithDataVolume();

var webApi = builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(messaging)
    .WaitFor(messaging);

builder.Build().Run();
```

### Shared User Secrets Across Projects

All WebApi projects should reference the **same `UserSecretsId`** as the Aspire AppHost. This creates a single secrets store for all development configuration.

Add the AppHost's `UserSecretsId` to each WebApi project's `.csproj`:

```xml
<PropertyGroup>
  <UserSecretsId>12d3119a-ea1f-43ad-b1f3-6c5072eb7dcd</UserSecretsId>
</PropertyGroup>
```

This way, secrets set via `--project Aspire.AppHost` are also available to WebApi when running standalone (without Aspire).

### Setting Secrets for Local Development

```bash
# RabbitMQ password (for Aspire-managed container)
dotnet user-secrets set "Parameters:messaging-password" "guest" --project Aspire.AppHost

# SQL Server connection string (used by WebApi directly, not managed by Aspire)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Data Source=MSI;Initial Catalog=DDD;Integrated Security=true;MultipleActiveResultSets=true;TrustServerCertificate=True" --project Aspire.AppHost
```

This keeps connection strings and credentials **out of `appsettings.json`** and out of source control.

### Resulting Credentials

After setting the user secrets:
- **RabbitMQ Management UI:** `guest` / `guest`
- **SQL Server:** via connection string in user secrets (no credentials in appsettings.json)

### Production Considerations

**Important:** User secrets are for local development only. They are stored on your local machine and are not deployed to production.

For production environments:
- Use **Azure Key Vault** to store sensitive credentials
- Aspire supports Azure Key Vault integration via `AddAzureKeyVault()`
- Never commit secrets to source control
- Use managed identities for production authentication

---

## 8. What Happens to docker-compose.yml?

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

If you added the `EnsureDockerServices` target from Phase 5, update it to automatically set `SKIP_DOCKER_CHECK=1` when Aspire is the launcher. This keeps a single condition on the target:

```xml
<!-- Directory.Build.targets -->
<PropertyGroup Condition="'$(ASPIRE_LAUNCHER)' != ''">
  <SKIP_DOCKER_CHECK>1</SKIP_DOCKER_CHECK>
</PropertyGroup>

<Target Name="EnsureDockerServices" BeforeTargets="Build" Condition="'$(SKIP_DOCKER_CHECK)' != '1'">
  <Exec Command="powershell -ExecutionPolicy Bypass -File &quot;$(MSBuildThisFileDirectory)scripts\ensure-docker.ps1&quot; -TimeoutSeconds 60"
        IgnoreExitCode="false"
        Timeout="90000" />
  <!-- Timeout is in milliseconds: 90000ms = 90 seconds (script timeout 60s + 30s buffer) -->
</Target>
```

This ensures the docker check:
- **Skips** when launched via Aspire (`ASPIRE_LAUNCHER` automatically sets `SKIP_DOCKER_CHECK=1`)
- **Skips** when `SKIP_DOCKER_CHECK=1` is set manually
- **Runs** with all existing timeouts and parameters in every other case

---

## 9. Before/After Comparison

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

// MassTransit reads Aspire connection string automatically
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
```

**What changed?**
- MassTransit now reads `configuration.GetConnectionString("messaging")` internally
- Health checks are provided by MassTransit (no separate client needed)
- Added health endpoint mapping for monitoring

### AppHost (Program.cs)

**Before (no RabbitMQ):**
```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.WebApi>("webapi");

builder.Build().Run();
```

**After (with RabbitMQ):**
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var messagingPassword = builder.AddParameter("messaging-password");

var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin()
    .WithDataVolume();

builder.AddProject<Projects.WebApi>("webapi")
    .WithReference(messaging)
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

## 10. Troubleshooting

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
var messagingPassword = builder.AddParameter("messaging-password");
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin();  // Required for Management UI
```

---

### Viewing Logs

RabbitMQ logs are visible in:
1. Aspire Dashboard - Click on "messaging" resource, then "Logs"
2. Docker Desktop - View container logs
3. Console output when running AppHost

---

## 11. Reference

### Aspire RabbitMQ Methods

| Method | Purpose |
|--------|---------|
| `AddRabbitMQ(name)` | Add RabbitMQ resource to AppHost |
| `WithManagementPlugin()` | Enable Management UI |
| `WithDataVolume()` | Persist data across restarts |
| `WithDataBindMount(path)` | Bind mount for data persistence |
| `WithEnvironment(key, value)` | Set environment variables |
| `WithReference(messaging)` | Inject connection string into consuming project |
| `WaitFor(messaging)` | Delay startup until RabbitMQ is healthy |

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
- [ ] Password parameter configured with `AddParameter("messaging-password")`
- [ ] RabbitMQ added to AppHost with `AddRabbitMQ("messaging", password: messagingPassword)`
- [ ] User secret set: `dotnet user-secrets set "Parameters:messaging-password" "guest" --project DDD.AppHost`
- [ ] Management plugin enabled with `WithManagementPlugin()`
- [ ] WebApi references messaging with `.WithReference(messaging)`
- [ ] WebApi waits for messaging with `.WaitFor(messaging)`
- [ ] MassTransit reads Aspire connection string via `configuration.GetConnectionString("messaging")`
- [ ] Health checks accessible at `/health` endpoint (provided by MassTransit)
- [ ] RabbitMQ Management UI accessible via Aspire Dashboard with guest/guest credentials
- [ ] Application starts successfully via F5 on AppHost
- [ ] Messages publish and consume correctly
- [ ] Verify only ONE connection to RabbitMQ (from MassTransit, not a separate Aspire client)

---

## Summary

Migrating RabbitMQ to Aspire provides:

1. **Simplified configuration** - No manual connection strings in appsettings.json
2. **Automatic container management** - No more `docker-compose up -d`
3. **Built-in health checks** - MassTransit provides health checks out of the box
4. **Integrated dashboard** - View all services, logs, and endpoints in one place
5. **Service discovery** - Connection strings injected automatically via `.WithReference()`
6. **Consistent developer experience** - Same workflow for all infrastructure

The key changes are:
- **AppHost**: `AddRabbitMQ("messaging", password: messagingPassword).WithManagementPlugin()` and `.WithReference(messaging)`
- **User Secrets**: Set `Parameters:messaging-password` to `guest` for local development convenience
- **MassTransit**: Read from `configuration.GetConnectionString("messaging")` with fallback to manual config
- **No Aspire RabbitMQ client needed** - MassTransit is already your RabbitMQ client

**Why no `Aspire.RabbitMQ.Client`?**

MassTransit is a full-featured message bus abstraction that manages its own RabbitMQ connections. Adding `Aspire.RabbitMQ.Client` would create a second, separate connection to RabbitMQ that goes unused. The Aspire hosting package (`Aspire.Hosting.RabbitMQ`) handles container orchestration and connection string injection - that's all you need. MassTransit reads the injected connection string and manages the actual RabbitMQ client connection.

Keep `docker-compose.yml` as a fallback for CI/CD and team members who may not have Aspire set up yet.

---

> Next: [04-billing-bounded-context.md](./04-billing-bounded-context.md) - Creating the Billing bounded context with cross-context integration events
