# Backend for Frontend (BFF) Pattern (OPTIONAL)

> **NOTE:** This document covers an OPTIONAL enhancement. BFFs are not required for all distributed systems. Read the "When to Use / When to Skip" section before implementing.

## Overview

Backend for Frontend (BFF) is a pattern where you create dedicated backend services tailored to specific frontend applications. This document covers:

- What BFF is and how it differs from API Gateway
- When to use BFF vs API Gateway vs both
- Architecture with multiple BFFs
- Implementation with YARP and custom endpoints
- Authentication strategies per BFF
- Integration with .NET Aspire
- Production deployment considerations

---

## BFF vs API Gateway

### API Gateway
```
+----------------+
|  Client A      |
|  (Blazor)      |
+----------------+
        |
        v
+----------------+
| API Gateway    |  <-- Single entry point for ALL clients
|                |      Generic routing, same data for everyone
+----------------+
   |       |
   v       v
+-----+ +-----+
|Sched| |Bill |
+-----+ +-----+
```

**API Gateway characteristics:**
- Single entry point for all clients
- Generic routing (simple proxy)
- Same data shape for all consumers
- Cross-cutting concerns (auth, rate limiting, CORS)
- Protocol translation
- No knowledge of frontend needs

### Backend for Frontend (BFF)
```
+----------+              +----------+
|  Blazor  |              | Angular  |
|  Client  |              |  Client  |
+----------+              +----------+
     |                         |
     v                         v
+------------+          +------------+
| Blazor BFF |          | Angular BFF|  <-- Dedicated backend per frontend
|            |          |            |      Tailored to each client's needs
+------------+          +------------+
     |                         |
     +------------+------------+
                  |
                  v
          +-------+-------+
          |       |       |
          v       v       v
       +----+ +----+ +----+
       |Sched| |Bill | |Med |
       +----+ +----+ +----+
```

**BFF characteristics:**
- Dedicated backend per frontend type
- Tailored responses for specific UI needs
- Aggregates data from multiple backend APIs
- Frontend-specific authentication (cookies vs JWT)
- UI-specific caching strategies
- Optimized for frontend consumption

### Key Differences

| Aspect | API Gateway | BFF |
|--------|-------------|-----|
| **Scope** | All clients | Single frontend type |
| **Data shaping** | Generic | Frontend-specific |
| **Aggregation** | Minimal | Common |
| **Authentication** | Unified | Per-frontend (cookies, JWT) |
| **Owned by** | Platform team | Frontend team |
| **Changes with** | Backend API changes | Frontend UI changes |
| **Number of instances** | 1 | 1 per frontend type |

---

## When to Use Each Pattern

### Use Only API Gateway When:

- **Single frontend** - One web app consuming your APIs
- **Thin clients** - Frontends can handle response shaping
- **Generic responses** - All clients need the same data
- **Third-party consumers** - External parties need stable API contracts
- **Learning phase** - Focus on backend patterns first

### Use Only BFF When:

- **Multiple frontends** - Different UIs (web, mobile, desktop)
- **Complex aggregation** - UIs need data from multiple services
- **Different auth** - Cookie-based for web, JWT for mobile
- **Frontend-specific caching** - Different cache strategies per client
- **No external consumers** - All APIs are internal

### Use Both BFF + Gateway When:

- **Multiple frontends + public API** - Internal BFFs for your apps, Gateway for third parties
- **Different SLAs** - BFFs optimized for your UIs, Gateway rate-limited for external use
- **Advanced scenario** - This is the most complex option

```
External Clients          Internal Clients
      |                          |
      v                          v
+------------+          +----------------------+
|  Public    |          |  Blazor  |  Angular  |
|  Gateway   |          |   BFF    |    BFF    |
+------------+          +----------+----------+
      |                          |
      +------------+-------------+
                   |
                   v
           +-------+-------+
           |       |       |
           v       v       v
        +----+ +----+ +----+
        |Sched| |Bill | |Med |
        +----+ +----+ +----+
```

---

## When to Use / When to Skip (OPTIONAL)

### Skip BFF When:

- **Single frontend** - One Blazor app or one Angular app
- **Simple data needs** - Frontend can work with raw API responses
- **Development/learning phase** - Adds complexity; focus on core patterns first
- **Direct API consumption works** - Backend APIs already return frontend-friendly data
- **Small team** - Maintaining multiple BFFs adds overhead

### Use BFF When:

- **Multiple frontend types** - Blazor web app + Angular SPA + mobile app
- **Different auth strategies** - Cookies for server-rendered, JWT for SPAs
- **Heavy aggregation needs** - UI needs data from 5+ backend services in single view
- **Frontend-specific optimizations** - Different caching, different data shapes
- **Reduce frontend complexity** - Move aggregation logic to backend
- **Team ownership** - Frontend team owns their BFF, backend team owns domain APIs

### For This Learning Project

**Recommendation:** Skip BFF initially unless you are building both Blazor and Angular frontends (Phase 7). If building only Blazor, consume the backend APIs directly or add a simple API Gateway.

Add BFF when:
- You add a second frontend type (Angular)
- You need different authentication strategies
- Your Blazor components are making 10+ API calls to render a single page

---

## Architecture with Multiple BFFs

### Scenario: Blazor + Angular Frontends

This learning project (Phase 7) will add:
1. **Blazor Server** (primary) - Server-side rendering, cookie auth
2. **Angular SPA** (optional) - Client-side rendering, JWT auth

Each frontend gets its own BFF.

```
+------------------------------------------------------------------+
|                         Production (Azure)                        |
+------------------------------------------------------------------+
|                                                                   |
|  +------------------------+                                       |
|  |  Azure Front Door      |  <-- Single entry point (CDN + WAF)   |
|  +------------------------+                                       |
|     |           |          |                                      |
|     v           v          v                                      |
|  +-------+  +--------+  +---------+                               |
|  |Blazor |  |Angular |  | Public  |                               |
|  | BFF   |  |  BFF   |  | Gateway |                               |
|  +-------+  +--------+  +---------+                               |
|     |           |            |                                    |
|     +-----------+------------+                                    |
|                 |                                                 |
|                 v                                                 |
|         +-------+-------+                                         |
|         |  Private VNet |                                         |
|         +-------+-------+                                         |
|            |       |                                              |
|            v       v                                              |
|         +-----+ +-----+                                           |
|         |Sched| |Bill |  <-- Internal APIs (not publicly exposed)|
|         | API | | API |                                           |
|         +-----+ +-----+                                           |
|            |       |                                              |
|            v       v                                              |
|         +-----------+                                             |
|         | RabbitMQ  |  <-- Integration events (bypasses BFFs)    |
|         +-----------+                                             |
|            |       |                                              |
|            v       v                                              |
|         +-----+ +-----+                                           |
|         |Sched| |Bill |                                           |
|         | DB  | | DB  |                                           |
|         +-----+ +-----+                                           |
|                                                                   |
+------------------------------------------------------------------+
```

### Local Development (Aspire)

```
+------------------------------------------------------------------+
|                        .NET Aspire AppHost                        |
+------------------------------------------------------------------+
|                                                                   |
|  Browser (Blazor)           Browser (Angular)                     |
|     |                             |                               |
|     v                             v                               |
|  http://localhost:5100       http://localhost:5101               |
|     |                             |                               |
|  +-------+                    +--------+                          |
|  |Blazor |                    |Angular |                          |
|  | BFF   |                    |  BFF   |                          |
|  +-------+                    +--------+                          |
|     |                             |                               |
|     +-------------+---------------+                               |
|                   |                                               |
|                   v                                               |
|       Service Discovery (Aspire)                                  |
|                   |                                               |
|         +---------+---------+                                     |
|         |                   |                                     |
|         v                   v                                     |
|      +-----+             +-----+                                  |
|      |Sched|             |Bill |                                  |
|      | API |             | API |                                  |
|      +-----+             +-----+                                  |
|         |                   |                                     |
|         v                   v                                     |
|      +-----------------------+                                    |
|      |      RabbitMQ         |                                    |
|      +-----------------------+                                    |
|                                                                   |
+------------------------------------------------------------------+
```

**Key differences local vs production:**
- **Local**: Direct access to BFFs via Aspire ports
- **Production**: Azure Front Door routes to BFFs, BFFs in App Services, backend APIs in private VNet

---

## What a BFF Does vs What It Doesn't

### BFF Responsibilities (DOES)

| Responsibility | Example |
|----------------|---------|
| **Aggregate data** | Combine patient + appointments + invoices into single response for dashboard |
| **Shape responses** | Transform backend DTOs into UI-specific view models |
| **Frontend-specific auth** | Cookie-based for Blazor, JWT for Angular |
| **UI-specific caching** | Cache dashboard data for 5 minutes, patient list for 1 hour |
| **Rate limit per client** | Blazor BFF allows 1000 req/min, Angular BFF allows 500 req/min |
| **Protocol translation** | GraphQL frontend → REST backend |
| **Error mapping** | Convert backend error codes to user-friendly messages |
| **Act as reverse proxy** | Pass-through simple requests to backend APIs |

### BFF Does NOT Do (DOESN'T)

| Anti-pattern | Why Not | Where It Belongs |
|--------------|---------|------------------|
| **Business logic** | Duplicates domain rules | Domain layer in backend APIs |
| **Data validation** | Commands already validated | FluentValidation in Application layer |
| **Database access** | BFF should be stateless | Backend APIs via EF Core |
| **Event publishing** | Domain events belong in aggregates | Domain layer → MassTransit |
| **Authorization logic** | BFF checks roles, not business rules | Domain layer (aggregate invariants) |

### Example: What Goes Where

**Scenario:** "A patient cannot have more than 5 active appointments"

```csharp
// ❌ WRONG: Business logic in BFF
app.MapGet("/dashboard", async (string patientId, ISchedulingApi schedulingApi) =>
{
    var appointments = await schedulingApi.GetAppointments(patientId);

    if (appointments.Count >= 5)
    {
        return Results.BadRequest("Too many appointments");
    }

    // ... more code
});

// ✅ RIGHT: Business logic in domain
public class Patient : AggregateRoot
{
    private readonly List<Appointment> _appointments = [];

    public Result ScheduleAppointment(Appointment appointment)
    {
        if (_appointments.Count >= 5)
        {
            return Result.Failure("Patient cannot have more than 5 active appointments");
        }

        _appointments.Add(appointment);
        return Result.Success();
    }
}

// ✅ BFF just aggregates data
app.MapGet("/dashboard", async (string patientId, ISchedulingApi schedulingApi, IBillingApi billingApi) =>
{
    var patient = await schedulingApi.GetPatient(patientId);
    var appointments = await schedulingApi.GetAppointments(patientId);
    var invoices = await billingApi.GetInvoices(patientId);

    return new DashboardViewModel
    {
        Patient = patient,
        Appointments = appointments,
        Invoices = invoices
    };
});
```

---

## Implementation with YARP + Custom Endpoints

A BFF can act as:
1. **Reverse proxy** for simple pass-through routes (YARP)
2. **Aggregation layer** for composite endpoints (custom Minimal API endpoints)

### BFF Project Structure

```
BlazorBff/
├── BlazorBff.csproj
├── Program.cs
├── Endpoints/
│   ├── DashboardEndpoints.cs      <-- Aggregation endpoint
│   ├── PatientEndpoints.cs        <-- Pass-through + aggregation
│   └── ViewModels/
│       ├── DashboardViewModel.cs
│       └── PatientDetailsViewModel.cs
├── Clients/
│   ├── ISchedulingApiClient.cs
│   ├── IBillingApiClient.cs
│   └── HttpClientExtensions.cs
└── appsettings.json
```

### Step 1: Create BFF Project

```bash
dotnet new web -n BlazorBff -o BlazorBff
cd BlazorBff
dotnet add package Yarp.ReverseProxy
dotnet add package Refit  # For typed HTTP clients
```

### Step 2: BlazorBff.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Yarp.ReverseProxy" />
    <PackageReference Include="Refit.HttpClientFactory" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ServiceDefaults\ServiceDefaults.csproj" />
  </ItemGroup>

</Project>
```

### Step 3: Define Backend API Clients (Refit)

```csharp
// Clients/ISchedulingApiClient.cs
using Refit;

namespace BlazorBff.Clients;

public interface ISchedulingApiClient
{
    [Get("/patients/{id}")]
    Task<PatientDto> GetPatient(Guid id);

    [Get("/patients/{patientId}/appointments")]
    Task<List<AppointmentDto>> GetAppointments(Guid patientId);
}

public record PatientDto(Guid Id, string Name, string Email, string Status);
public record AppointmentDto(Guid Id, DateTime ScheduledTime, string DoctorName, string Status);
```

```csharp
// Clients/IBillingApiClient.cs
using Refit;

namespace BlazorBff.Clients;

public interface IBillingApiClient
{
    [Get("/billing-profiles/{patientId}")]
    Task<BillingProfileDto> GetBillingProfile(Guid patientId);

    [Get("/billing-profiles/{patientId}/invoices")]
    Task<List<InvoiceDto>> GetInvoices(Guid patientId);
}

public record BillingProfileDto(Guid Id, Guid PatientId, string PaymentMethod);
public record InvoiceDto(Guid Id, decimal Amount, DateTime DueDate, string Status);
```

### Step 4: Aggregation Endpoint

```csharp
// Endpoints/DashboardEndpoints.cs
using BlazorBff.Clients;
using Microsoft.AspNetCore.Mvc;

namespace BlazorBff.Endpoints;

public static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/dashboard");

        group.MapGet("/{patientId:guid}", GetPatientDashboard);

        return group;
    }

    private static async Task<IResult> GetPatientDashboard(
        [FromRoute] Guid patientId,
        [FromServices] ISchedulingApiClient schedulingApi,
        [FromServices] IBillingApiClient billingApi,
        [FromServices] ILogger<DashboardEndpoints> logger)
    {
        logger.LogInformation("Fetching dashboard for patient {PatientId}", patientId);

        try
        {
            // Call multiple backend services in parallel
            var patientTask = schedulingApi.GetPatient(patientId);
            var appointmentsTask = schedulingApi.GetAppointments(patientId);
            var billingProfileTask = billingApi.GetBillingProfile(patientId);
            var invoicesTask = billingApi.GetInvoices(patientId);

            await Task.WhenAll(patientTask, appointmentsTask, billingProfileTask, invoicesTask);

            // Aggregate into UI-specific view model
            var dashboard = new DashboardViewModel
            {
                Patient = new PatientSummary
                {
                    Id = patientTask.Result.Id,
                    Name = patientTask.Result.Name,
                    Email = patientTask.Result.Email,
                    Status = patientTask.Result.Status
                },
                UpcomingAppointments = appointmentsTask.Result
                    .Where(a => a.ScheduledTime > DateTime.UtcNow)
                    .OrderBy(a => a.ScheduledTime)
                    .Take(5)
                    .Select(a => new AppointmentSummary
                    {
                        Id = a.Id,
                        ScheduledTime = a.ScheduledTime,
                        DoctorName = a.DoctorName
                    })
                    .ToList(),
                OutstandingInvoices = invoicesTask.Result
                    .Where(i => i.Status == "Unpaid")
                    .Select(i => new InvoiceSummary
                    {
                        Id = i.Id,
                        Amount = i.Amount,
                        DueDate = i.DueDate
                    })
                    .ToList(),
                TotalOutstanding = invoicesTask.Result
                    .Where(i => i.Status == "Unpaid")
                    .Sum(i => i.Amount)
            };

            return Results.Ok(dashboard);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch dashboard for patient {PatientId}", patientId);
            return Results.Problem("Failed to load dashboard");
        }
    }
}

// ViewModels/DashboardViewModel.cs
public class DashboardViewModel
{
    public required PatientSummary Patient { get; init; }
    public required List<AppointmentSummary> UpcomingAppointments { get; init; }
    public required List<InvoiceSummary> OutstandingInvoices { get; init; }
    public decimal TotalOutstanding { get; init; }
}

public class PatientSummary
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required string Status { get; init; }
}

public class AppointmentSummary
{
    public required Guid Id { get; init; }
    public required DateTime ScheduledTime { get; init; }
    public required string DoctorName { get; init; }
}

public class InvoiceSummary
{
    public required Guid Id { get; init; }
    public required decimal Amount { get; init; }
    public required DateTime DueDate { get; init; }
}
```

### Step 5: BFF Program.cs (YARP + Custom Endpoints)

```csharp
// BlazorBff/Program.cs
using BlazorBff.Clients;
using BlazorBff.Endpoints;
using Refit;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults
builder.AddServiceDefaults();

// Configure Refit HTTP clients with Aspire service discovery
builder.Services.AddRefitClient<ISchedulingApiClient>()
    .ConfigureHttpClient(client =>
    {
        client.BaseAddress = new Uri("https+http://scheduling-webapi");
    });

builder.Services.AddRefitClient<IBillingApiClient>()
    .ConfigureHttpClient(client =>
    {
        client.BaseAddress = new Uri("https+http://billing-webapi");
    });

// Add YARP for pass-through routes
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Add authentication (cookie-based for Blazor)
builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Map custom aggregation endpoints
app.MapDashboardEndpoints();

// Map Aspire health checks
app.MapDefaultEndpoints();

// Map YARP reverse proxy (pass-through routes)
app.MapReverseProxy();

app.Run();
```

### Step 6: BFF appsettings.json (YARP for Pass-Through)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Yarp": "Information"
    }
  },
  "ReverseProxy": {
    "Routes": {
      "patients-route": {
        "ClusterId": "scheduling-cluster",
        "Match": {
          "Path": "/api/patients/{**catch-all}"
        },
        "Transforms": [
          { "PathRemovePrefix": "/api" }
        ]
      },
      "appointments-route": {
        "ClusterId": "scheduling-cluster",
        "Match": {
          "Path": "/api/appointments/{**catch-all}"
        },
        "Transforms": [
          { "PathRemovePrefix": "/api" }
        ]
      }
    },
    "Clusters": {
      "scheduling-cluster": {
        "Destinations": {
          "scheduling-webapi": {
            "Address": "https+http://scheduling-webapi"
          }
        }
      }
    }
  }
}
```

**Result:**
- `GET /api/dashboard/{patientId}` → Custom aggregation endpoint
- `GET /api/patients/{id}` → Pass-through to Scheduling.WebApi
- `GET /api/appointments` → Pass-through to Scheduling.WebApi

---

## Authentication per BFF

### Blazor BFF (Cookie-Based)

```csharp
// BlazorBff/Program.cs
builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
```

**When calling backend APIs:**

```csharp
// BlazorBff uses managed identity or internal token when calling backend APIs
builder.Services.AddRefitClient<ISchedulingApiClient>()
    .ConfigureHttpClient(client =>
    {
        client.BaseAddress = new Uri("https+http://scheduling-webapi");
    })
    .AddHttpMessageHandler<ManagedIdentityHandler>();  // Add managed identity token
```

**Flow:**
1. User logs in to Blazor app → Cookie issued by BFF
2. Blazor sends cookie with each request to BFF
3. BFF validates cookie
4. BFF calls backend APIs with managed identity (internal network)

### Angular BFF (JWT-Based)

```csharp
// AngularBff/Program.cs
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];  // e.g., Azure AD
        options.Audience = builder.Configuration["Auth:Audience"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true
        };
    });
```

**When calling backend APIs:**

```csharp
// AngularBff uses managed identity when calling backend APIs
builder.Services.AddRefitClient<ISchedulingApiClient>()
    .ConfigureHttpClient(client =>
    {
        client.BaseAddress = new Uri("https+http://scheduling-webapi");
    })
    .AddHttpMessageHandler<ManagedIdentityHandler>();  // Add managed identity token
```

**Flow:**
1. User logs in via Angular app → JWT issued by identity provider (Azure AD)
2. Angular sends `Authorization: Bearer <token>` with each request to BFF
3. BFF validates JWT
4. BFF calls backend APIs with managed identity (internal network)

### Backend APIs (Internal Only)

```csharp
// Scheduling.WebApi/Program.cs
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        // Accept tokens from managed identity only (internal network)
        options.Authority = builder.Configuration["Auth:InternalAuthority"];
        options.Audience = "scheduling-api-internal";

        // Ensure token is from trusted source (BFFs or Gateway)
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "internal-bff",
            ValidateAudience = true,
            ValidAudience = "scheduling-api-internal"
        };
    });
```

**Security model:**
- Backend APIs are NOT publicly accessible (private VNet in Azure)
- Backend APIs only trust internal tokens (issued by BFFs or managed identity)
- BFFs handle external authentication (cookies, JWT)
- BFFs translate external identity → internal token when calling backend APIs

---

## How It Fits with Aspire Locally

### AppHost Configuration

```csharp
// AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var messagingPassword = builder.AddParameter("messaging-password");
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin();

// Backend services (internal)
var schedulingApi = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-webapi")
    .WithReference(messaging);

var billingApi = builder.AddProject<Projects.Billing_WebApi>("billing-webapi")
    .WithReference(messaging);

// BFFs (externally accessible)
var blazorBff = builder.AddProject<Projects.BlazorBff>("blazor-bff")
    .WithReference(schedulingApi)
    .WithReference(billingApi)
    .WithExternalHttpEndpoints();  // Expose to browser

var angularBff = builder.AddProject<Projects.AngularBff>("angular-bff")
    .WithReference(schedulingApi)
    .WithReference(billingApi)
    .WithExternalHttpEndpoints();  // Expose to browser

// Public API Gateway (optional - for third-party consumers)
var publicGateway = builder.AddProject<Projects.PublicGateway>("public-gateway")
    .WithReference(schedulingApi)
    .WithReference(billingApi)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

### Service Discovery

BFFs use Aspire service discovery to call backend APIs:

```csharp
// BlazorBff/Program.cs
builder.Services.AddRefitClient<ISchedulingApiClient>()
    .ConfigureHttpClient(client =>
    {
        // Aspire resolves "scheduling-webapi" to actual URL
        client.BaseAddress = new Uri("https+http://scheduling-webapi");
    });
```

**Aspire automatically:**
- Resolves `scheduling-webapi` to `http://localhost:5001` (or assigned port)
- Handles HTTPS + HTTP endpoints
- Updates service URLs if ports change

### Local Development Flow

```
Browser (http://localhost:5100)
        |
        v
    Blazor BFF
        |
        +---> https+http://scheduling-webapi (Aspire resolves to localhost:5001)
        |
        +---> https+http://billing-webapi (Aspire resolves to localhost:5002)
```

**No Azure Front Door needed locally** - just hit BFFs directly at their Aspire-assigned ports.

---

## Production Architecture (Azure)

### Infrastructure Components

```
Internet
   |
   v
+------------------------+
|  Azure Front Door      |  <-- Global load balancer + WAF + CDN
+------------------------+
   |           |          |
   v           v          v
+-------+  +--------+  +---------+
|Blazor |  |Angular |  | Public  |
| BFF   |  |  BFF   |  | Gateway |  <-- App Services (publicly accessible)
+-------+  +--------+  +---------+
   |           |            |
   +---+-------+------------+
       |
       v
+------------------+
| Private VNet     |
+------------------+
       |
       v
+------+------+
|             |
v             v
+----------+  +----------+
|Scheduling|  | Billing  |  <-- App Services (private VNet, not publicly accessible)
|  WebApi  |  |  WebApi  |
+----------+  +----------+
       |             |
       v             v
+-----------+  +-----------+
| SQL Server|  | SQL Server|
+-----------+  +-----------+
```

### Network Isolation

| Component | Accessibility | Authentication |
|-----------|---------------|----------------|
| **Azure Front Door** | Public internet | None |
| **BFFs** | Via Front Door | Cookie/JWT from client |
| **Public Gateway** | Via Front Door | API key or OAuth |
| **Backend APIs** | Private VNet only | Managed identity from BFFs |
| **RabbitMQ** | Private VNet only | Internal credentials |
| **SQL Server** | Private VNet only | Managed identity |

### Comparison: Local vs Production

| Aspect | Local (Aspire) | Production (Azure) |
|--------|----------------|---------------------|
| **Entry point** | Direct to BFF (http://localhost:5100) | Azure Front Door |
| **Service discovery** | Aspire | Azure DNS / App Service names |
| **Backend API access** | Localhost ports | Private VNet |
| **Authentication** | Development mode | Cookie/JWT + Managed Identity |
| **RabbitMQ** | Aspire container | Azure Service Bus or self-hosted |
| **SQL Server** | LocalDB / user secrets | Azure SQL (managed identity) |

---

## Integration Events and BFFs

**Important:** BFFs do NOT participate in integration events.

```
Scheduling.WebApi publishes event
        |
        v
    RabbitMQ
        |
        v
Billing.WebApi consumes event
```

**BFFs are NOT in this flow:**
- Integration events bypass BFFs entirely
- Events flow directly between backend APIs via RabbitMQ
- BFFs only aggregate data for UIs; they don't react to domain changes

**Why?**
- Integration events represent domain state changes
- Domain logic lives in backend APIs, not BFFs
- BFFs are UI-centric, not domain-centric

---

## When to Skip BFF

### Skip BFF and Use Direct API Access When:

- **Single frontend** - Only building Blazor, no Angular or mobile
- **Simple UI** - Pages map 1:1 to backend API endpoints
- **Learning phase** - Focus on DDD, CQRS, event-driven architecture first
- **Thin client** - Frontend can handle aggregation with client-side logic
- **Small team** - Maintaining BFF infrastructure is overhead

### Skip BFF and Use API Gateway When:

- **Third-party consumers** - Need stable, versioned API contracts
- **Generic responses** - All clients get the same data
- **Security focus** - Centralized authentication and rate limiting more important than data shaping

### Use BFF When:

- **Multiple frontend types** - Blazor + Angular + Mobile
- **Complex aggregation** - Dashboard needs data from 5+ services
- **Different auth strategies** - Cookie for web, JWT for mobile
- **Frontend team ownership** - Team owns both UI and BFF
- **Performance critical** - Server-side aggregation faster than client-side

---

## Verification Checklist (If Implementing)

- [ ] BFF project created with YARP and Refit packages
- [ ] ServiceDefaults referenced in BFF
- [ ] Refit clients defined for backend APIs (`ISchedulingApiClient`, `IBillingApiClient`)
- [ ] HTTP clients configured with Aspire service discovery (`https+http://service-name`)
- [ ] Custom aggregation endpoints implemented (e.g., `/api/dashboard/{patientId}`)
- [ ] View models designed for UI-specific needs
- [ ] Pass-through routes configured with YARP (simple proxy)
- [ ] Authentication configured per BFF (cookies for Blazor, JWT for Angular)
- [ ] BFF registered in AppHost with `WithExternalHttpEndpoints()`
- [ ] Backend API references added to BFF in AppHost
- [ ] Parallel API calls used in aggregation endpoints (`Task.WhenAll`)
- [ ] Error handling implemented (try/catch, Results.Problem)
- [ ] Logging added for aggregation endpoints
- [ ] Verified BFF does NOT contain business logic
- [ ] Integration events bypass BFFs (flow directly between backend APIs)

---

## Summary

| Aspect | Recommendation |
|--------|----------------|
| **For single frontend** | Skip BFF; consume backend APIs directly |
| **For Blazor only** | Skip BFF initially; add later if aggregation needs grow |
| **For Blazor + Angular** | Use BFF pattern (one BFF per frontend) |
| **For third-party APIs** | Use API Gateway, not BFF |
| **For complex dashboards** | Use BFF to aggregate data server-side |
| **Technology choice** | YARP for pass-through + Refit for aggregation |
| **Authentication** | Cookies for Blazor BFF, JWT for Angular BFF, Managed Identity for backend APIs |

### Key Takeaways

1. **BFF is frontend-specific** - One BFF per frontend type, owned by frontend team
2. **BFF aggregates, does not decide** - Business logic stays in domain layer
3. **BFF uses YARP + custom endpoints** - Proxy simple requests, aggregate complex ones
4. **Authentication differs per BFF** - Cookies for server-rendered, JWT for SPAs
5. **BFFs bypass integration events** - Events flow directly between backend APIs
6. **Optional pattern** - Only use if you have multiple frontends with different needs

Remember: **BFF is an OPTIONAL architectural pattern.** It adds value when you have multiple frontend types with different data needs. For single-frontend applications, direct API consumption or a simple API Gateway is simpler and sufficient.

---

Return to [06-api-gateway-optional.md](./06-api-gateway-optional.md) for API Gateway documentation, or [01-aspire-introduction.md](./01-aspire-introduction.md) for an overview of .NET Aspire integration.
