# Blazor Project Setup

This document covers setting up a Blazor Server project as the frontend for the DDD learning project, integrating with the existing Aspire-orchestrated backend APIs.

---

## 1. What Is Blazor Server?

Blazor Server is a web UI framework that lets you build interactive web applications using C# instead of JavaScript. It uses a real-time SignalR connection between the browser and server to handle UI updates and user interactions.

### How Blazor Server Works

```
Browser (UI)
    |
    | SignalR WebSocket Connection (persistent)
    |
    v
Server (Blazor Runtime)
    |
    +-- Component rendering
    +-- Event handling
    +-- State management
    +-- Calls to backend APIs
```

### Key Characteristics

| Aspect | Description |
|--------|-------------|
| **Rendering** | Server-side rendering - components execute on the server |
| **Connection** | Persistent SignalR connection for UI updates |
| **State** | Maintained on the server (circuit state) |
| **Execution** | C# code runs on the server, not in browser |
| **Latency** | Every UI interaction requires a server round-trip |

### Blazor Server vs WebAssembly vs Blazor United

| Feature | Blazor Server | Blazor WebAssembly | Blazor United (.NET 8+) |
|---------|---------------|-------------------|------------------------|
| **Execution** | Server-side | Client-side (browser) | Hybrid (server + client) |
| **Connection** | SignalR required | No persistent connection | Flexible |
| **Initial Load** | Fast | Slow (downloads .NET runtime) | Fast (server-rendered) |
| **Offline** | Not supported | Supported | Depends on mode |
| **Security** | Secure (code on server) | Less secure (code in browser) | Secure for server parts |
| **Scalability** | Limited by SignalR connections | High (no server load) | Balanced |

### Why Blazor Server for This Project?

We chose Blazor Server because:

1. **C# Everywhere** - Leverage existing C# skills across frontend and backend
2. **Security** - Business logic stays on the server, not exposed in browser
3. **.NET Ecosystem** - Use the same libraries, tools, and patterns as the backend
4. **Simplicity** - No need to manage client/server synchronization complexity
5. **Aspire Integration** - Works seamlessly with .NET Aspire orchestration
6. **Learning Focus** - Keeps focus on DDD patterns rather than JavaScript frameworks

For a production application with heavy interactivity or offline requirements, consider Blazor United or WebAssembly.

---

## 2. Step 1: Create the Blazor Server Project

Create the project using the .NET 9 Blazor template with interactive server rendering.

```bash
# Navigate to solution root
cd C:\projects\DDD\DDD

# Create Blazor Server project
dotnet new blazor -n Scheduling.BlazorApp -o 05. Frontend/Blazor/Scheduling.BlazorApp --interactivity Server

# Add to solution
dotnet sln add 05. Frontend/Blazor/Scheduling.BlazorApp
```

### Understanding the Template Parameters

| Parameter | Value | Purpose |
|-----------|-------|---------|
| `blazor` | Template name | Creates a Blazor web application |
| `-n Scheduling.BlazorApp` | Project name | Names the project and namespace |
| `-o 05. Frontend/Blazor/Scheduling.BlazorApp` | Output directory | Places project in 05. Frontend/Blazor folder |
| `--interactivity Server` | Render mode | Configures Blazor Server (SignalR-based) |

### Generated Project Structure

```
05. Frontend/Blazor/Scheduling.BlazorApp/
├── Components/
│   ├── Layout/
│   │   ├── MainLayout.razor          # Main layout wrapper
│   │   ├── NavMenu.razor             # Navigation sidebar
│   │   └── MainLayout.razor.css      # Layout styles
│   ├── Pages/
│   │   ├── Home.razor                # Home page
│   │   ├── Counter.razor             # Example interactive page
│   │   └── Weather.razor             # Example data page
│   ├── _Imports.razor                # Global using directives
│   ├── App.razor                     # Root component
│   └── Routes.razor                  # Routing configuration
├── wwwroot/
│   ├── app.css                       # Global styles
│   └── favicon.png                   # Site icon
├── appsettings.json                  # Configuration
├── appsettings.Development.json      # Dev configuration
└── Program.cs                        # Application startup
```

---

## 3. Step 2: Add FluentUI Blazor

FluentUI Blazor provides Microsoft's Fluent Design System components for Blazor applications.

### Install the NuGet Package

```bash
cd 05. Frontend/Blazor/Scheduling.BlazorApp
dotnet add package Microsoft.FluentUI.AspNetCore.Components
```

**Note:** This project uses Central Package Management. After adding the package, move the version to `Directory.Packages.props`:

```xml
<!-- In Directory.Packages.props at solution root -->
<PackageVersion Include="Microsoft.FluentUI.AspNetCore.Components" Version="4.10.2" />
```

Then remove the `Version` attribute from the csproj:
```xml
<!-- In Scheduling.BlazorApp.csproj -->
<PackageReference Include="Microsoft.FluentUI.AspNetCore.Components" />
```

### Configure FluentUI in Program.cs

Add FluentUI services to the DI container:

**05. Frontend/Blazor/Scheduling.BlazorApp/Program.cs**:
```csharp
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add FluentUI Blazor components
builder.Services.AddFluentUIComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

### Add FluentUI to _Imports.razor

Add the FluentUI namespace globally so it's available in all components:

**05. Frontend/Blazor/Scheduling.BlazorApp/Components/_Imports.razor**:
```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using Scheduling.BlazorApp
@using Scheduling.BlazorApp.Components
@using Microsoft.FluentUI.AspNetCore.Components
```

### Add FluentUI Assets to App.razor

Include FluentUI CSS and JavaScript in the root component:

**05. Frontend/Blazor/Scheduling.BlazorApp/Components/App.razor**:
```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <link rel="stylesheet" href="app.css" />
    <link rel="stylesheet" href="Scheduling.BlazorApp.styles.css" />

    <!-- FluentUI Blazor CSS -->
    <link href="_content/Microsoft.FluentUI.AspNetCore.Components/css/reboot.css" rel="stylesheet" />

    <link rel="icon" type="image/png" href="favicon.png" />
    <HeadOutlet />
</head>
<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>

    <!-- FluentUI Blazor JavaScript -->
    <script src="_content/Microsoft.FluentUI.AspNetCore.Components/Microsoft.FluentUI.AspNetCore.Components.lib.module.js" type="module" async></script>
</body>
</html>
```

---

## 4. Step 3: Register in Aspire AppHost

Add the Blazor app to the Aspire orchestration so it starts alongside the backend APIs.

### Add Project Reference to AppHost

```bash
cd Aspire.AppHost
dotnet add reference ../05. Frontend/Blazor/Scheduling.BlazorApp/Scheduling.BlazorApp.csproj
```

### Update AppHost.cs

Add the Blazor app to the distributed application model:

**Aspire.AppHost/AppHost.cs**:
```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Add RabbitMQ with management plugin enabled
var messagingPassword = builder.AddParameter("messaging-password");
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin()
    .WithDataVolume();

// Add backend APIs
var schedulingApi = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-webapi")
    .WithReference(messaging)
    .WaitFor(messaging);

var billingApi = builder.AddProject<Projects.Billing_WebApi>("billing-webapi")
    .WithReference(messaging)
    .WaitFor(messaging);

// Add Blazor frontend
var blazorApp = builder.AddProject<Projects.Scheduling_BlazorApp>("scheduling-blazorapp")
    .WithReference(schedulingApi)
    .WithReference(billingApi)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

### Understanding the Configuration

| Method | Purpose |
|--------|---------|
| `AddProject<Projects.Scheduling_BlazorApp>()` | Registers the Blazor project with Aspire |
| `.WithReference(schedulingApi)` | Enables service discovery to Scheduling API |
| `.WithReference(billingApi)` | Enables service discovery to Billing API |
| `.WithExternalHttpEndpoints()` | Makes Blazor app accessible from external browsers |

---

## 5. Step 4: Configure HttpClient for API Access

Configure typed HttpClients to communicate with backend APIs using Aspire service discovery.

### Add ServiceDefaults Reference

First, add a reference to the Aspire ServiceDefaults project:

```bash
cd 05. Frontend/Blazor/Scheduling.BlazorApp
dotnet add reference ../../../ServiceDefaults/Aspire.ServiceDefaults.csproj
```

### Configure HttpClients in Program.cs

Update Program.cs to register HttpClients with Aspire service discovery:

**05. Frontend/Blazor/Scheduling.BlazorApp/Program.cs**:
```csharp
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire ServiceDefaults (OpenTelemetry, health checks, resilience, service discovery)
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add FluentUI Blazor components
builder.Services.AddFluentUIComponents();

// Register HttpClients for backend APIs using Aspire service discovery
builder.Services.AddHttpClient("SchedulingApi", client =>
{
    // Aspire resolves "https+http://scheduling-webapi" to the actual URL
    client.BaseAddress = new Uri("https+http://scheduling-webapi");
});

builder.Services.AddHttpClient("BillingApi", client =>
{
    client.BaseAddress = new Uri("https+http://billing-webapi");
});

var app = builder.Build();

// Map Aspire default endpoints (health checks)
app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

### How Aspire Service Discovery Works

```
HttpClient Configuration:
  client.BaseAddress = new Uri("https+http://scheduling-webapi");
                                 ↓
                    Aspire Service Discovery
                                 ↓
              Resolves "scheduling-webapi" to actual endpoint
                                 ↓
              e.g., https://localhost:7001
```

The `https+http://` scheme tells Aspire to prefer HTTPS but fall back to HTTP if needed.

### Create an API Service Wrapper (Optional)

For cleaner component code, create a service that wraps the HttpClient:

**05. Frontend/Blazor/Scheduling.BlazorApp/Services/PatientApiService.cs**:
```csharp
namespace Scheduling.BlazorApp.Services;

public class PatientApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PatientApiService> _logger;

    public PatientApiService(
        IHttpClientFactory httpClientFactory,
        ILogger<PatientApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<List<PatientDto>?> GetPatientsAsync(CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SchedulingApi");
            return await client.GetFromJsonAsync<List<PatientDto>>("/api/patients", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching patients from API");
            throw;
        }
    }

    public async Task<PatientDto?> GetPatientByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SchedulingApi");
            return await client.GetFromJsonAsync<PatientDto>($"/api/patients/{id}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching patient {PatientId} from API", id);
            throw;
        }
    }
}

// DTO matching the API response
public record PatientDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    DateTime DateOfBirth);
```

Register the service:
```csharp
// In Program.cs
builder.Services.AddScoped<PatientApiService>();
```

---

## 6. Step 5: Project Structure

Organize the Blazor project following Blazor conventions and Clean Architecture principles.

### Recommended Structure

```
05. Frontend/Blazor/Scheduling.BlazorApp/
├── Components/
│   ├── Layout/
│   │   ├── MainLayout.razor              # Main layout wrapper
│   │   ├── NavMenu.razor                 # Navigation sidebar
│   │   └── MainLayout.razor.css          # Layout styles
│   ├── Pages/
│   │   ├── Home.razor                    # Home page
│   │   ├── Patients/
│   │   │   ├── PatientList.razor         # List all patients
│   │   │   ├── PatientDetail.razor       # View patient details
│   │   │   └── CreatePatient.razor       # Create new patient
│   │   ├── Appointments/
│   │   │   ├── AppointmentList.razor
│   │   │   ├── AppointmentDetail.razor
│   │   │   └── ScheduleAppointment.razor
│   ├── Shared/
│   │   ├── ErrorBoundary.razor           # Error handling component
│   │   └── LoadingSpinner.razor          # Reusable loading indicator
│   ├── App.razor                         # Root component
│   ├── Routes.razor                      # Routing configuration
│   └── _Imports.razor                    # Global using directives
├── Services/
│   ├── PatientApiService.cs              # Patient API wrapper
│   └── AppointmentApiService.cs          # Appointment API wrapper
├── Models/
│   ├── PatientDto.cs                     # Patient data transfer object
│   └── AppointmentDto.cs                 # Appointment data transfer object
├── wwwroot/
│   ├── app.css                           # Global styles
│   └── favicon.png                       # Site icon
├── appsettings.json                      # Configuration
├── appsettings.Development.json          # Dev configuration
└── Program.cs                            # Application startup
```

### Folder Responsibilities

| Folder | Purpose |
|--------|---------|
| **Components/Layout/** | Reusable layout components (navbar, footer, etc.) |
| **Components/Pages/** | Routable pages (one per route) |
| **Components/Shared/** | Shared UI components used across multiple pages |
| **Services/** | API client wrappers and business logic |
| **Models/** | DTOs and view models |
| **wwwroot/** | Static files (CSS, images, JavaScript) |

---

## 7. Step 6: Complete Program.cs

Here's the complete Program.cs with all configurations:

**05. Frontend/Blazor/Scheduling.BlazorApp/Program.cs**:
```csharp
using Microsoft.FluentUI.AspNetCore.Components;
using Scheduling.BlazorApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire ServiceDefaults (OpenTelemetry, health checks, resilience, service discovery)
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add FluentUI Blazor components
builder.Services.AddFluentUIComponents();

// Register HttpClients for backend APIs using Aspire service discovery
builder.Services.AddHttpClient("SchedulingApi", client =>
{
    // Aspire resolves "https+http://scheduling-webapi" to the actual URL
    client.BaseAddress = new Uri("https+http://scheduling-webapi");
});

builder.Services.AddHttpClient("BillingApi", client =>
{
    client.BaseAddress = new Uri("https+http://billing-webapi");
});

// Register API service wrappers
builder.Services.AddScoped<PatientApiService>();
// builder.Services.AddScoped<AppointmentApiService>();  // Add when created

var app = builder.Build();

// Map Aspire default endpoints (health checks)
app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

### Configuration Breakdown

| Section | Purpose |
|---------|---------|
| `builder.AddServiceDefaults()` | Adds Aspire observability, health checks, service discovery |
| `AddRazorComponents()` | Registers Blazor component services |
| `AddInteractiveServerComponents()` | Enables Blazor Server interactive rendering |
| `AddFluentUIComponents()` | Registers FluentUI Blazor component services |
| `AddHttpClient("SchedulingApi")` | Configures HttpClient with service discovery |
| `app.MapDefaultEndpoints()` | Exposes `/health` and `/alive` endpoints |
| `MapRazorComponents<App>()` | Maps the Blazor app routing |

---

## 8. FluentUI vs Other Component Libraries

Choosing a component library is an important architectural decision. Here's a comparison:

| Library | Pros | Cons | Best For |
|---------|------|------|----------|
| **FluentUI Blazor** | Microsoft-backed, Fluent Design System, good data grid, enterprise-ready | Smaller community than MudBlazor, fewer third-party resources | Enterprise apps, Microsoft ecosystem |
| **MudBlazor** | Large community, many components, excellent documentation, very customizable | Not officially Microsoft-backed, larger bundle size | Feature-rich apps, startups |
| **Radzen** | Free + paid tiers, many components, good data grid, Blazor Studio designer | Complex API, can be heavy, paid features needed for advanced scenarios | Rapid prototyping, LOB apps |
| **Telerik UI for Blazor** | Enterprise-grade, comprehensive components, excellent support | Commercial license required, expensive | Large enterprises with budget |
| **Syncfusion Blazor** | Many components, good performance | Commercial license required, steep learning curve | Data-intensive applications |

### Why We Use FluentUI Blazor

We chose FluentUI Blazor for this project because:

1. **Microsoft-Backed** - Official support and alignment with .NET ecosystem
2. **Fluent Design** - Modern, consistent Microsoft design language
3. **Enterprise-Ready** - Designed for business applications
4. **Good Integration** - Works well with .NET Aspire and ASP.NET Core
5. **Free & Open Source** - MIT license, no commercial restrictions
6. **Learning Value** - Understanding Microsoft's patterns and practices

For production projects, evaluate based on:
- Budget (free vs commercial)
- Community size and support
- Component completeness
- Design system fit
- Performance requirements

---

## 9. Verification Checklist

After completing this setup, verify each step:

- [ ] Blazor Server project created and added to solution
- [ ] FluentUI Blazor package installed and configured
- [ ] FluentUI namespaces added to `_Imports.razor`
- [ ] FluentUI assets referenced in `App.razor`
- [ ] Project registered in Aspire AppHost with API references
- [ ] ServiceDefaults reference added to Blazor project
- [ ] `AddServiceDefaults()` called in Program.cs
- [ ] HttpClients configured with Aspire service discovery
- [ ] `MapDefaultEndpoints()` called for health checks
- [ ] `dotnet run --project Aspire.AppHost` starts all services including Blazor app
- [ ] Blazor app accessible via Aspire Dashboard
- [ ] Blazor app shows in browser (check default home page loads)
- [ ] Health endpoint works: `GET https://localhost:xxxx/health` returns HTTP 200

### Testing Service Discovery

Create a test page to verify API connectivity:

**Components/Pages/TestApi.razor**:
```razor
@page "/test-api"
@inject IHttpClientFactory HttpClientFactory

<h3>Test API Connection</h3>

@if (result == null)
{
    <p>Click button to test API...</p>
}
else
{
    <p>Result: @result</p>
}

<button @onclick="TestConnection">Test Scheduling API</button>

@code {
    private string? result;

    private async Task TestConnection()
    {
        try
        {
            var client = HttpClientFactory.CreateClient("SchedulingApi");
            var response = await client.GetAsync("/health");
            result = response.IsSuccessStatusCode
                ? "✓ Connected successfully"
                : $"✗ Failed with status {response.StatusCode}";
        }
        catch (Exception ex)
        {
            result = $"✗ Error: {ex.Message}";
        }
    }
}
```

Navigate to `/test-api` and click the button. You should see "✓ Connected successfully".

---

## Summary

You've successfully set up a Blazor Server project integrated with your DDD backend:

1. **Created Blazor Server Project** - Using .NET 9 Blazor template with interactive server rendering
2. **Added FluentUI Blazor** - Microsoft's Fluent Design System for enterprise UI
3. **Registered with Aspire** - Orchestrated alongside backend APIs
4. **Configured Service Discovery** - HttpClients automatically resolve API endpoints
5. **Applied ServiceDefaults** - Observability, health checks, and resilience built-in

### Current Architecture

```
Aspire.AppHost (Orchestrator)
    |
    +-- RabbitMQ (messaging)
    +-- Scheduling.WebApi (scheduling-webapi)
    +-- Billing.WebApi (billing-webapi)
    +-- Scheduling.BlazorApp (scheduling-blazorapp)
            |
            +-- uses ServiceDefaults
            +-- calls --> Scheduling.WebApi (via service discovery)
            +-- calls --> Billing.WebApi (via service discovery)
```

### What's Next

In the next document, we'll build Blazor components and routing for the patient management UI, including:
- Creating routable pages
- Building forms with FluentUI components
- Calling backend APIs from components
- Handling loading states and errors
- Navigation between pages

---

**Previous**: [../00-frontend-overview.md](../00-frontend-overview.md)
**Next**: [02-blazor-components-and-routing.md](./02-blazor-components-and-routing.md)
