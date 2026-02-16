# .NET Aspire Setup

This document covers setting up .NET Aspire to orchestrate your distributed application, providing a unified dashboard, service discovery, and simplified configuration management.

---

## 1. What Is .NET Aspire?

.NET Aspire is an opinionated stack for building cloud-native, distributed applications. It provides:

| Feature | Description |
|---------|-------------|
| **App Model** | Define your distributed app topology in C# |
| **Dashboard** | Real-time view of logs, traces, and metrics for all services |
| **Service Discovery** | Automatic endpoint resolution between services |
| **Health Checks** | Built-in health monitoring |
| **Configuration** | Simplified environment and connection string management |
| **Resource Orchestration** | Start containers (RabbitMQ, SQL Server) alongside your services |

```
Traditional Setup:
  - Start Docker containers manually
  - Configure connection strings in appsettings.json
  - Run each project separately
  - Check logs in different terminal windows

With Aspire:
  - F5 starts everything: containers + services
  - Connection strings injected automatically
  - Single dashboard for all logs, traces, metrics
  - Service discovery "just works"
```

---

## 2. Prerequisites

Before setting up Aspire, ensure you have:

### Required Software

| Requirement | Minimum Version | Check Command |
|-------------|-----------------|---------------|
| Visual Studio 2022 | 17.9+ | Help > About |
| .NET SDK | 9.0+ | `dotnet --version` |
| Docker Desktop | Latest | `docker --version` |
| Aspire Project Templates | 9.0+ | `dotnet new list aspire` |

### Install the Aspire Templates

Starting with .NET Aspire 9, the workload is **no longer required**. Aspire is now fully NuGet-based. Install the project templates:

```bash
# Install the Aspire project templates
dotnet new install Aspire.ProjectTemplates

# Verify installation - should show aspire-apphost, aspire-servicedefaults, etc.
dotnet new list aspire
```

### Visual Studio Configuration (Optional)

The .NET Aspire SDK component in Visual Studio is **optional** for Aspire 9+. The NuGet packages provide all necessary functionality.

If you still want to install it:
1. Open **Visual Studio Installer**
2. Click **Modify** on your VS 2022 installation
3. Check **.NET Aspire SDK** under **Individual Components**
4. Click **Modify** to apply changes

---

## 3. Project Structure Overview

Aspire adds two new projects to your solution:

```
DDD.sln
+-- Aspire.AppHost/                 <- NEW: Orchestration project
|   +-- AppHost.cs                  <- Defines app topology
|   +-- Aspire.AppHost.csproj
|
+-- ServiceDefaults/                <- NEW: Shared service configuration
|   +-- Extensions.cs               <- OpenTelemetry, health checks, etc.
|   +-- Aspire.ServiceDefaults.csproj
|
+-- WebApi/                         <- Existing: Your Scheduling API
|   +-- Program.cs                  <- Modified to use ServiceDefaults
|   +-- WebApi.csproj
|
+-- Core/Scheduling/...             <- Existing: Domain, Application, Infrastructure
```

### Project Responsibilities

| Project | Purpose |
|---------|---------|
| **Aspire.AppHost** | The "control tower" - orchestrates all services and resources |
| **Aspire.ServiceDefaults** | Shared configuration for observability, resilience, health checks |
| **WebApi** | Your existing API, now enhanced with Aspire defaults |

---

## 4. Create the ServiceDefaults Project

The ServiceDefaults project contains shared configuration that all services in your distributed app will use.

### Step 1: Create the Project

```bash
# Navigate to solution root
cd C:/projects/DDD/DDD

# Create ServiceDefaults project using Aspire template
dotnet new aspire-servicedefaults -n Aspire.ServiceDefaults -o ServiceDefaults

# Add to solution
dotnet sln add ServiceDefaults/Aspire.ServiceDefaults.csproj
```

### Step 2: Review the Generated Code

The template creates `Extensions.cs` with standard configuration:

**ServiceDefaults/Extensions.cs**:
```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Health check endpoints
        app.MapHealthChecks("/health");

        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}
```

### What ServiceDefaults Provides

| Feature | Description |
|---------|-------------|
| **OpenTelemetry** | Automatic tracing, metrics, and logging export to Aspire Dashboard |
| **Health Checks** | `/health` and `/alive` endpoints |
| **Service Discovery** | Resolves service names to actual endpoints |
| **Resilience** | Built-in retry policies for HTTP clients |

---

## 5. Create the AppHost Project

The AppHost project is the orchestrator that defines your distributed application's topology.

### Step 1: Create the Project

```bash
# Navigate to solution root
cd C:/projects/DDD/DDD

# Create AppHost project using Aspire template
dotnet new aspire-apphost -n Aspire.AppHost -o Aspire.AppHost

# Add to solution
dotnet sln add Aspire.AppHost/Aspire.AppHost.csproj
```

### Step 2: Add Project References

The AppHost needs references to all projects it orchestrates:

```bash
cd Aspire.AppHost

# Reference the WebApi project
dotnet add reference ../WebApi/WebApi.csproj
```

### Step 3: Configure the App Model

Edit the generated `AppHost.cs` to define your distributed application:

**Aspire.AppHost/AppHost.cs**:
```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Add the Scheduling WebApi
var schedulingApi = builder.AddProject<Projects.WebApi>("scheduling-api");

builder.Build().Run();
```

This minimal setup:
- Registers the WebApi project with the name `scheduling-api`
- Aspire will start it when you run the AppHost
- The Dashboard will show logs, traces, and metrics for this service

### Understanding the App Model

```csharp
// AddProject<T> - adds a .NET project to be orchestrated
var api = builder.AddProject<Projects.WebApi>("scheduling-api");

// The name "scheduling-api" is used for:
// - Service discovery (other services can call http://scheduling-api)
// - Dashboard identification
// - Logging correlation
```

---

## 6. Modify WebApi to Use ServiceDefaults

Update your existing WebApi to use the shared ServiceDefaults configuration.

### Step 1: Add Reference to ServiceDefaults

```bash
cd WebApi
dotnet add reference ../ServiceDefaults/Aspire.ServiceDefaults.csproj
```

### Step 2: Update Program.cs

Modify your existing `Program.cs` to use ServiceDefaults:

**WebApi/Program.cs**:
```csharp
using BuildingBlocks.Application;
using BuildingBlocks.Infrastructure.MassTransit.Configuration;
using BuildingBlocks.WebApplications.Filters;
using BuildingBlocks.WebApplications.Json;
using MassTransit;
using Scheduling.Application;
using Scheduling.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire ServiceDefaults (OpenTelemetry, health checks, resilience)
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddControllers(options =>
{
    options.SuppressAsyncSuffixInActionNames = false;
    options.Filters.Add<ExceptionToJsonFilter>();
}).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new SmartEnumJsonConverterFactory());
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add infrastructure
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddSchedulingInfrastructure(connectionString);
builder.Services.AddSchedulingApplication();
builder.Services.AddDefaultPipelineBehaviors();

// Add MassTransit for event-driven messaging
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    // Register consumers from bounded context assemblies
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});

var app = builder.Build();

// Map Aspire default endpoints (health checks)
app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
```

### Changes Made

| Line | Change | Purpose |
|------|--------|---------|
| `builder.AddServiceDefaults();` | Added after `CreateBuilder` | Configures OpenTelemetry, health checks, resilience |
| `app.MapDefaultEndpoints();` | Added before other middleware | Exposes `/health` and `/alive` endpoints |

---

## 7. Project References Summary

After setup, your project references should look like:

```
Aspire.AppHost
+-- References: WebApi (for orchestration)

Aspire.ServiceDefaults
+-- References: (none - standalone)

WebApi
+-- References: Aspire.ServiceDefaults
+-- References: Scheduling.Infrastructure (transitively includes Scheduling.Application, Scheduling.Domain)
+-- References: BuildingBlocks.Infrastructure.MassTransit
+-- References: BuildingBlocks.WebApplications
```

### Verify with csproj Files

**Aspire.AppHost/Aspire.AppHost.csproj**:
```xml
<Project Sdk="Aspire.AppHost.Sdk/13.1.1">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsAspireHost>true</IsAspireHost>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\WebApi\WebApi.csproj" />
  </ItemGroup>

</Project>
```

**ServiceDefaults/Aspire.ServiceDefaults.csproj**:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsAspireSharedProject>true</IsAspireSharedProject>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />

    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
  </ItemGroup>

</Project>
```

**Important:** This project uses Central Package Management (CPM). The ServiceDefaults csproj must **not** have `Version` attributes on `PackageReference` items. Instead, add the versions to `Directory.Packages.props`:

```xml
<!-- Aspire ServiceDefaults -->
<PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.1.0" />
<PackageVersion Include="Microsoft.Extensions.ServiceDiscovery" Version="10.1.0" />
<PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.14.0" />
<PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.14.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.14.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.14.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.14.0" />
```

> **Tip:** The Aspire template generates the csproj with hardcoded versions. If you have `ManagePackageVersionsCentrally` enabled in `Directory.Packages.props`, you must move the versions there and remove them from the csproj, otherwise you'll get `NU1008` build errors.

---

## 8. Running with Aspire

### Set AppHost as Startup Project

1. In Visual Studio, right-click **Aspire.AppHost**
2. Select **Set as Startup Project**
3. Press **F5** to start debugging

### What Happens on F5

```
1. Aspire Dashboard starts (localhost:18888 by default)
2. AppHost reads AppHost.cs to understand app topology
3. Each AddProject<T> project is started
4. OpenTelemetry is configured to export to Dashboard
5. Browser opens to Dashboard
```

### Using dotnet CLI

```bash
# Navigate to AppHost project
cd C:/projects/DDD/DDD/Aspire.AppHost

# Run the distributed application
dotnet run
```

---

## 9. The Aspire Dashboard

The Dashboard is the primary developer experience for Aspire.

### Accessing the Dashboard

When you run the AppHost:
- Dashboard URL is printed to console: `Login to the dashboard at https://localhost:18888/login?t=...`
- In Visual Studio, the browser opens automatically

### Dashboard Features

| Tab | Description |
|-----|-------------|
| **Resources** | All services, containers, and their status |
| **Console** | Real-time console output from all services |
| **Structured Logs** | Searchable, filterable log entries |
| **Traces** | Distributed tracing across services |
| **Metrics** | Runtime metrics, HTTP request metrics |

### Resources Tab

```
+-----------------+--------+----------+----------------------------+
| Name            | Type   | State    | Endpoints                  |
+-----------------+--------+----------+----------------------------+
| scheduling-api  | Project| Running  | https://localhost:7xxx     |
+-----------------+--------+----------+----------------------------+
```

### Structured Logs

- Filter by service name
- Filter by log level (Information, Warning, Error)
- Search log message content
- Click a log entry to see full details including trace context

### Traces

- Visualize request flow across services
- See timing breakdown for each span
- Identify bottlenecks and errors
- Click through to related logs

---

## 10. Configuration and Environment Variables

Aspire simplifies configuration management.

### How Configuration Works

```csharp
// In Aspire.AppHost/AppHost.cs
var api = builder.AddProject<Projects.WebApi>("scheduling-api")
    .WithEnvironment("MyKey", "MyValue");

// In WebApi, access via IConfiguration
var value = builder.Configuration["MyKey"]; // "MyValue"
```

### Connection Strings

When you add Aspire-managed resources (covered in next doc), Aspire injects connection strings.

```csharp
// AppHost defines Aspire-managed resources (RabbitMQ only)
var messagingPassword = builder.AddParameter("messaging-password");
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword);

var api = builder.AddProject<Projects.WebApi>("scheduling-api")
    .WithReference(messaging);  // RabbitMQ connection string injected

// WebApi receives RabbitMQ connection string automatically
// Configuration["ConnectionStrings:messaging"] is set by Aspire

// SQL Server connection string comes from user secrets
// Configuration["ConnectionStrings:DefaultConnection"] set via dotnet user-secrets
```

### Environment-Specific Configuration

AppHost respects `launchSettings.json`:

**Aspire.AppHost/Properties/launchSettings.json**:
```json
{
  "profiles": {
    "https": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "https://localhost:17171;http://localhost:15047",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "DOTNET_ENVIRONMENT": "Development",
        "DOTNET_DASHBOARD_OTLP_ENDPOINT_URL": "https://localhost:21147",
        "DOTNET_RESOURCE_SERVICE_ENDPOINT_URL": "https://localhost:22289"
      }
    }
  }
}
```

---

## 11. Verification Checklist

After completing this setup:

- [ ] Aspire templates installed (`dotnet new list aspire` shows templates)
- [ ] Aspire.ServiceDefaults project created and added to solution
- [ ] Aspire.AppHost project created and added to solution
- [ ] AppHost references WebApi project
- [ ] WebApi references ServiceDefaults
- [ ] WebApi Program.cs calls `AddServiceDefaults()` and `MapDefaultEndpoints()`
- [ ] AppHost is set as startup project
- [ ] F5 launches Aspire Dashboard
- [ ] Dashboard shows scheduling-api in Resources tab
- [ ] Health check endpoint works: `GET https://localhost:xxxx/health`
- [ ] Logs appear in Dashboard Structured Logs tab

---

## 12. Troubleshooting

### Common Issues

**"Aspire templates not found"**
```bash
# Install Aspire project templates (Aspire 9+ uses NuGet, not workloads)
dotnet new install Aspire.ProjectTemplates

# Update to latest version
dotnet new update
```

**"Projects.WebApi not found in AppHost"**
- Ensure AppHost has a `<ProjectReference>` to WebApi
- Rebuild the solution

**"Dashboard not opening"**
- Check console output for the Dashboard URL
- May require accepting a self-signed certificate

**"Health check endpoint returns 404"**
- Ensure `app.MapDefaultEndpoints()` is called in Program.cs
- Must be called before or after `app.MapControllers()`

**"OpenTelemetry traces not appearing"**
- ServiceDefaults configures OTLP export to Aspire Dashboard
- Ensure `AddServiceDefaults()` is called early in Program.cs

---

## Summary

You've set up .NET Aspire for your DDD learning project:

1. **Aspire.ServiceDefaults** - Shared configuration for OpenTelemetry, health checks, and resilience
2. **Aspire.AppHost** - Orchestrator that defines your distributed application topology
3. **Modified WebApi** - Now uses ServiceDefaults for observability

### Current Architecture

```
Aspire.AppHost (Orchestrator)
    |
    +-- starts --> WebApi (scheduling-api)
                      |
                      +-- uses --> ServiceDefaults
                      +-- uses --> Scheduling.Infrastructure
                      +-- uses --> MassTransit + RabbitMQ
```

### Benefits So Far

- **Single F5** - Starts everything
- **Unified Dashboard** - Logs, traces, metrics in one place
- **Health Checks** - Automatic `/health` and `/alive` endpoints
- **OpenTelemetry** - Tracing and metrics configured automatically

### What's Next

In the next document, we'll add RabbitMQ and SQL Server as Aspire resources, replacing the manual Docker Compose setup with Aspire-managed containers.

---

> Next: [03-rabbitmq-with-aspire.md](./03-rabbitmq-with-aspire.md) - Adding RabbitMQ as an Aspire resource with automatic connection string injection and health checks
