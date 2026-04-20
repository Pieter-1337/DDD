# Phase 8.2 - Authorization Server Setup with Duende IdentityServer

This document walks through setting up a centralized OAuth 2.0/OpenID Connect authorization server using ASP.NET Core Identity and Duende IdentityServer. This server will be the single source of truth for authentication and issue access tokens to our microservices.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Why Duende IdentityServer?](#why-duende-identityserver)
3. [Project Structure](#project-structure)
4. [NuGet Package Setup](#nuget-package-setup)
5. [Database Context and Models](#database-context-and-models)
6. [Duende IdentityServer Configuration](#duende-identityserver-configuration)
7. [Razor Pages Login UI](#razor-pages-login-ui)
8. [Seed Data](#seed-data)
9. [Aspire Integration](#aspire-integration)
10. [Testing the Setup](#testing-the-setup)
11. [Next Steps](#next-steps)

---

## Architecture Overview

Our authorization server acts as the centralized identity provider for all bounded contexts:

```
┌─────────────────────────────────────────────────────────────────┐
│                    Identity.WebApi (Port 7010)                  │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ ASP.NET Core Identity (User Management)                  │  │
│  │  - ApplicationUser, IdentityRole                         │  │
│  │  - UserManager, SignInManager                            │  │
│  └──────────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ Duende IdentityServer (OAuth 2.0 / OIDC)                │  │
│  │  - Authorization endpoint                                │  │
│  │  - Token endpoint                                        │  │
│  │  - UserInfo endpoint                                     │  │
│  │  - Discovery endpoint                                    │  │
│  └──────────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ Razor Pages UI                                           │  │
│  │  - Login, Register, Logout                               │  │
│  └──────────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ IdentityDbContext + EF Core Stores                       │  │
│  │  - AspNetUsers, AspNetRoles (Identity)                   │  │
│  │  - Clients, ApiScopes, PersistedGrants (Duende)         │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                            │
                            │ Issues Access Tokens
                            ▼
        ┌───────────────────────────────────────┐
        │  Scheduling.WebApi (Port 7001)        │
        │  Billing.WebApi (Port 7002)           │
        │  Angular SPA (Port 7003)              │
        │                                       │
        │  Each validates tokens using OIDC     │
        └───────────────────────────────────────┘
```

**Key Concepts:**

- **ASP.NET Core Identity**: Handles user registration, password hashing, role management
- **Duende IdentityServer**: Implements OAuth 2.0 and OpenID Connect protocol on top of Identity
- **Authorization Code Flow**: Most secure flow for server-side applications and SPAs with PKCE
- **JWT Access Tokens** (default): Self-contained tokens validated locally by each API — no call to the auth server needed. Industry standard for most applications
- **Separate Database**: Identity has its own bounded context with its own database (IdentityDb)

---

## Why Duende IdentityServer?

Duende IdentityServer is the industry standard for OAuth 2.0 / OpenID Connect in .NET — the successor to the legendary IdentityServer4, built by the original team. Key benefits:

- **Free Community Edition** for learning, development, and small production (revenue < $1M)
- **Batteries-included** — built-in OIDC endpoints, no custom controllers needed
- **Excellent documentation** and large community
- **Seamless ASP.NET Identity integration** via `AddAspNetIdentity<T>()`
- **EF Core support** via separate Configuration and Operational stores

---

## Project Structure

Create the following structure under `WebApplications/`:

```
WebApplications/
└── Identity.WebApi/
    ├── Program.cs
    ├── Data/
    │   └── IdentityDbContext.cs
    ├── Models/
    │   └── ApplicationUser.cs
    ├── Pages/
    │   ├── _ViewImports.cshtml
    │   ├── _ViewStart.cshtml
    │   ├── Shared/
    │   │   ├── _Layout.cshtml
    │   │   └── _ValidationScriptsPartial.cshtml
    │   └── Account/
    │       ├── Login.cshtml
    │       ├── Login.cshtml.cs
    │       ├── Register.cshtml
    │       ├── Register.cshtml.cs
    │       ├── Logout.cshtml
    │       └── Logout.cshtml.cs
    ├── Config/
    │   └── IdentityServerConfig.cs
    ├── SeedData/
    │   └── IdentitySeedData.cs
    └── Identity.WebApi.csproj
```

> **Scaffold alternative:** Duende provides a `dotnet new` template that scaffolds this structure for you:
> ```bash
> dotnet new install Duende.IdentityServer.Templates
> dotnet new isaspid -n Identity.WebApi
> ```
> The `isaspid` template generates a working IdentityServer with ASP.NET Identity, Razor Pages UI (login, logout, consent), and a `Config.cs` — structurally very similar to what we build below. The template includes extra pages (consent, device flow, grants management) that you can strip out. We build it manually here for learning purposes.

---

## NuGet Package Setup

### Update Directory.Packages.props

Add the authentication packages to the centralized package management file:

```xml
<!-- C:\projects\DDD\DDD\Directory.Packages.props -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <!-- Existing packages... -->

    <!-- Authentication & Authorization -->
    <PackageVersion Include="Duende.IdentityServer" Version="7.0.7" />
    <PackageVersion Include="Duende.IdentityServer.AspNetIdentity" Version="7.0.7" />
    <PackageVersion Include="Duende.IdentityServer.EntityFramework" Version="7.0.7" />
    <PackageVersion Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="9.0.3" />
    <PackageVersion Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="9.0.3" />
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.0.3" />
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="9.0.3" />
  </ItemGroup>
</Project>
```

### Create Identity.WebApi.csproj

```xml
<!-- C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Identity.WebApi.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Duende.IdentityServer" />
    <PackageReference Include="Duende.IdentityServer.AspNetIdentity" />
    <PackageReference Include="Duende.IdentityServer.EntityFramework" />
    <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\ServiceDefaults\Aspire.ServiceDefaults.csproj" />
  </ItemGroup>

</Project>
```

---

## Database Context and Models

### ApplicationUser Model

Start with a basic user model extending `IdentityUser`. You can add custom properties later (e.g., FirstName, LastName, Department):

```csharp
// C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Models\ApplicationUser.cs
namespace Identity.WebApi.Models;

using Microsoft.AspNetCore.Identity;

/// <summary>
/// Represents a user in the identity system.
/// Extends IdentityUser to allow future custom properties.
/// </summary>
public class ApplicationUser : IdentityUser
{
    // Future extensions:
    // public string? FirstName { get; set; }
    // public string? LastName { get; set; }
}
```

### IdentityDbContext

This context is used for ASP.NET Core Identity tables only. Duende IdentityServer uses separate contexts (ConfigurationDbContext and PersistedGrantDbContext) configured via the EF stores:

```csharp
// C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Data\IdentityDbContext.cs
namespace Identity.WebApi.Data;

using Identity.WebApi.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Database context for the Identity bounded context.
/// Contains ASP.NET Core Identity tables and DataProtection keys.
/// Duende IdentityServer tables are managed separately via ConfigurationDbContext and PersistedGrantDbContext.
/// </summary>
public class IdentityDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Data protection keys for encrypting authentication cookies and tokens.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Customize table names if needed
        // builder.Entity<ApplicationUser>().ToTable("Users");
        // builder.Entity<IdentityRole>().ToTable("Roles");
        // etc.
    }
}
```

**Key Points:**

- **IdentityDbContext<ApplicationUser>**: Provides tables for users, roles, claims, logins
- **IDataProtectionKeyContext**: Stores data protection keys in the database for multi-instance deployments
- **Duende tables**: Managed separately via `AddConfigurationStore()` and `AddOperationalStore()` in Program.cs

---

## Duende IdentityServer Configuration

### IdentityServerConfig.cs

Duende uses a configuration pattern with static classes defining resources, scopes, and clients:

```csharp
// C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Config\IdentityServerConfig.cs
namespace Identity.WebApi.Config;

using Duende.IdentityServer;
using Duende.IdentityServer.Models;

/// <summary>
/// Duende IdentityServer configuration for identity resources, API scopes, and clients.
/// </summary>
public static class IdentityServerConfig
{
    /// <summary>
    /// Identity resources define user identity data that can be requested via scopes.
    /// </summary>
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResources.Email(),
        new IdentityResource("roles", "User roles", new[] { "role" })
    ];

    /// <summary>
    /// API scopes define permissions that clients can request for accessing APIs.
    /// </summary>
    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new ApiScope("scheduling_api", "Scheduling API"),
        new ApiScope("billing_api", "Billing API")
    ];

    /// <summary>
    /// Clients represent applications that can request tokens from this authorization server.
    /// </summary>
    public static IEnumerable<Client> Clients =>
    [
        // Scheduling API Client (Confidential - has client secret)
        new Client
        {
            ClientId = "scheduling-api",
            ClientName = "Scheduling API",
            ClientSecrets = { new Secret("scheduling-secret".Sha256()) },

            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,

            RedirectUris = { "https://localhost:7001/signin-oidc" },
            PostLogoutRedirectUris = { "https://localhost:7001/signout-callback-oidc" },

            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId,
                IdentityServerConstants.StandardScopes.Profile,
                IdentityServerConstants.StandardScopes.Email,
                "roles",
                "scheduling_api"
            },

            AllowOfflineAccess = true // Enable refresh tokens
        },

        // Billing API Client (Confidential - has client secret)
        new Client
        {
            ClientId = "billing-api",
            ClientName = "Billing API",
            ClientSecrets = { new Secret("billing-secret".Sha256()) },

            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,

            RedirectUris = { "https://localhost:7002/signin-oidc" },
            PostLogoutRedirectUris = { "https://localhost:7002/signout-callback-oidc" },

            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId,
                IdentityServerConstants.StandardScopes.Profile,
                IdentityServerConstants.StandardScopes.Email,
                "roles",
                "billing_api"
            },

            AllowOfflineAccess = true // Enable refresh tokens
        },

        // Angular SPA Client (Public - no client secret, PKCE required)
        new Client
        {
            ClientId = "angular-spa",
            ClientName = "Angular SPA",

            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,
            RequireClientSecret = false, // Public client (SPA can't keep secrets)

            RedirectUris =
            {
                "https://localhost:7003/callback",
                "https://localhost:7003/silent-refresh.html"
            },
            PostLogoutRedirectUris = { "https://localhost:7003/" },
            AllowedCorsOrigins = { "https://localhost:7003" },

            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId,
                IdentityServerConstants.StandardScopes.Profile,
                IdentityServerConstants.StandardScopes.Email,
                "roles",
                "scheduling_api",
                "billing_api"
            },

            AllowOfflineAccess = true // Enable refresh tokens
        }
    ];
}
```

### Program.cs

This is the heart of the authorization server. We configure ASP.NET Core Identity, Duende IdentityServer, and Razor Pages:

```csharp
// C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Program.cs
using Identity.WebApi.Config;
using Identity.WebApi.Data;
using Identity.WebApi.Models;
using Identity.WebApi.SeedData;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddRazorPages();

// Database connection
var connectionString = builder.Configuration.GetConnectionString("IdentityDb")
    ?? throw new InvalidOperationException("Connection string 'IdentityDb' not found.");

var migrationsAssembly = typeof(Program).Assembly.GetName().Name;

// ASP.NET Core Identity
builder.Services.AddDbContext<IdentityDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Password settings (relax for development, tighten for production)
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequiredLength = 6;
    options.Password.RequiredUniqueChars = 1;

    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User settings
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false; // Disable for dev
})
.AddEntityFrameworkStores<IdentityDbContext>()
.AddDefaultTokenProviders();

// Data Protection (for cookie encryption across multiple instances)
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<IdentityDbContext>()
    .SetApplicationName("DDD.Identity");

// Duende IdentityServer
builder.Services.AddIdentityServer(options =>
{
    options.EmitStaticAudienceClaim = true; // Include 'aud' claim in tokens

    options.Events.RaiseErrorEvents = true;
    options.Events.RaiseInformationEvents = true;
    options.Events.RaiseFailureEvents = true;
    options.Events.RaiseSuccessEvents = true;
})
    // Integrate with ASP.NET Core Identity
    .AddAspNetIdentity<ApplicationUser>()

    // Configuration store (clients, resources, scopes)
    .AddConfigurationStore(options =>
    {
        options.ConfigureDbContext = b =>
            b.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly));
    })

    // Operational store (tokens, consents, codes)
    .AddOperationalStore(options =>
    {
        options.ConfigureDbContext = b =>
            b.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly));

        // Enable automatic token cleanup
        options.EnableTokenCleanup = true;
        options.TokenCleanupInterval = 3600; // seconds (1 hour)
    });
    // Note: Duende v7+ has automatic key management built in — no need for AddDevelopmentSigningCredential().
    // Keys are generated, rotated, and stored automatically.

// Add hosted service for seeding data
builder.Services.AddHostedService<IdentitySeedData>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Authentication & Authorization
app.UseAuthentication();
app.UseIdentityServer(); // Duende IdentityServer middleware
app.UseAuthorization();

app.MapRazorPages();

app.Run();
```

**Key Configuration Sections Explained:**

1. **Endpoints:**
   - Duende IdentityServer automatically registers standard OIDC endpoints:
   - `/connect/authorize`: Where users are redirected to log in
   - `/connect/token`: Where clients exchange authorization codes for access tokens
   - `/connect/userinfo`: Returns user profile information
   - `/connect/endsession`: Logs out the user

2. **Flows:**
   - **Authorization Code Flow**: Most secure flow. Client redirects user to login, user authenticates, server returns authorization code, client exchanges code for token
   - **Refresh Token Flow**: Allows clients to get new access tokens without re-authenticating (via `AllowOfflineAccess = true`)

3. **Access Token Types — JWT vs Reference:**

   By default, Duende IdentityServer issues **JWT access tokens** (self-contained). This is the industry standard because:
   - APIs validate tokens locally (check signature + expiry) — no network call to the auth server
   - The auth server doesn't become a bottleneck or single point of failure
   - Scales effortlessly — works even if the auth server is temporarily down

   The trade-off is that JWTs can't be instantly revoked — they remain valid until they expire. Mitigate this with short lifetimes (5-15 minutes) paired with refresh tokens.

   | Concern | JWT (default) | Reference Token |
   |---------|--------------|-----------------|
   | Validation | Local (signature + expiry check) | Network call to introspection endpoint on every request |
   | Auth server load | None after issuing | Hit on every API request |
   | Auth server availability | API works independently | Auth server down = all APIs down |
   | Revocation | Wait for expiry (use short lifetimes) | Instant (delete from DB) |
   | Scalability | Excellent | Auth server becomes bottleneck |

   > **When to use reference tokens:** High-security environments where instant revocation is non-negotiable, low-traffic internal APIs, or when token size is a concern. Configure per-client with `AccessTokenType = AccessTokenType.Reference`.

4. **Database Stores:**
   - **ConfigurationStore**: Stores clients, identity resources, API scopes
   - **OperationalStore**: Stores refresh tokens, authorization codes, device codes, user consents (and reference access tokens if configured per-client)
   - Both use EF Core with separate DbContexts

---

## Razor Pages Login UI

### Shared Layout

```cshtml
@* C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Pages\Shared\_Layout.cshtml *@
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewData["Title"] - DDD Identity Server</title>
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css" />
</head>
<body>
    <div class="container">
        <main role="main" class="pb-3">
            @RenderBody()
        </main>
    </div>
    @RenderSection("Scripts", required: false)
</body>
</html>
```

### _ViewStart.cshtml

```cshtml
@* C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Pages\_ViewStart.cshtml *@
@{
    Layout = "_Layout";
}
```

### _ViewImports.cshtml

```cshtml
@* C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Pages\_ViewImports.cshtml *@
@using Identity.WebApi
@namespace Identity.WebApi.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

### _ValidationScriptsPartial.cshtml

```cshtml
@* C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Pages\Shared\_ValidationScriptsPartial.cshtml *@
<script src="https://cdn.jsdelivr.net/npm/jquery@3.7.1/dist/jquery.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/jquery-validation@1.21.0/dist/jquery.validate.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/jquery-validation-unobtrusive@4.0.0/dist/jquery.validate.unobtrusive.min.js"></script>
```

### Login Page (Razor)

```cshtml
@* C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Pages\Account\Login.cshtml *@
@page
@model Identity.WebApi.Pages.Account.LoginModel
@{
    ViewData["Title"] = "Log in";
}

<div class="row justify-content-center mt-5">
    <div class="col-md-6 col-lg-4">
        <div class="card">
            <div class="card-body">
                <h1 class="card-title text-center">@ViewData["Title"]</h1>
                <hr />

                <form method="post">
                    <div asp-validation-summary="ModelOnly" class="text-danger"></div>

                    <div class="mb-3">
                        <label asp-for="Input.Email" class="form-label"></label>
                        <input asp-for="Input.Email" class="form-control" autocomplete="username" />
                        <span asp-validation-for="Input.Email" class="text-danger"></span>
                    </div>

                    <div class="mb-3">
                        <label asp-for="Input.Password" class="form-label"></label>
                        <input asp-for="Input.Password" class="form-control" autocomplete="current-password" />
                        <span asp-validation-for="Input.Password" class="text-danger"></span>
                    </div>

                    <div class="mb-3">
                        <div class="form-check">
                            <input asp-for="Input.RememberMe" class="form-check-input" />
                            <label asp-for="Input.RememberMe" class="form-check-label"></label>
                        </div>
                    </div>

                    <button type="submit" class="btn btn-primary w-100">Log in</button>

                    <div class="mt-3 text-center">
                        <a asp-page="./Register" asp-route-returnUrl="@Model.ReturnUrl">Register as a new user</a>
                    </div>
                </form>
            </div>
        </div>
    </div>
</div>

@section Scripts {
    <partial name="_ValidationScriptsPartial" />
}
```

### Login Page (Code-Behind)

```csharp
// C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Pages\Account\Login.cshtml.cs
namespace Identity.WebApi.Pages.Account;

using System.ComponentModel.DataAnnotations;
using Identity.WebApi.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(SignInManager<ApplicationUser> signInManager, ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public string? ReturnUrl { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = default!;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = default!;

        [Display(Name = "Remember me?")]
        public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            ModelState.AddModelError(string.Empty, ErrorMessage);
        }

        returnUrl ??= Url.Content("~/");

        // Clear the existing external cookie to ensure a clean login process
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        if (ModelState.IsValid)
        {
            // This doesn't count login failures towards account lockout
            // To enable password failures to trigger account lockout, set lockoutOnFailure: true
            var result = await _signInManager.PasswordSignInAsync(
                Input.Email,
                Input.Password,
                Input.RememberMe,
                lockoutOnFailure: false);

            if (result.Succeeded)
            {
                _logger.LogInformation("User logged in.");
                return LocalRedirect(returnUrl);
            }
            if (result.RequiresTwoFactor)
            {
                // Future: redirect to 2FA page
                return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, Input.RememberMe });
            }
            if (result.IsLockedOut)
            {
                _logger.LogWarning("User account locked out.");
                return RedirectToPage("./Lockout");
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return Page();
            }
        }

        // If we got this far, something failed, redisplay form
        return Page();
    }
}
```

**Key Points:**

- **ReturnUrl**: Preserved across the login flow. After successful login, user is redirected back to the OIDC authorization endpoint, which then redirects to the client application
- **SignInManager**: ASP.NET Core Identity's built-in service for authentication
- **Lockout**: Disabled for now (set `lockoutOnFailure: true` in production)

### Register Page (Razor)

```cshtml
@* C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Pages\Account\Register.cshtml *@
@page
@model Identity.WebApi.Pages.Account.RegisterModel
@{
    ViewData["Title"] = "Register";
}

<div class="row justify-content-center mt-5">
    <div class="col-md-6 col-lg-4">
        <div class="card">
            <div class="card-body">
                <h1 class="card-title text-center">@ViewData["Title"]</h1>
                <hr />

                <form method="post">
                    <div asp-validation-summary="ModelOnly" class="text-danger"></div>

                    <div class="mb-3">
                        <label asp-for="Input.Email" class="form-label"></label>
                        <input asp-for="Input.Email" class="form-control" autocomplete="username" />
                        <span asp-validation-for="Input.Email" class="text-danger"></span>
                    </div>

                    <div class="mb-3">
                        <label asp-for="Input.Password" class="form-label"></label>
                        <input asp-for="Input.Password" class="form-control" autocomplete="new-password" />
                        <span asp-validation-for="Input.Password" class="text-danger"></span>
                    </div>

                    <div class="mb-3">
                        <label asp-for="Input.ConfirmPassword" class="form-label"></label>
                        <input asp-for="Input.ConfirmPassword" class="form-control" autocomplete="new-password" />
                        <span asp-validation-for="Input.ConfirmPassword" class="text-danger"></span>
                    </div>

                    <button type="submit" class="btn btn-primary w-100">Register</button>

                    <div class="mt-3 text-center">
                        <a asp-page="./Login" asp-route-returnUrl="@Model.ReturnUrl">Already have an account?</a>
                    </div>
                </form>
            </div>
        </div>
    </div>
</div>

@section Scripts {
    <partial name="_ValidationScriptsPartial" />
}
```

### Register Page (Code-Behind)

```csharp
// C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Pages\Account\Register.cshtml.cs
namespace Identity.WebApi.Pages.Account;

using System.ComponentModel.DataAnnotations;
using Identity.WebApi.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class RegisterModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<RegisterModel> _logger;

    public RegisterModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<RegisterModel> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = default!;

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = default!;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = default!;
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        if (ModelState.IsValid)
        {
            var user = new ApplicationUser { UserName = Input.Email, Email = Input.Email };
            var result = await _userManager.CreateAsync(user, Input.Password);

            if (result.Succeeded)
            {
                _logger.LogInformation("User created a new account with password.");

                // Assign default "User" role
                await _userManager.AddToRoleAsync(user, "User");

                // Auto sign-in after registration
                await _signInManager.SignInAsync(user, isPersistent: false);
                return LocalRedirect(returnUrl);
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        // If we got this far, something failed, redisplay form
        return Page();
    }
}
```

### Logout Page

```cshtml
@* C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Pages\Account\Logout.cshtml *@
@page
@model Identity.WebApi.Pages.Account.LogoutModel
@{
    ViewData["Title"] = "Log out";
}

<header>
    <h1>@ViewData["Title"]</h1>
    <p>You have successfully logged out of the application.</p>
</header>
```

```csharp
// C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Pages\Account\Logout.cshtml.cs
namespace Identity.WebApi.Pages.Account;

using Identity.WebApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(SignInManager<ApplicationUser> signInManager, ILogger<LogoutModel> logger)
    {
        _signInManager = signInManager;
        _logger = logger;
    }

    public async Task<IActionResult> OnPost(string? returnUrl = null)
    {
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User logged out.");

        if (returnUrl != null)
        {
            return LocalRedirect(returnUrl);
        }
        else
        {
            return RedirectToPage();
        }
    }
}
```

---

## Seed Data

Create test users, roles, and register OAuth clients for development. With Duende, we seed configuration data directly into the EF stores:

```csharp
// C:\projects\DDD\DDD\WebApplications\Identity.WebApi\SeedData\IdentitySeedData.cs
namespace Identity.WebApi.SeedData;

using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Identity.WebApi.Config;
using Identity.WebApi.Data;
using Identity.WebApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Seeds the identity database with test users, roles, and OAuth clients.
/// Runs once on application startup in development environment.
/// </summary>
public class IdentitySeedData : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostEnvironment _environment;

    public IdentitySeedData(IServiceProvider serviceProvider, IHostEnvironment environment)
    {
        _serviceProvider = serviceProvider;
        _environment = environment;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Only seed in development
        if (!_environment.IsDevelopment())
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();

        // Ensure databases are created
        var identityContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identityContext.Database.EnsureCreatedAsync(cancellationToken);

        var configurationContext = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
        await configurationContext.Database.EnsureCreatedAsync(cancellationToken);

        var persistedGrantContext = scope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>();
        await persistedGrantContext.Database.EnsureCreatedAsync(cancellationToken);

        await SeedRolesAsync(scope.ServiceProvider);
        await SeedUsersAsync(scope.ServiceProvider);
        await SeedIdentityServerConfigurationAsync(scope.ServiceProvider);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedRolesAsync(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        string[] roles = { "Admin", "User", "Doctor", "Nurse" };

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private static async Task SeedUsersAsync(IServiceProvider serviceProvider)
    {
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Admin user
        var adminEmail = "admin@test.com";
        if (await userManager.FindByEmailAsync(adminEmail) == null)
        {
            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(admin, "Admin123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, "Admin");
            }
        }

        // Regular user
        var userEmail = "user@test.com";
        if (await userManager.FindByEmailAsync(userEmail) == null)
        {
            var user = new ApplicationUser
            {
                UserName = userEmail,
                Email = userEmail,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(user, "User123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, "User");
            }
        }

        // Doctor user
        var doctorEmail = "doctor@test.com";
        if (await userManager.FindByEmailAsync(doctorEmail) == null)
        {
            var doctor = new ApplicationUser
            {
                UserName = doctorEmail,
                Email = doctorEmail,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(doctor, "Doctor123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(doctor, "Doctor");
            }
        }
    }

    private static async Task SeedIdentityServerConfigurationAsync(IServiceProvider serviceProvider)
    {
        var context = serviceProvider.GetRequiredService<ConfigurationDbContext>();

        // Seed Identity Resources
        if (!await context.IdentityResources.AnyAsync())
        {
            foreach (var resource in IdentityServerConfig.IdentityResources)
            {
                context.IdentityResources.Add(resource.ToEntity());
            }
            await context.SaveChangesAsync();
        }

        // Seed API Scopes
        if (!await context.ApiScopes.AnyAsync())
        {
            foreach (var scope in IdentityServerConfig.ApiScopes)
            {
                context.ApiScopes.Add(scope.ToEntity());
            }
            await context.SaveChangesAsync();
        }

        // Seed Clients
        if (!await context.Clients.AnyAsync())
        {
            foreach (var client in IdentityServerConfig.Clients)
            {
                context.Clients.Add(client.ToEntity());
            }
            await context.SaveChangesAsync();
        }
    }
}
```

**Seeding Strategy Explained:**

- **Identity Resources**: Define user identity claims (openid, profile, email, roles)
- **API Scopes**: Define API permissions (scheduling_api, billing_api)
- **Clients**: Define applications that can request tokens (APIs, SPAs)
- **ToEntity()**: Extension method from Duende that converts configuration models to EF entities
- All configuration is stored in the `ConfigurationDbContext` tables

---

## Aspire Integration

Add the Identity.WebApi project to the Aspire orchestrator and reference it from the other services:

```csharp
// C:\projects\DDD\DDD\Aspire.AppHost\Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// RabbitMQ
var messagingPassword = builder.AddParameter("messaging-password");
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin()
    .WithDataVolume();

// Identity Server (Authorization Server)
var identityApi = builder.AddProject<Projects.Identity_WebApi>("identity-webapi")
    .WithHttpsEndpoint(port: 7010, name: "identity-https");

// Scheduling API (fixed port — OIDC redirect URIs must match exactly)
var schedulingApi = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-webapi")
    .WithHttpsEndpoint(port: 7001, name: "scheduling-https")
    .WithReference(messaging)
    .WithReference(identityApi)
    .WaitFor(messaging);

// Billing API (fixed port — OIDC redirect URIs must match exactly)
var billingApi = builder.AddProject<Projects.Billing_WebApi>("billing-webapi")
    .WithHttpsEndpoint(port: 7002, name: "billing-https")
    .WithReference(messaging)
    .WithReference(identityApi)
    .WaitFor(messaging);

// Angular SPA (references all APIs and Identity)
builder.AddJavaScriptApp("scheduling-angularapp", "../Frontend/Angular/Scheduling.AngularApp", "start-aspire")
    .WithReference(schedulingApi)
    .WithReference(billingApi)
    .WithReference(identityApi)
    .WithHttpsEndpoint(port: 7003, env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

**What This Does:**

- **WithReference(identityApi)**: Injects the Identity server URL as an environment variable into the API projects
- **Fixed ports**: All services use fixed HTTPS ports because OIDC requires exact redirect URI matching in the client configuration (`Config.cs`). If ports changed dynamically, the auth server would reject redirects.
  - Identity.WebApi: `7010`
  - Scheduling.WebApi: `7001`
  - Billing.WebApi: `7002`
  - Angular SPA: `7003`

### Connection String Configuration

Store the connection string in **User Secrets** (not `appsettings.json`) to keep credentials out of source control.

#### Shared User Secrets

Identity.WebApi should reference the **same `UserSecretsId`** as the Aspire AppHost, so secrets are shared across all projects:

```xml
<!-- WebApplications/Identity.WebApi/Identity.WebApi.csproj -->
<PropertyGroup>
  <UserSecretsId>12d3119a-ea1f-43ad-b1f3-6c5072eb7dcd</UserSecretsId>
</PropertyGroup>
```

#### Set the Connection String

```bash
dotnet user-secrets set "ConnectionStrings:IdentityDb" "Data Source=YOUR_SERVER;Initial Catalog=IdentityDb;Integrated Security=true;TrustServerCertificate=True" --project Aspire.AppHost
```

This keeps the connection string available to Identity.WebApi whether running via Aspire or standalone.

> **Note:** Do NOT add `MultipleActiveResultSets=true` (MARS). MARS disables EF Core savepoints on transactions — see [Phase 2 docs](../phase-2-ef-core/03-database-migrations.md#step-2-add-connection-string-via-user-secrets) for details.

---

## Testing the Setup

### 1. Create Database Migrations

Duende IdentityServer requires separate migrations for the configuration and operational stores:

```bash
cd C:\projects\DDD\DDD\WebApplications\Identity.WebApi

# Identity tables (ASP.NET Core Identity)
dotnet ef migrations add InitialIdentity -c IdentityDbContext

# IdentityServer configuration tables (Clients, Scopes, Resources)
dotnet ef migrations add InitialIdentityServerConfigurationDbMigration -c ConfigurationDbContext

# IdentityServer operational tables (Tokens, Grants, Codes)
dotnet ef migrations add InitialIdentityServerPersistedGrantDbMigration -c PersistedGrantDbContext

# Apply all migrations
dotnet ef database update -c IdentityDbContext
dotnet ef database update -c ConfigurationDbContext
dotnet ef database update -c PersistedGrantDbContext
```

This creates three sets of tables:

**Identity Tables (IdentityDbContext):**
- AspNetUsers, AspNetRoles, AspNetUserRoles, AspNetUserClaims, AspNetRoleClaims
- DataProtectionKeys

**Configuration Tables (ConfigurationDbContext):**
- Clients, ClientScopes, ClientSecrets, ClientGrantTypes, ClientRedirectUris, ClientPostLogoutRedirectUris, ClientCorsOrigins
- IdentityResources, IdentityResourceClaims
- ApiScopes, ApiScopeClaims
- ApiResources, ApiResourceScopes, ApiResourceClaims

**Operational Tables (PersistedGrantDbContext):**
- PersistedGrants (stores refresh tokens, authorization codes, device codes, user consents — and reference access tokens if configured per-client)
- DeviceCodes
- Keys (signing keys for JWT signature validation)
- ServerSideSessions

### 2. Run the Application

```bash
cd C:\projects\DDD\DDD\Aspire.AppHost
dotnet run
```

The Aspire dashboard should show the Identity.WebApi project running on `https://localhost:7010`.

### 3. Test Discovery Endpoint

Navigate to:
```
https://localhost:7010/.well-known/openid-configuration
```

You should see a JSON response like:

```json
{
  "issuer": "https://localhost:7010",
  "authorization_endpoint": "https://localhost:7010/connect/authorize",
  "token_endpoint": "https://localhost:7010/connect/token",
  "userinfo_endpoint": "https://localhost:7010/connect/userinfo",
  "end_session_endpoint": "https://localhost:7010/connect/endsession",
  "jwks_uri": "https://localhost:7010/.well-known/openid-configuration/jwks",
  "grant_types_supported": [
    "authorization_code",
    "refresh_token"
  ],
  "response_types_supported": [
    "code"
  ],
  "scopes_supported": [
    "openid",
    "email",
    "profile",
    "roles",
    "scheduling_api",
    "billing_api",
    "offline_access"
  ],
  "token_endpoint_auth_methods_supported": [
    "client_secret_basic",
    "client_secret_post"
  ],
  "claims_supported": [
    "sub",
    "name",
    "email",
    "role"
  ],
  "code_challenge_methods_supported": [
    "S256"
  ]
}
```

**Discovery Document Fields Explained:**

- **issuer**: The unique identifier for this authorization server
- **authorization_endpoint**: Where users are redirected to log in
- **token_endpoint**: Where clients exchange codes for tokens
- **userinfo_endpoint**: Returns user profile info
- **end_session_endpoint**: Logout endpoint
- **jwks_uri**: Public keys for verifying token signatures
- **grant_types_supported**: OAuth flows this server supports
- **scopes_supported**: Available scopes clients can request
- **code_challenge_methods_supported**: PKCE methods (S256 = SHA-256)

### 4. Test Login Page

Navigate to:
```
https://localhost:7010/Account/Login
```

You should see the login form. You can verify it renders correctly and even log in with the seeded users:
- **admin@test.com** / **Admin123!**
- **user@test.com** / **User123!**
- **doctor@test.com** / **Doctor123!**

> **Note:** Logging in directly on the IdentityServer works (it creates a session cookie on the auth server), but you won't be redirected anywhere useful. In a real flow, the login page is reached via an OIDC redirect from a client (e.g., Angular or Blazor). The client passes a `ReturnUrl` so IdentityServer knows where to send the user after login. The full end-to-end flow is wired up in docs 03-05.

### 5. Verify Database

Open SQL Server Management Studio or Azure Data Studio and connect to `(localdb)\mssqllocaldb`.

Check the `IdentityDb` database for:

**Identity Tables:**
- **AspNetUsers**: Should contain 3 users
- **AspNetRoles**: Should contain 4 roles (Admin, User, Doctor, Nurse)
- **AspNetUserRoles**: Should link users to their roles

**Configuration Tables:**
- **Clients**: Should contain 3 registered clients (scheduling-api, billing-api, angular-spa)
- **ClientScopes**: Should contain scope assignments for each client
- **ClientRedirectUris**: Should contain redirect URIs
- **IdentityResources**: Should contain 4 identity resources (openid, profile, email, roles)
- **ApiScopes**: Should contain 2 API scopes (scheduling_api, billing_api)

**Operational Tables:**
- **PersistedGrants**: Stores refresh tokens, authorization codes, device codes, user consents (and reference access tokens if configured per-client)
- **Keys**: Contains signing keys for JWT signature validation

---

## Next Steps

You now have a fully functional OAuth 2.0/OpenID Connect authorization server using Duende IdentityServer. The next steps are:

1. **Create Shared Authentication Infrastructure** (doc 03): Build a `BuildingBlocks.Authentication` library with reusable JWT validation, ICurrentUser abstraction, and authorization policies
2. **Configure APIs** (doc 04): Add OIDC authentication to Scheduling.WebApi and Billing.WebApi
3. **Implement Authorization Policies** (doc 05): Define role-based and claims-based policies
4. **Integrate with Domain Layer** (doc 06): Use ICurrentUser in commands and domain events

---

## Summary

In this document, you:

- Created an **Identity.WebApi** project as the centralized authorization server
- Configured **ASP.NET Core Identity** for user management
- Integrated **Duende IdentityServer** for OAuth 2.0 / OIDC protocol implementation
- Defined **configuration via code** (IdentityServerConfig.cs)
- Built **Razor Pages** login/register UI
- Seeded **test users, roles, and OAuth clients** into EF Core stores
- Registered the authorization server with **.NET Aspire**
- Tested the **discovery endpoint** and **login flow**

This authorization server will issue access tokens that our APIs validate in the next documents. Duende IdentityServer provides industry-standard OAuth 2.0/OIDC implementation with excellent documentation and community support.

---

> **Previous:** [01-auth-overview.md](./01-auth-overview.md) - Authentication & Authorization Overview
> **Next:** [03-shared-auth-infrastructure.md](./03-shared-auth-infrastructure.md) - Building the shared authentication BuildingBlock
