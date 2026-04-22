# Phase 8.3: Shared Authentication Infrastructure

> Previous: [02-authorization-server-setup.md](./02-authorization-server-setup.md)

This document covers building a reusable authentication infrastructure layer in BuildingBlocks that both the Scheduling and Billing APIs can consume. We follow the same extension method pattern established by `BuildingBlocks.Infrastructure.MassTransit`.

---

## Why a Shared BuildingBlock?

In a microservices architecture with multiple APIs, each API needs the same authentication capabilities:

- Cookie authentication middleware
- OpenID Connect (OIDC) client configuration
- Claim mapping and transformation
- Access to current user information (`ICurrentUser`)
- Shared Data Protection keys (so cookies work across APIs)

Rather than duplicate this configuration in every API's `Program.cs`, we extract it into a reusable BuildingBlock following the pattern already established in this project:

| BuildingBlock | Purpose | Entry Point |
|---------------|---------|-------------|
| `BuildingBlocks.Infrastructure.MassTransit` | Event bus integration | `AddMassTransitEventBus()` |
| `BuildingBlocks.Infrastructure.Auth` | Authentication infrastructure | `AddOidcCookieAuth()` |

Both follow the same pattern: a single extension method that APIs call in `Program.cs`, encapsulating all the complexity.

---

## Project Structure

```
BuildingBlocks/
  BuildingBlocks.Infrastructure.Auth/
    BuildingBlocks.Infrastructure.Auth.csproj
    AuthExtensions.cs                    # AddOidcCookieAuth() extension
    AuthController.cs                    # /auth/login, /auth/logout, /auth/current-user
    HttpContextCurrentUser.cs            # ICurrentUser implementation
    DataProtection/
      DataProtectionDbContext.cs          # Shared Data Protection key store (optional)
```

This project is referenced by both `Scheduling.WebApi` and `Billing.WebApi`.

---

## 1. Create the Project

Add a new class library in the `BuildingBlocks` directory:

```bash
# From the solution root
cd BuildingBlocks
dotnet new classlib -n BuildingBlocks.Infrastructure.Auth
dotnet sln ../DDD.sln add BuildingBlocks.Infrastructure.Auth/BuildingBlocks.Infrastructure.Auth.csproj
```

### Project File

```xml
<!-- BuildingBlocks/BuildingBlocks.Infrastructure.Auth/BuildingBlocks.Infrastructure.Auth.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Data Protection for shared cookie encryption keys -->
    <PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" />
    <!-- OIDC client middleware for AddOpenIdConnect -->
    <PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" />

    <!-- Required for controller endpoints (/auth/login, /auth/logout, /auth/current-user) -->
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <!-- ICurrentUser interface lives in Application layer -->
    <ProjectReference Include="..\BuildingBlocks.Application\BuildingBlocks.Application.csproj" />
  </ItemGroup>

</Project>
```

**Why these dependencies?**

- **Microsoft.AspNetCore.Authentication.OpenIdConnect**: Provides `AddOpenIdConnect()` for the OIDC client middleware. Works with any OIDC-compliant server, including Duende IdentityServer
- **Microsoft.AspNetCore.DataProtection.EntityFrameworkCore**: Allows storing Data Protection keys in SQL Server so both APIs can decrypt the same cookies
- **BuildingBlocks.Application**: Contains the `ICurrentUser` interface that domain handlers depend on

---

## 2. The AddOidcCookieAuth() Extension Method

This is the main entry point, mirroring the pattern from `AddMassTransitEventBus()`. It encapsulates all authentication setup.

```csharp
// BuildingBlocks/BuildingBlocks.Infrastructure.Auth/AuthExtensions.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Application.Abstractions;

namespace BuildingBlocks.Infrastructure.Auth;

/// <summary>
/// Extension methods for configuring cookie-based authentication with OIDC.
/// </summary>
public static class AuthExtensions
{
    /// <summary>
    /// Adds cookie authentication with OpenID Connect (OIDC) client configured for any OIDC-compliant authorization server (e.g., Duende IdentityServer).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration containing Auth:Authority, Auth:ClientId, Auth:ClientSecret, Auth:SharedKeysPath.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOidcCookieAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Read auth configuration
        var authority = configuration["Auth:Authority"]
            ?? throw new InvalidOperationException("Auth:Authority is required in configuration");
        var clientId = configuration["Auth:ClientId"]
            ?? throw new InvalidOperationException("Auth:ClientId is required in configuration");
        var clientSecret = configuration["Auth:ClientSecret"]
            ?? throw new InvalidOperationException("Auth:ClientSecret is required in configuration");
        var sharedKeysPath = configuration["Auth:SharedKeysPath"];

        // 1. Configure cookie authentication as the default scheme
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = "oidc"; // Custom name for OIDC handler
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.Name = "DDD.Auth";
            options.Cookie.HttpOnly = true;      // Prevent JavaScript access (XSS protection)
            options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always; // HTTPS only
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax; // CSRF protection
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;    // Refresh cookie on activity

            // Redirect paths — these map to AuthController endpoints (see section 3 below)
            options.LoginPath = "/auth/login";
            options.LogoutPath = "/auth/logout";
            options.AccessDeniedPath = "/auth/access-denied";
        })
        // 2. Configure OpenID Connect client (talks to Auth Server)
        .AddOpenIdConnect("oidc", options =>
        {
            options.Authority = authority;       // https://localhost:7010
            options.ClientId = clientId;         // "scheduling-api" or "billing-api"
            options.ClientSecret = clientSecret; // From Auth Server client configuration
            options.ResponseType = "code";       // Authorization Code flow (most secure)
            options.RequireHttpsMetadata = true; // Enforce HTTPS for production

            // Request scopes (must match what Auth Server allows for this client)
            options.Scope.Clear();
            options.Scope.Add("openid");         // Required for OIDC
            options.Scope.Add("profile");        // Get name, email claims
            options.Scope.Add("roles");          // Get role claims

            // Save tokens in cookie properties (access_token, refresh_token)
            // Set to false for cookie-only auth (tokens stay on server)
            options.SaveTokens = false;

            // Map OIDC claims to .NET ClaimTypes
            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "role";

            // Map additional claims from ID token to cookie
            options.ClaimActions.MapJsonKey("email", "email");
            options.ClaimActions.MapJsonKey("sub", "sub"); // Subject (user ID)

            // OIDC event hooks — listed in execution order, uncomment as needed
            options.Events = new OpenIdConnectEvents
            {
                // 1. Before redirecting to the Identity Provider login page
                // OnRedirectToIdentityProvider = context => { ... },

                // 2. After receiving the auth code, before exchanging it for tokens
                // OnAuthorizationCodeReceived = context => { ... },

                // 3. After receiving tokens from the token endpoint
                // OnTokenResponseReceived = context => { ... },

                // 4. After ID token is validated, before claims are saved to cookie
                OnTokenValidated = context =>
                {
                    // Claims transformation can happen here if needed
                    return Task.CompletedTask;
                },

                // 5. After calling the userinfo endpoint (if enabled)
                // OnUserInformationReceived = context => { ... },

                // Error handler — fires when something goes wrong at any point
                // OnRemoteFailure = context => { ... },

                // Logout flow — after the logout callback redirect from auth server
                // OnSignedOutCallbackRedirect = context => { ... },
            };
        });

        // 3. Configure Data Protection for shared cookie encryption
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("DDD.WebApis"); // MUST be same across all APIs

        if (!string.IsNullOrEmpty(sharedKeysPath))
        {
            // Store keys in shared file system location
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(sharedKeysPath));
        }
        // else: ephemeral keys (in-memory) — cookies won't work across API restarts or different APIs

        // 4. Register ICurrentUser implementation
        services.AddHttpContextAccessor(); // Required for HttpContextCurrentUser
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        return services;
    }
}
```

### OIDC Event Hooks Reference

The `OpenIdConnectEvents` provide hooks into the OIDC authentication flow. Listed here in **execution order** during a login flow:

| # | Event | When it fires | Common use |
|---|-------|--------------|------------|
| 1 | **OnRedirectToIdentityProvider** | Before redirecting to login page | Add extra parameters, change redirect URI |
| 2 | **OnAuthorizationCodeReceived** | After receiving the auth code, before exchanging it for tokens | Inspect or modify the code exchange |
| 3 | **OnTokenResponseReceived** | After receiving tokens from the token endpoint | Store tokens, log token metadata |
| 4 | **OnTokenValidated** | After ID token is validated, before claims are saved to cookie | Transform/add/remove claims |
| 5 | **OnUserInformationReceived** | After calling the userinfo endpoint (if enabled) | Merge additional claims |
| | **OnRemoteFailure** | When something goes wrong at any point in the flow | Custom error handling, redirect to error page |
| | **OnSignedOutCallbackRedirect** | After logout callback from auth server (logout flow only) | Custom post-logout redirect |

`OnTokenValidated` (#4) is the most commonly used — it's where you'd map IdentityServer claims to your app's claim structure (e.g., renaming claim types or adding claims from your own database).

### Configuration in appsettings.json

Each API needs its own client configuration:

```json
// WebApplications/Scheduling.WebApi/appsettings.json
{
  "Auth": {
    "Authority": "https://localhost:7010",
    "ClientId": "scheduling-api",
    "ClientSecret": "scheduling-secret-change-in-production",
    "SharedKeysPath": "C:\\SharedKeys\\DDD"
  }
}
```

```json
// WebApplications/Billing.WebApi/appsettings.json
{
  "Auth": {
    "Authority": "https://localhost:7010",
    "ClientId": "billing-api",
    "ClientSecret": "billing-secret-change-in-production",
    "SharedKeysPath": "C:\\SharedKeys\\DDD"
  }
}
```

**Why different ClientIds?**

Each API is a separate OIDC client from the Auth Server's perspective. This allows:
- Different redirect URIs per API
- Different scopes/permissions per API
- Auditing which API a user logged into
- Revoking access to one API without affecting others

---

## 3. AuthController Endpoints

The `AuthController` provides three endpoints that the Angular SPA calls to manage authentication.

```csharp
// BuildingBlocks/BuildingBlocks.Infrastructure.Auth/AuthController.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BuildingBlocks.Application.Abstractions;

namespace BuildingBlocks.Infrastructure.Auth;

/// <summary>
/// Handles authentication flows: login, logout, and current user info.
/// </summary>
[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// Initiates OIDC login flow by redirecting to the Auth Server.
    /// </summary>
    /// Invoked by the cookie middleware's LoginPath (configured in AddOidcCookieAuth).
    /// <param name="returnUrl">URL to redirect to after successful authentication.</param>
    /// <returns>Challenge result that redirects to Auth Server login page.</returns>
    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        // If already authenticated, just redirect
        if (User.Identity?.IsAuthenticated == true)
        {
            return Redirect(returnUrl ?? "/");
        }

        // Trigger OIDC challenge (redirect to Auth Server)
        var authProperties = new AuthenticationProperties
        {
            RedirectUri = returnUrl ?? "/",
            IsPersistent = true // "Remember me" — cookie persists across browser sessions
        };

        return Challenge(authProperties, "oidc");
    }

    /// <summary>
    /// Signs out the user locally and from the Auth Server.
    /// Redirects to the Auth Server's end session endpoint, which then redirects
    /// back to the PostLogoutRedirectUri configured per-client in IdentityServer's Config.cs.
    /// </summary>
    /// Invoked by the cookie middleware's LogoutPath (configured in AddOidcCookieAuth).
    [HttpGet("logout")]
    public IActionResult Logout()
    {
        // Signs out of both cookie and OIDC (redirects to Auth Server logout endpoint)
        return SignOut(new AuthenticationProperties(), CookieAuthenticationDefaults.AuthenticationScheme, "oidc");
    }

    /// <summary>
    /// Returns current authenticated user information from cookie claims.
    /// </summary>
    [HttpGet("current-user")]
    [Authorize]
    public IActionResult GetCurrentUser(
        [FromServices] ICurrentUser currentUser)
    {
        return Ok(new { currentUser.UserId, currentUser.Name, currentUser.Email, currentUser.Roles });
    }

    /// <summary>
    /// Fallback endpoint for access denied scenarios.
    /// Invoked by the cookie middleware's AccessDeniedPath (configured in AddOidcCookieAuth).
    /// </summary>
    [HttpGet("access-denied")]
    public IActionResult AccessDenied()
    {
        return StatusCode(403, new { message = "Access denied. You do not have permission to access this resource." });
    }
}
```

### How Angular Uses These Endpoints

```typescript
// Angular auth.service.ts
class AuthService {
  // Check if user is logged in
  getCurrentUser() {
    return this.http.get('/auth/current-user');
  }

  // Redirect to login
  login(returnUrl: string) {
    window.location.href = `/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`;
  }

  // Logout
  logout() {
    return this.http.post('/auth/logout', {});
  }
}
```

---

## 4. HttpContextCurrentUser Implementation

Domain handlers need access to the current user (e.g., "Who is creating this appointment?"). The `ICurrentUser` interface (defined in `BuildingBlocks.Application`, covered in doc 06) is implemented by reading claims from the HTTP context.

```csharp
// BuildingBlocks/BuildingBlocks.Infrastructure.Auth/HttpContextCurrentUser.cs
using BuildingBlocks.Application.Auth;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace BuildingBlocks.Infrastructure.Auth;

/// <summary>
/// Provides access to the current authenticated user from the HTTP context.
/// </summary>
internal sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets the current user's unique identifier (sub claim).
    /// </summary>
    public string? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            return user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");
        }
    }

    /// <summary>
    /// Gets the current user's email address.
    /// </summary>
    public string? Email
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            return user.FindFirstValue(ClaimTypes.Email)
                ?? user.FindFirstValue("email");
        }
    }

    /// <summary>
    /// Gets the current user's display name.
    /// </summary>
    public string? Name
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            return user.FindFirstValue(ClaimTypes.Name)
                ?? user.FindFirstValue("name");
        }
    }

    /// <summary>
    /// Gets the current user's roles.
    /// </summary>
    public IReadOnlyList<string> Roles
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return Array.Empty<string>();

            return user.FindAll(ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();
        }
    }

    /// <summary>
    /// Indicates whether the current user is authenticated.
    /// </summary>
    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;
}
```

### Usage in Domain Handlers

```csharp
// Scheduling.Application/Commands/CreateAppointmentCommandHandler.cs
public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Result<Guid>>
{
    private readonly ICurrentUser _currentUser; // Injected

    public async Task<Result<Guid>> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
    {
        // No auth check needed — [Authorize] on the endpoint already guarantees authentication.
        // By the time a handler executes, the user is always authenticated.
        var createdBy = _currentUser.UserId; // For audit logging

        // ... rest of handler logic
    }
}
```

**Why Scoped lifetime?**

- `IHttpContextAccessor` is scoped (one per HTTP request)
- Claims come from the HTTP context, which is per-request
- Each request gets a fresh `HttpContextCurrentUser` instance with that request's claims

---

## 5. Shared Data Protection Keys

> **When do you need shared Data Protection keys?**
>
> | Scenario | Shared keys needed? | Why |
> |----------|-------------------|-----|
> | **Multiple APIs, no gateway** (this phase) | Yes | Each API must decrypt the same cookie |
> | **Single app, scaled horizontally** | Yes | Multiple instances must share the same key ring |
> | **BFF gateway** (Phase 9) | No (across APIs) | Only the BFF handles cookies; downstream APIs receive JWTs |
> | **Machine-to-machine** (Client Credentials) | No | No cookies involved — bearer tokens only |
>
> In this phase we don't have a BFF yet, so shared keys are required across APIs. Once a BFF is introduced (Phase 9), this shared key setup can be removed from the individual APIs — only the BFF itself needs key persistence if scaled horizontally.

### The Problem

ASP.NET Core's cookie authentication uses **Data Protection** to encrypt cookies. By default:
- Each app generates its own encryption keys
- Keys are stored in-memory (ephemeral)
- When the app restarts, old cookies become unreadable

In a multi-API scenario:
- User logs into Scheduling API → cookie encrypted with Scheduling's keys
- User calls Billing API → Billing can't decrypt the cookie (different keys)
- Result: user appears logged out

### The Solution

Store Data Protection keys in a **shared location** that all APIs can access:

1. **File system** (simple, dev-friendly)
2. **Database** (production-ready, supports key rotation)
3. **Redis** (distributed, scalable)

For this project, we'll use the **file system** for development.

### File System Implementation

```csharp
// In AuthExtensions.cs (already shown above)
var dataProtection = services.AddDataProtection()
    .SetApplicationName("DDD.WebApis"); // CRITICAL: must be same across all APIs

if (!string.IsNullOrEmpty(sharedKeysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(sharedKeysPath));
}
```

### API Configuration

```csharp
// WebApplications/Scheduling.WebApi/Program.cs
builder.Services.AddOidcCookieAuth(builder.Configuration);
```

```json
// WebApplications/Scheduling.WebApi/appsettings.Development.json
{
  "Auth": {
    "Authority": "https://localhost:7010",
    "ClientId": "scheduling-api",
    "ClientSecret": "scheduling-secret-change-in-production",
    "SharedKeysPath": "C:\\SharedKeys\\DDD"
  }
}
```

**Both APIs must use the same path** (`C:\SharedKeys\DDD`).

### Database Implementation (Optional)

For production, store keys in SQL Server:

```csharp
// BuildingBlocks/BuildingBlocks.Infrastructure.Auth/DataProtection/DataProtectionDbContext.cs
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure.Auth.DataProtection;

public class DataProtectionDbContext : DbContext, IDataProtectionKeyContext
{
    public DataProtectionDbContext(DbContextOptions<DataProtectionDbContext> options)
        : base(options)
    {
    }

    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
}
```

```csharp
// AuthExtensions.cs modification
services.AddDbContext<DataProtectionDbContext>(options =>
    options.UseSqlServer(connectionString));

services.AddDataProtection()
    .SetApplicationName("DDD.WebApis")
    .PersistKeysToDbContext<DataProtectionDbContext>();
```

Run migrations to create the `DataProtectionKeys` table. Both APIs connect to the same database.

---

## 6. How It All Fits Together

```
┌─────────────────┐
│  Angular SPA    │
│  localhost:7003 │
└────────┬────────┘
         │
         │ 1. GET /api/patients (with cookie: DDD.Auth=encrypted_data)
         │
         v
┌─────────────────────────────────────────────────────────────┐
│  Scheduling API (localhost:7001)                            │
│  ────────────────────────────────────────────────────────   │
│                                                              │
│  Program.cs:                                                 │
│    services.AddOidcCookieAuth(configuration, keys)          │ ← BuildingBlocks.Infrastructure.Auth
│                                                              │
│  2. Cookie middleware reads "DDD.Auth" cookie                │
│  3. Data Protection decrypts cookie using shared keys        │
│  4. OIDC handler validates claims                            │
│  5. Claims extracted → HttpContext.User populated            │
│  6. [Authorize] attribute checks IsAuthenticated             │
│  7. HttpContextCurrentUser reads claims from HttpContext     │
│                                                              │
└────────┬────────────────────────────────────────────────────┘
         │
         │ 8. Controller/Handler executes
         │    ICurrentUser.UserId = "123e4567-..."
         │
         v
┌─────────────────────────────────────────────────────────────┐
│  CreateAppointmentCommandHandler                            │
│  ──────────────────────────────────────────────────────     │
│                                                              │
│  var appointment = Appointment.Create(                       │
│      patientId,                                              │
│      doctorId,                                               │
│      scheduledAt,                                            │
│      createdBy: _currentUser.UserId  ← from cookie claims   │
│  );                                                          │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

### First-Time Login Flow

```
Angular SPA                  Scheduling API              Auth Server
    │                             │                           │
    │  GET /auth/current-user               │                           │
    ├────────────────────────────>│                           │
    │  401 Unauthorized            │                           │
    │<────────────────────────────┤                           │
    │                             │                           │
    │  Redirect to /auth/login    │                           │
    ├────────────────────────────>│                           │
    │  Challenge "oidc"            │                           │
    │<────────────────────────────┤                           │
    │                             │                           │
    │  302 Redirect: https://localhost:7010/connect/authorize │
    ├─────────────────────────────────────────────────────────>│
    │                                                          │
    │  (User sees login page)                                 │
    │  POST /connect/token (username/password)                │
    ├─────────────────────────────────────────────────────────>│
    │                                                          │
    │  302 Redirect: https://localhost:7001?code=abc123       │
    │<─────────────────────────────────────────────────────────┤
    │                             │                           │
    │  GET /?code=abc123          │                           │
    ├────────────────────────────>│                           │
    │                             │  Exchange code for token  │
    │                             ├──────────────────────────>│
    │                             │  ID token + claims        │
    │                             │<──────────────────────────┤
    │                             │                           │
    │  Set-Cookie: DDD.Auth=...   │                           │
    │<────────────────────────────┤                           │
    │                             │                           │
    │  GET /auth/current-user (with cookie) │                           │
    ├────────────────────────────>│                           │
    │  200 { userId, email, ... } │                           │
    │<────────────────────────────┤                           │
```

---

## 7. Adding Auth to the APIs

### Reference the BuildingBlock

```xml
<!-- WebApplications/Scheduling.WebApi/Scheduling.WebApi.csproj -->
<ItemGroup>
  <ProjectReference Include="..\..\BuildingBlocks\BuildingBlocks.Infrastructure.Auth\BuildingBlocks.Infrastructure.Auth.csproj" />
</ItemGroup>
```

Do the same for `Billing.WebApi`.

### Update Program.cs

```csharp
// WebApplications/Scheduling.WebApi/Program.cs
using BuildingBlocks.Infrastructure.Auth;

var builder = WebApplication.CreateBuilder(args);

// Existing services
builder.AddServiceDefaults();
builder.Services.AddControllers();

// Existing infrastructure
builder.Services.AddSchedulingInfrastructure(connectionString);
builder.Services.AddSchedulingApplication();
builder.Services.AddDefaultPipelineBehaviors();
builder.Services.AddMassTransitEventBus(builder.Configuration, configurator =>
{
    // ... consumers
});

// Add authentication (NEW)
builder.Services.AddOidcCookieAuth(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("https://localhost:7003") // Angular SPA
              .AllowCredentials()  // REQUIRED for cookies
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Enable authentication middleware (NEW)
app.UseAuthentication(); // MUST come before UseAuthorization
app.UseAuthorization();

app.UseCors();
app.MapControllers();
app.Run();
```

**Critical ordering:**
1. `UseAuthentication()` — reads cookie, populates `HttpContext.User`
2. `UseAuthorization()` — checks `[Authorize]` attributes against `HttpContext.User`

### appsettings.json

```json
// WebApplications/Scheduling.WebApi/appsettings.json
{
  "Auth": {
    "Authority": "https://localhost:7010",
    "ClientId": "scheduling-api",
    "ClientSecret": "scheduling-secret-change-in-production",
    "SharedKeysPath": "C:\\SharedKeys\\DDD"
  }
}
```

Repeat for `Billing.WebApi` with `ClientId: "billing-api"` and a different secret.

### Shared Keys Directory

The Data Protection framework automatically creates the `SharedKeysPath` directory on first startup if it doesn't exist. No manual `mkdir` needed — both APIs will write/read key files here.

---

## Key Concepts Explained

### Cookie vs Token Authentication

| Aspect | Cookie (this project) | Bearer Token (JWT) |
|--------|----------------------|-------------------|
| **Storage** | HttpOnly cookie (browser) | localStorage/sessionStorage |
| **Security** | XSS-safe, CSRF risk (mitigated by SameSite) | CSRF-safe, XSS risk |
| **Cross-API** | Requires shared Data Protection keys | Stateless (any API can validate) |
| **Native mobile apps** | No browser cookie jar available | Easy (just Authorization header) |
| **Use Case** | Browser-based SPAs (desktop + mobile) | Native mobile apps, third-party APIs |

We use cookies because:
- Angular SPA and APIs are on the same domain (localhost)
- HttpOnly cookies are immune to XSS attacks
- Simpler for learning (no token refresh logic)

For production with mobile apps, add JWT support (doc 05 optional topic).

### OIDC Server vs Client

| Role | Package/Technology | Responsibility |
|------|-------------------|----------------|
| **Server** | Duende IdentityServer | Issues tokens, validates credentials (Identity.WebApi project from doc 02) |
| **Client** | `Microsoft.AspNetCore.Authentication.OpenIdConnect` | Consumes tokens, validates with server (Scheduling/Billing APIs) |

The Auth Server (doc 02: Identity.WebApi) runs **Duende IdentityServer**. The Scheduling and Billing APIs are **clients** using standard ASP.NET Core OIDC middleware — this middleware works with any OIDC-compliant server, keeping the API projects provider-agnostic.

### Data Protection Key Rotation

ASP.NET Core automatically rotates Data Protection keys every 90 days. Old keys are kept for decryption (grace period). When using shared storage:
- First API to start generates the initial key
- Key is written to `C:\SharedKeys\DDD\key-{guid}.xml`
- Other APIs read the same key
- After 90 days, a new key is generated, old one retained for 90 more days

No manual intervention needed.

---

## Testing the Setup

### 1. Start the Auth Server

```bash
cd WebApplications/Scheduling.AuthServer
dotnet run
```

Verify it's running at `https://localhost:7010`.

### 2. Start Scheduling API

```bash
cd WebApplications/Scheduling.WebApi
dotnet run
```

### 3. Test Login Flow

```bash
# Should return 401 (not authenticated)
curl https://localhost:7001/auth/current-user

# Trigger login (will redirect to Auth Server)
curl -L https://localhost:7001/auth/login
```

You'll be redirected to the Auth Server login page. After entering credentials, you'll be redirected back with a cookie.

### 4. Test Authenticated Request

```bash
# Save cookie from login response
curl -c cookies.txt https://localhost:7001/auth/login

# Use cookie for subsequent requests
curl -b cookies.txt https://localhost:7001/auth/current-user
# Should return: { "userId": "...", "email": "...", "isAuthenticated": true }
```

### 5. Verify Shared Keys

Check that keys were created:

```bash
dir C:\SharedKeys\DDD
# Should see: key-{guid}.xml
```

Start Billing API and verify it uses the same key (no new key generated).

---

## Common Issues

### "No authenticationScheme was specified"

**Cause**: Missing `AddAuthentication()` call.

**Fix**: Ensure `AddOidcCookieAuth()` is called in `Program.cs`.

### "Correlation failed"

**Cause**: Cookie encryption keys differ between Auth Server callback and API.

**Fix**: Ensure `SetApplicationName("DDD.WebApis")` is the same across all APIs.

### "Access denied"

**Cause**: User doesn't have required role for `[Authorize(Roles = "Admin")]`.

**Fix**: Check user's roles in `/auth/current-user` response. Grant roles in Auth Server (doc 02).

### Cookies Not Sent from Angular

**Cause**: CORS `AllowCredentials()` not configured.

**Fix**: Add `.AllowCredentials()` to CORS policy AND set `withCredentials: true` in Angular HTTP requests (doc 04).

---

## Summary

You've built a reusable authentication infrastructure in `BuildingBlocks.Infrastructure.Auth` that:

- ✅ Provides a single extension method (`AddOidcCookieAuth`) following the project's BuildingBlock pattern
- ✅ Configures cookie authentication with standard OIDC client middleware for any OIDC-compliant authorization server (Duende IdentityServer in this project)
- ✅ Exposes `/auth/login`, `/auth/logout`, `/auth/current-user` endpoints for Angular integration
- ✅ Implements `ICurrentUser` for domain handlers to access authenticated user information
- ✅ Shares Data Protection keys across APIs so cookies work universally
- ✅ Maps OIDC claims to .NET claims for consistent access

Both Scheduling and Billing APIs can now call `AddOidcCookieAuth()` in two lines and get full authentication support.

**Next**: We'll protect API endpoints with `[Authorize]` attributes and implement role-based authorization.

---

> Next: [04-api-resource-protection.md](./04-api-resource-protection.md) - Securing the API endpoints
