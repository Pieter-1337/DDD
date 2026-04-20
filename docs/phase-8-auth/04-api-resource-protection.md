# Phase 8.04 - API Resource Protection

> Previous: [03-shared-auth-infrastructure.md](./03-shared-auth-infrastructure.md)

This document covers protecting the Scheduling and Billing APIs with cookie-based authentication using the shared infrastructure built in previous documents.

---

## Overview

Now that we have:
- An Auth Server (Phase 8.02) that issues authentication cookies
- Shared authentication infrastructure (Phase 8.03) with Duende IdentityServer cookie validation and reusable auth endpoints

We need to **protect our existing API endpoints** by:
1. Adding authentication middleware to validate cookies
2. Adding authorization middleware to enforce `[Authorize]` attributes
3. Updating CORS to allow credentials (cookies) from Angular
4. Applying authorization policies to controllers and endpoints

This document shows the changes needed for both `Scheduling.WebApi` and `Billing.WebApi`.

---

## Why Cookie-Based Authentication?

For our architecture (Blazor/Angular SPAs calling .NET APIs), cookie-based authentication offers:

| Aspect | Cookie-Based | JWT Bearer |
|--------|--------------|------------|
| **Security** | HttpOnly cookies immune to XSS | JWT in localStorage vulnerable to XSS |
| **Simplicity** | Browser handles storage/sending | Manual header management |
| **CORS** | Requires `AllowCredentials()` | Works with simple CORS |
| **Best For** | Same-domain or trusted origins | Public APIs, mobile apps |

Since our Angular app and APIs are all first-party applications running on localhost (and later, same domain in production), cookies are the secure choice.

---

## Authentication vs Authorization Middleware

Understanding the middleware pipeline is critical:

```
HTTP Request arrives
    │
    ▼
UseHttpsRedirection()      ← Redirects HTTP to HTTPS
    │
    ▼
UseAuthentication()        ← Reads cookie, validates signature, populates HttpContext.User
    │                        (sets User.Identity.IsAuthenticated = true/false)
    ▼
UseAuthorization()         ← Checks [Authorize] attributes against HttpContext.User
    │                        (returns 401 if unauthenticated, 403 if unauthorized)
    ▼
UseCors("Angular")         ← Adds CORS headers to response
    │
    ▼
MapControllers()           ← Executes controller action method
    │
    ▼
Response returned
```

**Critical Order**: `UseAuthentication()` **must** come before `UseAuthorization()`. Otherwise, `User` will be null and all `[Authorize]` checks fail.

---

## NuGet Packages Required

The following packages need to be added to `Directory.Packages.props` (add them when implementing, not before):

```xml
<!-- File: Directory.Packages.props -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- Existing packages -->
    <PackageVersion Include="Ardalis.SmartEnum" Version="8.1.0" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.3" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.3" />
    <PackageVersion Include="FluentValidation" Version="11.11.0" />
    <PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="11.11.0" />
    <PackageVersion Include="MassTransit.RabbitMQ" Version="8.4.2" />
    <PackageVersion Include="MediatR" Version="12.5.0" />
    <PackageVersion Include="Polly" Version="8.5.0" />
    <PackageVersion Include="Aspire.Hosting.RabbitMQ" Version="13.1.1" />
    <PackageVersion Include="Aspire.Hosting.JavaScript" Version="13.1.1" />

    <!-- Authentication & Authorization (NEW) -->
    <PackageVersion Include="Duende.IdentityServer" Version="7.0.8" />
    <PackageVersion Include="Duende.IdentityServer.AspNetIdentity" Version="7.0.8" />
    <PackageVersion Include="Duende.IdentityServer.EntityFramework" Version="7.0.8" />
    <PackageVersion Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="9.0.3" />
    <PackageVersion Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="9.0.3" />
  </ItemGroup>
</Project>
```

**Note**: These packages will be referenced by `BuildingBlocks.Infrastructure.Auth` (created in doc 03) and indirectly by the WebApi projects.

---

## Scheduling.WebApi: Updated Program.cs

Here's the **complete** updated `Program.cs` for Scheduling.WebApi with authentication and authorization configured:

```csharp
// File: WebApplications/Scheduling.WebApi/Program.cs
using BuildingBlocks.Application;
using BuildingBlocks.Infrastructure.Auth;  // NEW: Shared auth infrastructure
using BuildingBlocks.Infrastructure.MassTransit.Configuration;
using BuildingBlocks.WebApplications.Filters;
using BuildingBlocks.WebApplications.Json;
using BuildingBlocks.WebApplications.OpenApi;
using MassTransit;
using Scheduling.Application;
using Scheduling.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Service Defaults (Aspire observability, health checks, resilience)
builder.AddServiceDefaults();

// Controllers with exception filter and SmartEnum JSON serialization
builder.Services.AddControllers(options =>
{
    options.SuppressAsyncSuffixInActionNames = false;
    options.Filters.Add<ExceptionToJsonFilter>();
}).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new SmartEnumJsonConverterFactory());
});

// OpenAPI (Scalar documentation)
builder.Services.AddOpenApi();

// SQL Server connection and health checks
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "sqlserver", tags: ["ready"]);

// Infrastructure and Application layers
builder.Services.AddSchedulingInfrastructure(connectionString);
builder.Services.AddSchedulingApplication();
builder.Services.AddDefaultPipelineBehaviors();

// MassTransit with RabbitMQ for event-driven communication
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});

// ==================== AUTHENTICATION & AUTHORIZATION (NEW) ====================

// Add OIDC cookie authentication (validates cookies issued by Auth Server)
builder.Services.AddOidcCookieAuth(builder.Configuration);

// Add authorization policies
builder.Services.AddAuthorization(options =>
{
    // AdminOnly: Only users with "Admin" role
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));

    // DoctorOrAdmin: Users with "Doctor" OR "Admin" role
    options.AddPolicy("DoctorOrAdmin", policy =>
        policy.RequireRole("Doctor", "Admin"));

    // NurseOrHigher: Users with "Nurse", "Doctor", OR "Admin" role
    options.AddPolicy("NurseOrHigher", policy =>
        policy.RequireRole("Nurse", "Doctor", "Admin"));
});

// ==================== CORS (UPDATED) ====================

builder.Services.AddCors(options =>
{
    options.AddPolicy("Angular", policy => policy
        .WithOrigins(
            "https://localhost:7003",  // Angular SPA
            "https://localhost:7010")  // Auth Server (for redirect flows if needed)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());  // NEW: Required for cookie authentication
});

// ==============================================================================

var app = builder.Build();

// Default endpoints (health checks, etc.)
app.MapDefaultEndpoints();

// OpenAPI in development only
if (app.Environment.IsDevelopment())
{
    app.UseOpenApiWithScalar("Scheduling API");
}

// ==================== MIDDLEWARE PIPELINE (CRITICAL ORDER) ====================

app.UseHttpsRedirection();

// Authentication MUST come before Authorization
app.UseAuthentication();  // NEW: Validates cookies, populates HttpContext.User
app.UseAuthorization();   // Enforces [Authorize] attributes

app.UseCors("Angular");

app.MapControllers();

// ==============================================================================

app.Run();
```

### What Changed?

1. **New using**: `BuildingBlocks.Infrastructure.Auth` for the shared auth extension method
2. **Authentication**: `AddOidcCookieAuth(builder.Configuration)` registers cookie validation
3. **Authorization policies**: Three policies for role-based access control
4. **CORS update**: Added `AllowCredentials()` and Auth Server origin
5. **Middleware order**: Added `UseAuthentication()` before `UseAuthorization()`

---

## Billing.WebApi: Updated Program.cs

The Billing API gets the **same changes**. Here's the complete updated `Program.cs`:

```csharp
// File: WebApplications/Billing.WebApi/Program.cs
using BuildingBlocks.Application;
using BuildingBlocks.Infrastructure.Auth;  // NEW: Shared auth infrastructure
using BuildingBlocks.Infrastructure.MassTransit.Configuration;
using BuildingBlocks.WebApplications.Filters;
using BuildingBlocks.WebApplications.Json;
using BuildingBlocks.WebApplications.OpenApi;
using Billing.Application;
using Billing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Service Defaults (Aspire observability, health checks, resilience)
builder.AddServiceDefaults();

// Controllers with exception filter and SmartEnum JSON serialization
builder.Services.AddControllers(options =>
{
    options.SuppressAsyncSuffixInActionNames = false;
    options.Filters.Add<ExceptionToJsonFilter>();
}).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new SmartEnumJsonConverterFactory());
});

// OpenAPI (Scalar documentation)
builder.Services.AddOpenApi();

// SQL Server connection and health checks
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "sqlserver", tags: ["ready"]);

// Infrastructure and Application layers
builder.Services.AddBillingInfrastructure(connectionString);
builder.Services.AddBillingApplication();
builder.Services.AddDefaultPipelineBehaviors();

// MassTransit with RabbitMQ for event-driven communication
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    configure.AddConsumers(typeof(Billing.Infrastructure.ServiceCollectionExtensions).Assembly);
});

// ==================== AUTHENTICATION & AUTHORIZATION (NEW) ====================

// Add OIDC cookie authentication (validates cookies issued by Auth Server)
builder.Services.AddOidcCookieAuth(builder.Configuration);

// Add authorization policies
builder.Services.AddAuthorization(options =>
{
    // AdminOnly: Only users with "Admin" role
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));

    // BillingStaffOrAdmin: Users with "BillingStaff" OR "Admin" role
    options.AddPolicy("BillingStaffOrAdmin", policy =>
        policy.RequireRole("BillingStaff", "Admin"));
});

// ==================== CORS (UPDATED) ====================

builder.Services.AddCors(options =>
{
    options.AddPolicy("Angular", policy => policy
        .WithOrigins(
            "https://localhost:7003",  // Angular SPA
            "https://localhost:7010")  // Auth Server
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());  // NEW: Required for cookie authentication
});

// ==============================================================================

var app = builder.Build();

// Default endpoints (health checks, etc.)
app.MapDefaultEndpoints();

// OpenAPI in development only
if (app.Environment.IsDevelopment())
{
    app.UseOpenApiWithScalar("Billing API");
}

// ==================== MIDDLEWARE PIPELINE (CRITICAL ORDER) ====================

app.UseHttpsRedirection();

// Authentication MUST come before Authorization
app.UseAuthentication();  // NEW: Validates cookies, populates HttpContext.User
app.UseAuthorization();   // Enforces [Authorize] attributes

app.UseCors("Angular");

app.MapControllers();

// ==============================================================================

app.Run();
```

**Note**: Billing has its own policies (`BillingStaffOrAdmin`) tailored to its domain.

---

## Protecting Controllers with [Authorize]

Now that authentication middleware is configured, we add `[Authorize]` attributes to controllers.

### Example: Scheduling PatientsController

```csharp
// File: WebApplications/Scheduling.WebApi/Controllers/PatientsController.cs
using BuildingBlocks.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;  // NEW
using Microsoft.AspNetCore.Mvc;
using Scheduling.Application.Commands;
using Scheduling.Application.Queries;

namespace Scheduling.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]  // NEW: All endpoints require authentication
public class PatientsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Get all patients (paginated).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPatientsAsync(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new GetPatientsQuery(pageNumber, pageSize);
        var result = await sender.Send(query, cancellationToken);

        return Ok(new PagedResponse<PatientDto>(result));
    }

    /// <summary>
    /// Get patient by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetPatientByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var query = new GetPatientByIdQuery(id);
        var result = await sender.Send(query, cancellationToken);

        if (result is null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// Create a new patient.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "NurseOrHigher")]  // NEW: Only Nurse, Doctor, or Admin can create
    public async Task<IActionResult> CreatePatientAsync(
        [FromBody] CreatePatientCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(command, cancellationToken);

        return CreatedAtAction(
            nameof(GetPatientByIdAsync),
            new { id = result.Id },
            result);
    }

    /// <summary>
    /// Update patient contact info.
    /// </summary>
    [HttpPut("{id:guid}/contact-info")]
    [Authorize(Policy = "NurseOrHigher")]  // NEW: Only Nurse, Doctor, or Admin can update
    public async Task<IActionResult> UpdatePatientContactInfoAsync(
        Guid id,
        [FromBody] UpdatePatientContactInfoCommand command,
        CancellationToken cancellationToken = default)
    {
        if (id != command.PatientId)
            return BadRequest("ID in URL does not match ID in body.");

        await sender.Send(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Deactivate a patient (soft delete).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]  // NEW: Only Admin can deactivate
    public async Task<IActionResult> DeactivatePatientAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var command = new DeactivatePatientCommand(id);
        await sender.Send(command, cancellationToken);

        return NoContent();
    }
}
```

### Authorization Policy Summary

| Endpoint | Policy | Allowed Roles | Reasoning |
|----------|--------|---------------|-----------|
| `GET /api/patients` | *(default)* | Any authenticated user | Read-only, safe |
| `GET /api/patients/{id}` | *(default)* | Any authenticated user | Read-only, safe |
| `POST /api/patients` | `NurseOrHigher` | Nurse, Doctor, Admin | Data entry operation |
| `PUT /api/patients/{id}/contact-info` | `NurseOrHigher` | Nurse, Doctor, Admin | Data modification |
| `DELETE /api/patients/{id}` | `AdminOnly` | Admin only | Destructive operation |

---

## Auth Endpoints Are Not Protected

The `AuthController` from `BuildingBlocks.Infrastructure.Auth` (doc 03) provides:
- `POST /auth/login` - Issues authentication cookie
- `POST /auth/logout` - Clears cookie
- `GET /auth/me` - Returns current user info

These endpoints are **intentionally not protected** because:
- `/login` must be accessible to unauthenticated users
- `/logout` should work even with expired cookies
- `/me` needs `[Authorize]` to return user info, but returns 401 if not authenticated (which is fine)

The `AuthController` already has appropriate attributes:

```csharp
[ApiController]
[Route("[controller]")]
public class AuthController : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]  // Explicitly allow anonymous
    public async Task<IActionResult> LoginAsync(...) { ... }

    [HttpPost("logout")]
    [AllowAnonymous]  // Allow logout even with expired cookie
    public IActionResult Logout(...) { ... }

    [HttpGet("me")]
    [Authorize]  // Requires authentication
    public IActionResult GetCurrentUser() { ... }
}
```

---

## CORS with AllowCredentials() Explained

### Before (Broken for Cookie Auth)

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("Angular", policy => policy
        .WithOrigins("https://localhost:7003")
        .AllowAnyHeader()
        .AllowAnyMethod());
    // Missing: .AllowCredentials()
});
```

**Problem**: Without `AllowCredentials()`, the browser **blocks** cookies from being sent with cross-origin requests. Angular's `HttpClient` with `withCredentials: true` will fail.

### After (Correct)

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("Angular", policy => policy
        .WithOrigins(
            "https://localhost:7003",  // Angular origin
            "https://localhost:7010")  // Auth Server origin
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());  // Required for cookies
});
```

**Why**: `AllowCredentials()` sets the `Access-Control-Allow-Credentials: true` header, telling the browser it's safe to include cookies in the request.

**Security Note**: When using `AllowCredentials()`, you **cannot** use `AllowAnyOrigin()`. You must explicitly list allowed origins.

---

## Testing the Protected API

### 1. Test Without Authentication (401 Unauthorized)

```bash
# Request without cookie
curl -X GET https://localhost:7001/api/patients \
  -H "Content-Type: application/json" \
  --insecure

# Response: 401 Unauthorized
{
  "type": "https://tools.ietf.org/html/rfc7235#section-3.1",
  "title": "Unauthorized",
  "status": 401
}
```

### 2. Login via Auth Server

```bash
# Login to get authentication cookie
curl -X POST https://localhost:7010/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "username": "admin@hospital.local",
    "password": "Admin@123"
  }' \
  --cookie-jar cookies.txt \
  --insecure

# Response: 200 OK with Set-Cookie header
```

### 3. Test With Authentication (200 OK)

```bash
# Request with cookie
curl -X GET https://localhost:7001/api/patients \
  -H "Content-Type: application/json" \
  --cookie cookies.txt \
  --insecure

# Response: 200 OK with patient data
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "firstName": "John",
      "lastName": "Doe",
      ...
    }
  ],
  "pageNumber": 1,
  "pageSize": 20,
  "totalCount": 1
}
```

### 4. Test Authorization Policy (403 Forbidden)

```bash
# Login as a Nurse
curl -X POST https://localhost:7010/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "username": "nurse@hospital.local",
    "password": "Nurse@123"
  }' \
  --cookie-jar cookies.txt \
  --insecure

# Try to delete a patient (AdminOnly policy)
curl -X DELETE https://localhost:7001/api/patients/3fa85f64-5717-4562-b3fc-2c963f66afa6 \
  --cookie cookies.txt \
  --insecure

# Response: 403 Forbidden
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.3",
  "title": "Forbidden",
  "status": 403
}
```

---

## Testing in Scalar (OpenAPI UI)

When you navigate to `https://localhost:7001/scalar/v1`, you'll notice that protected endpoints return 401.

### Option 1: Login First in Browser

1. Open `https://localhost:7010/auth/login` in the same browser
2. POST credentials via a tool like Postman or curl
3. The cookie is set in your browser
4. Refresh Scalar at `https://localhost:7001/scalar/v1`
5. Now requests include the cookie automatically

### Option 2: Use [AllowAnonymous] for Development

Temporarily add `[AllowAnonymous]` to specific endpoints during development:

```csharp
[HttpGet]
[AllowAnonymous]  // TEMP: For Scalar testing
public async Task<IActionResult> GetPatientsAsync(...) { ... }
```

**Warning**: Remove `[AllowAnonymous]` before deploying to production.

### Option 3: Use Scalar's Cookie Support

Scalar supports sending cookies if they're already set in the browser. The easiest flow:
1. Use a separate tab to POST to `/auth/login`
2. Return to Scalar tab
3. Requests now include the cookie

---

## Configuration: appsettings.json

Both WebApi projects need the same `Auth` configuration section added to `appsettings.json`:

```json
// File: WebApplications/Scheduling.WebApi/appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=SchedulingDb;Trusted_Connection=True;MultipleActiveResultSets=true"
  },
  "Auth": {
    "Authority": "https://localhost:7010",
    "Audiences": [
      "scheduling-api",
      "billing-api"
    ]
  }
}
```

```json
// File: WebApplications/Billing.WebApi/appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=BillingDb;Trusted_Connection=True;MultipleActiveResultSets=true"
  },
  "Auth": {
    "Authority": "https://localhost:7010",
    "Audiences": [
      "scheduling-api",
      "billing-api"
    ]
  }
}
```

**Why this config matters**:
- `Authority`: The Auth Server that issued the cookie (must match for validation)
- `Audiences`: The APIs that can accept this cookie (both APIs trust cookies for both audiences)

---

## Common Issues and Troubleshooting

### Issue: 401 Unauthorized Even After Login

**Symptoms**: Cookie is set, but API still returns 401.

**Causes**:
1. **Middleware order wrong**: `UseAuthorization()` is before `UseAuthentication()`
2. **Missing `AllowCredentials()`**: Browser doesn't send cookie due to CORS
3. **Authority mismatch**: `appsettings.json` has wrong Authority URL
4. **Cookie domain mismatch**: Auth Server and API on different domains without proper configuration

**Fix**: Verify middleware order and CORS configuration as shown above.

### Issue: 403 Forbidden (Not 401)

**Symptoms**: Authentication works, but user gets 403 on certain endpoints.

**Cause**: User doesn't have the required role for the authorization policy.

**Fix**: Verify the user's roles in the Auth Server database and ensure the policy matches.

### Issue: OPTIONS Preflight Fails

**Symptoms**: Browser shows CORS error in console before actual request is sent.

**Cause**: CORS policy doesn't allow credentials on preflight.

**Fix**: Ensure `AllowCredentials()` is present in CORS policy.

### Issue: Cookie Not Sent from Angular

**Symptoms**: Angular `HttpClient` doesn't include cookie in requests.

**Cause**: Missing `withCredentials: true` in Angular HTTP call.

**Fix**: (Covered in doc 05) Add `withCredentials: true` to every HTTP request:

```typescript
this.http.get('https://localhost:7001/api/patients', { withCredentials: true })
```

---

## Security Best Practices

### 1. Use HTTPS Everywhere

Cookies marked `Secure` (which Duende IdentityServer does by default) are **only sent over HTTPS**. Ensure all services use HTTPS in development and production.

### 2. HttpOnly Cookies

Duende IdentityServer sets `HttpOnly = true`, preventing JavaScript from accessing the cookie. This protects against XSS attacks.

### 3. SameSite Policy

Duende IdentityServer sets `SameSite = SameSiteMode.Lax` by default. For stricter security, consider:

```csharp
// In BuildingBlocks.Infrastructure.Auth configuration
options.UseCookies(cookieOptions =>
{
    cookieOptions.Cookie.SameSite = SameSiteMode.Strict; // Stricter CSRF protection
});
```

**Tradeoff**: `SameSite.Strict` can break legitimate cross-site flows (e.g., OAuth redirects).

### 4. Role-Based Access Control (RBAC)

Define granular policies:
- **AdminOnly**: Destructive operations (delete, archive)
- **StaffOrHigher**: Write operations (create, update)
- **Authenticated**: Read-only operations

### 5. Avoid Overly Permissive CORS

Never use:
```csharp
.AllowAnyOrigin()
.AllowCredentials()  // Invalid! Cannot combine these.
```

Always explicitly list trusted origins.

---

## Summary

In this document, we:

1. **Added authentication middleware** (`UseAuthentication()`) to both APIs
2. **Added authorization middleware** (`UseAuthorization()`) in the correct order
3. **Configured CORS** to allow credentials (`AllowCredentials()`)
4. **Applied `[Authorize]` attributes** to controllers and methods
5. **Defined authorization policies** for role-based access control
6. **Updated `appsettings.json`** with Auth configuration
7. **Tested protected endpoints** with and without authentication

### What We Built

```
┌─────────────────────────────────────────────────────────────────┐
│                         Angular SPA                             │
│                     (https://localhost:7003)                    │
│                                                                 │
│  - Sends requests with withCredentials: true                   │
│  - Browser automatically includes HttpOnly cookie              │
└────────────────┬────────────────────────────────────────────────┘
                 │
                 │ HTTPS + Cookie
                 │
    ┌────────────▼────────────┐         ┌──────────────────────┐
    │   Scheduling.WebApi     │         │   Billing.WebApi     │
    │ (https://localhost:7001)│         │(https://localhost:7002)│
    │                         │         │                      │
    │ Middleware Pipeline:    │         │ Middleware Pipeline: │
    │ 1. UseAuthentication()  │         │ 1. UseAuthentication()│
    │ 2. UseAuthorization()   │         │ 2. UseAuthorization()│
    │ 3. UseCors()            │         │ 3. UseCors()         │
    │                         │         │                      │
    │ Controllers:            │         │ Controllers:         │
    │ - [Authorize]           │         │ - [Authorize]        │
    │ - [Authorize("Admin")]  │         │ - [Authorize("...")]│
    └─────────────────────────┘         └──────────────────────┘
                 │                                   │
                 └───────────────┬───────────────────┘
                                 │
                                 │ Cookie Validation
                                 │
                    ┌────────────▼────────────┐
                    │      Auth Server        │
                    │ (https://localhost:7010)│
                    │                         │
                    │ - Issues cookies        │
                    │ - Stores users/roles    │
                    │ - Validates cookies     │
                    └─────────────────────────┘
```

### Next Steps

With the APIs now protected, we need to update the **Angular frontend** to:
1. Login via the Auth Server
2. Include credentials in all HTTP requests
3. Handle 401/403 responses gracefully
4. Display user info and role-based UI

---

> Next: [05-angular-auth.md](./05-angular-auth.md) - Angular Authentication
