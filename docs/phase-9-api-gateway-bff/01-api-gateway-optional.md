# API Gateway with YARP (OPTIONAL)

> **NOTE:** This document covers an OPTIONAL enhancement. API Gateways are not required for all distributed systems. Read the "When to Use / When to Skip" section before implementing.

## Overview

An API Gateway provides a single entry point for client applications to access multiple backend services. This document covers:

- Why use an API Gateway
- When to use it vs when to skip it
- YARP (Yet Another Reverse Proxy) - Microsoft's solution
- Setting up a Gateway project with Aspire
- Route configuration for multiple bounded contexts
- Cross-cutting concerns (rate limiting, authentication, logging)
- Alternatives to YARP

---

## Why API Gateway?

```
WITHOUT API Gateway:
+----------------+
|    Client      |
|   (Browser)    |
+----------------+
   |    |    |
   |    |    +----------------------------------+
   |    +------------------+                    |
   |                       |                    |
   v                       v                    v
+-----------+      +-------------+      +-----------+
| Scheduling|      |   Billing   |      |  Medical  |
|  WebApi   |      |   WebApi    |      |  Records  |
| :5001     |      |   :5002     |      |  :5003    |
+-----------+      +-------------+      +-----------+

Problems:
- Client must know multiple URLs
- CORS configuration on each service
- Authentication logic duplicated
- Rate limiting duplicated
- No unified logging
- Difficult to change service URLs


WITH API Gateway:
+----------------+
|    Client      |
|   (Browser)    |
+----------------+
        |
        v
+------------------+
|   API Gateway    |  <-- Single entry point
|     (YARP)       |      Cross-cutting concerns handled here
|     :5000        |
+------------------+
   |       |       |
   v       v       v
+-----+ +-----+ +-----+
|Sched| |Bill | |Med  |
|:5001| |:5002| |:5003|
+-----+ +-----+ +-----+
```

### Benefits of an API Gateway

| Benefit | Description |
|---------|-------------|
| **Single entry point** | Clients interact with one URL |
| **Cross-cutting concerns** | Authentication, rate limiting, logging in one place |
| **Client simplification** | Hide internal service topology from clients |
| **Protocol translation** | Transform requests between protocols if needed |
| **Load balancing** | Distribute traffic across service instances |
| **Request aggregation** | Combine multiple service calls into one response |
| **Security** | Shield internal services from direct internet access |

---

## When to Use / When to Skip (OPTIONAL)

### Skip the API Gateway When:

- **Internal tools only** - If your APIs are only used by internal applications you control
- **Single bounded context** - One API serving all needs
- **Development/learning phase** - Adds complexity; focus on core patterns first
- **Small team** - Gateway adds operational overhead
- **Direct service-to-service communication** - Services calling each other directly do not need a gateway

### Use an API Gateway When:

- **External clients** - Public APIs consumed by third parties
- **Mobile applications** - Multiple backends need unified access
- **Microservices in production** - Multiple services needing consistent security
- **Multi-tenant SaaS** - Rate limiting and tenant isolation required
- **Different auth requirements** - Some endpoints public, some require auth
- **API versioning** - Managing multiple API versions

### For This Learning Project

**Recommendation:** Skip the gateway initially. Focus on DDD patterns, bounded contexts, and event-driven architecture first. Add a gateway later if you need:

- A public-facing API
- Mobile client support
- Advanced security requirements

---

## YARP (Yet Another Reverse Proxy)

YARP is Microsoft's open-source reverse proxy library. It integrates seamlessly with ASP.NET Core and .NET Aspire.

### Why YARP?

| Feature | YARP | Traditional Proxies (Nginx, HAProxy) |
|---------|------|-------------------------------------|
| **Language** | C# / .NET | Config files |
| **.NET integration** | Native | External |
| **Configuration** | Code or JSON | Config files |
| **Middleware pipeline** | Full ASP.NET Core | Limited |
| **Custom transforms** | C# code | Scripting/modules |
| **Aspire support** | Built-in | Manual setup |
| **Learning curve** | Low for .NET devs | Medium |

### YARP Architecture

```
+-------------------------------------------------------------------+
|                        API Gateway (YARP)                          |
+-------------------------------------------------------------------+
|                                                                    |
|  +--------------------+    +--------------------+                  |
|  |    ASP.NET Core    |    |   YARP Middleware  |                  |
|  |    Middleware      |    |                    |                  |
|  +--------------------+    +--------------------+                  |
|         |                           |                              |
|  +------+------+             +------+------+                       |
|  | Auth        |             | Route       |                       |
|  | Rate Limit  |             | Matching    |                       |
|  | Logging     |             | Load Balance|                       |
|  | CORS        |             | Transform   |                       |
|  +-------------+             +-------------+                       |
|                                                                    |
+-------------------------------------------------------------------+
         |                    |                    |
         v                    v                    v
   +-----------+       +-------------+      +-----------+
   | Scheduling|       |   Billing   |      |  Medical  |
   |  WebApi   |       |   WebApi    |      |  Records  |
   +-----------+       +-------------+      +-----------+
```

---

## Setup with Aspire (OPTIONAL)

### Step 1: Create the Gateway Project

```bash
dotnet new web -n Gateway -o Gateway
cd Gateway
dotnet add package Yarp.ReverseProxy
```

### Step 2: Gateway Project Structure

```
Gateway/
+-- Gateway.csproj
+-- Program.cs
+-- appsettings.json
```

### Step 3: Gateway.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Yarp.ReverseProxy" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ServiceDefaults\ServiceDefaults.csproj" />
  </ItemGroup>

</Project>
```

### Step 4: Gateway Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (telemetry, health checks)
builder.AddServiceDefaults();

// Add YARP reverse proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Map Aspire endpoints
app.MapDefaultEndpoints();

// Map YARP reverse proxy
app.MapReverseProxy();

app.Run();
```

### Step 5: Register Gateway in AppHost

```csharp
// AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
// NOTE: In this project, only RabbitMQ is managed by Aspire.
// SQL Server runs locally and uses connection strings from user secrets.
var messagingPassword = builder.AddParameter("messaging-password");
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin();

// Backend services
// Each service reads its SQL Server connection string from user secrets (DefaultConnection)
var schedulingApi = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-webapi")
    .WithReference(messaging);

var billingApi = builder.AddProject<Projects.Billing_WebApi>("billing-webapi")
    .WithReference(messaging);

// API Gateway (OPTIONAL)
var gateway = builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(schedulingApi)
    .WithReference(billingApi)
    .WithExternalHttpEndpoints();  // Expose to external clients

builder.Build().Run();
```

### Step 6: Gateway appsettings.json

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
      "scheduling-route": {
        "ClusterId": "scheduling-cluster",
        "Match": {
          "Path": "/api/scheduling/{**catch-all}"
        },
        "Transforms": [
          { "PathRemovePrefix": "/api/scheduling" }
        ]
      },
      "billing-route": {
        "ClusterId": "billing-cluster",
        "Match": {
          "Path": "/api/billing/{**catch-all}"
        },
        "Transforms": [
          { "PathRemovePrefix": "/api/billing" }
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
      },
      "billing-cluster": {
        "Destinations": {
          "billing-webapi": {
            "Address": "https+http://billing-webapi"
          }
        }
      }
    }
  }
}
```

**Note:** The `https+http://` scheme uses Aspire service discovery. YARP automatically resolves `scheduling-webapi` and `billing-webapi` to their actual URLs.

---

## Route Configuration

### URL Mapping

| Client Request | Gateway Route | Backend Service |
|---------------|---------------|-----------------|
| `GET /api/scheduling/patients` | `/api/scheduling/*` | `Scheduling.WebApi/patients` |
| `POST /api/scheduling/appointments` | `/api/scheduling/*` | `Scheduling.WebApi/appointments` |
| `GET /api/billing/invoices` | `/api/billing/*` | `Billing.WebApi/invoices` |
| `POST /api/billing/payments` | `/api/billing/*` | `Billing.WebApi/payments` |

### Request Flow

```
Client Request:
GET https://gateway:5000/api/scheduling/patients/abc-123

         |
         v

+------------------+
|     Gateway      |
|------------------|
| 1. Match route   |  -> /api/scheduling/* matches scheduling-route
| 2. Transform     |  -> Remove /api/scheduling prefix
| 3. Forward       |  -> GET https://scheduling-webapi/patients/abc-123
+------------------+

         |
         v

+------------------+
| Scheduling.WebApi|
|------------------|
| GET /patients/   |
|     abc-123      |
+------------------+

         |
         v

Response flows back through Gateway to Client
```

### Advanced Route Configuration

```json
{
  "ReverseProxy": {
    "Routes": {
      "scheduling-patients-route": {
        "ClusterId": "scheduling-cluster",
        "Match": {
          "Path": "/api/scheduling/patients/{**catch-all}",
          "Methods": ["GET", "POST", "PUT", "DELETE"]
        },
        "Transforms": [
          { "PathRemovePrefix": "/api/scheduling" },
          { "RequestHeader": "X-Forwarded-Prefix", "Set": "/api/scheduling" }
        ],
        "Metadata": {
          "RateLimitPolicy": "PatientApi"
        }
      },
      "scheduling-appointments-readonly": {
        "ClusterId": "scheduling-cluster",
        "Match": {
          "Path": "/api/scheduling/appointments/{**catch-all}",
          "Methods": ["GET"]
        },
        "Transforms": [
          { "PathRemovePrefix": "/api/scheduling" }
        ],
        "Metadata": {
          "AuthorizationPolicy": "ReadOnly"
        }
      }
    }
  }
}
```

---

## Cross-Cutting Concerns (OPTIONAL)

### Rate Limiting

```csharp
// Gateway/Program.cs
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add rate limiting
builder.Services.AddRateLimiter(options =>
{
    // Global rate limit
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 10
            }));

    // Named policy for specific routes
    options.AddFixedWindowLimiter("PatientApi", opt =>
    {
        opt.PermitLimit = 50;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 5;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseRateLimiter();
app.MapDefaultEndpoints();
app.MapReverseProxy();

app.Run();
```

### Authentication at Gateway Level

The gateway centralizes authentication — all requests are validated here before routing to backend APIs. Authorization policies can be applied per route in the YARP configuration (e.g., read-only vs full access).

> **Implementation:** See [Phase 8: Authentication & Authorization](../phase-8-auth/) for the full auth setup including gateway authentication, authorization policies, and YARP route-level policy configuration.

### Request/Response Logging

```csharp
// Gateway/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Add request logging middleware
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

    logger.LogInformation(
        "Gateway received {Method} {Path} from {RemoteIp}",
        context.Request.Method,
        context.Request.Path,
        context.Connection.RemoteIpAddress);

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    await next();

    stopwatch.Stop();

    logger.LogInformation(
        "Gateway responded {StatusCode} in {ElapsedMs}ms",
        context.Response.StatusCode,
        stopwatch.ElapsedMilliseconds);
});

app.MapDefaultEndpoints();
app.MapReverseProxy();

app.Run();
```

### CORS Configuration

```csharp
// Gateway/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure CORS at gateway level (not on individual services)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebApp", policy =>
    {
        policy.WithOrigins("https://webapp.example.com")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });

    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseCors("AllowWebApp");  // Or apply per-route

app.MapDefaultEndpoints();
app.MapReverseProxy();

app.Run();
```

---

## Complete Gateway Example

```csharp
// Gateway/Program.cs
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults
builder.AddServiceDefaults();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// YARP
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Request logging
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    logger.LogInformation("-> {Method} {Path}", context.Request.Method, context.Request.Path);

    await next();

    logger.LogInformation("<- {StatusCode} ({ElapsedMs}ms)",
        context.Response.StatusCode,
        stopwatch.ElapsedMilliseconds);
});

app.UseRateLimiter();
app.UseCors();

app.MapDefaultEndpoints();
app.MapReverseProxy();

app.Run();
```

---

## Alternatives to YARP (Brief Overview)

If YARP does not fit your needs, consider these alternatives:

| Alternative | Best For | Considerations |
|-------------|----------|----------------|
| **Azure API Management** | Production Azure deployments | Managed service, rich features, cost |
| **Kong** | Multi-platform, plugin ecosystem | Separate infrastructure, Lua plugins |
| **Nginx** | High performance, proven | External to .NET, config-file based |
| **Ocelot** | .NET ecosystem (older) | Less active development than YARP |
| **AWS API Gateway** | AWS deployments | AWS-specific, managed service |
| **Envoy** | Service mesh, Kubernetes | Complex setup, powerful features |

### When to Consider Alternatives

- **Azure API Management**: You need developer portal, API subscriptions, advanced analytics
- **Kong/Nginx**: You have a polyglot architecture (not just .NET)
- **Envoy**: You are using Kubernetes with service mesh requirements

For most .NET Aspire projects, **YARP is the recommended choice** due to its native integration.

---

## Architecture with Gateway

```
+------------------------------------------------------------------+
|                           Aspire AppHost                          |
+------------------------------------------------------------------+
|                                                                   |
|  External Traffic                                                 |
|        |                                                          |
|        v                                                          |
|  +------------+                                                   |
|  |  Gateway   |  <-- Public endpoint                              |
|  |   (YARP)   |      Rate limiting, auth, CORS, logging           |
|  +------------+                                                   |
|     |      |                                                      |
|     v      v                                                      |
|  +------+ +------+                                                |
|  |Sched | |Bill  |  <-- Internal services (not exposed publicly)  |
|  |API   | |API   |                                                |
|  +------+ +------+                                                |
|     |      |                                                      |
|     v      v                                                      |
|  +------------+                                                   |
|  |  RabbitMQ  |  <-- Internal messaging                           |
|  +------------+                                                   |
|     |      |                                                      |
|     v      v                                                      |
|  +------+ +------+                                                |
|  |Sched | |Bill  |                                                |
|  | DB   | | DB   |                                                |
|  +------+ +------+                                                |
|                                                                   |
+------------------------------------------------------------------+
```

---

## Verification Checklist (If Implementing)

- [ ] Gateway project created with YARP package
- [ ] ServiceDefaults referenced in Gateway
- [ ] Gateway registered in AppHost with `WithExternalHttpEndpoints()`
- [ ] Routes configured for each backend service
- [ ] Service discovery working (`https+http://service-name`)
- [ ] Rate limiting configured (optional)
- [ ] CORS configured at gateway (remove from backend services)
- [ ] Request logging enabled
- [ ] Health checks accessible via gateway
- [ ] Backend services not directly exposed to external traffic

---

## Summary

| Aspect | Recommendation |
|--------|----------------|
| **For learning** | Skip gateway; focus on DDD and bounded contexts |
| **For internal tools** | Skip gateway; direct service access is fine |
| **For external APIs** | Use gateway with YARP |
| **For production SaaS** | Use gateway with full cross-cutting concerns |
| **Technology choice** | YARP for .NET; Azure API Management for Azure production |

Remember: **An API Gateway is an OPTIONAL architectural pattern.** It adds value when you have external clients or need centralized cross-cutting concerns. For internal systems and learning projects, direct service access is simpler and sufficient.

---

Return to [01-aspire-introduction.md](../phase-6-integration/01-aspire-introduction.md) for an overview of .NET Aspire integration.
