# Phase 8.2 - Authorization Server Setup with OpenIddict

This document walks through setting up a centralized OAuth 2.0/OpenID Connect authorization server using ASP.NET Core Identity and OpenIddict. This server will be the single source of truth for authentication and issue access tokens to our microservices.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Why OpenIddict?](#why-openiddict)
3. [Project Structure](#project-structure)
4. [NuGet Package Setup](#nuget-package-setup)
5. [Database Context and Models](#database-context-and-models)
6. [OpenIddict Configuration](#openiddict-configuration)
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
│  │ OpenIddict Server (OAuth 2.0 / OIDC)                     │  │
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
│  │ IdentityDbContext (Separate Database)                    │  │
│  │  - AspNetUsers, AspNetRoles                              │  │
│  │  - OpenIddictApplications, OpenIddictTokens              │  │
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
- **OpenIddict**: Implements OAuth 2.0 and OpenID Connect protocol on top of Identity
- **Authorization Code Flow**: Most secure flow for server-side applications and SPAs with PKCE
- **Reference Tokens**: Tokens are opaque identifiers stored in the database (not self-contained JWTs). This allows instant revocation and better security
- **Separate Database**: Identity has its own bounded context with its own database (IdentityDb)

---

## Why OpenIddict?

**OpenIddict vs IdentityServer:**

| Feature | OpenIddict | IdentityServer |
|---------|-----------|----------------|
| License | Free (Apache 2.0) | Commercial for production |
| Integration | Built-in ASP.NET Core Identity integration | Requires additional setup |
| Database | EF Core out-of-the-box | Custom stores or EF Core |
| Learning Curve | Moderate | Steeper |
| Community | Growing | Mature |
| Token Storage | Reference tokens supported | JWT-focused |

For a learning project and small-to-medium production scenarios, OpenIddict is an excellent choice. It integrates seamlessly with ASP.NET Core Identity and supports all the OAuth 2.0 flows we need.

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
    │   │   └── _Layout.cshtml
    │   └── Account/
    │       ├── Login.cshtml
    │       ├── Login.cshtml.cs
    │       ├── Register.cshtml
    │       ├── Register.cshtml.cs
    │       ├── Logout.cshtml
    │       └── Logout.cshtml.cs
    ├── SeedData/
    │   └── IdentitySeedData.cs
    └── Identity.WebApi.csproj
```

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
    <PackageVersion Include="OpenIddict.AspNetCore" Version="6.3.0" />
    <PackageVersion Include="OpenIddict.EntityFrameworkCore" Version="6.3.0" />
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
    <PackageReference Include="OpenIddict.AspNetCore" />
    <PackageReference Include="OpenIddict.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
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
    // public string? Department { get; set; }
}
```

### IdentityDbContext

This context combines ASP.NET Core Identity tables with OpenIddict entity stores:

```csharp
// C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Data\IdentityDbContext.cs
namespace Identity.WebApi.Data;

using Identity.WebApi.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Database context for the Identity bounded context.
/// Combines ASP.NET Core Identity tables, OpenIddict stores, and DataProtection keys.
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
- **OpenIddict tables**: Added automatically when we configure OpenIddict to use EF Core stores

---

## OpenIddict Configuration

### Program.cs

This is the heart of the authorization server. We configure ASP.NET Core Identity, OpenIddict server, and Razor Pages:

```csharp
// C:\projects\DDD\DDD\WebApplications\Identity.WebApi\Program.cs
using Identity.WebApi.Data;
using Identity.WebApi.Models;
using Identity.WebApi.SeedData;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddRazorPages();

// Database connection
var connectionString = builder.Configuration.GetConnectionString("IdentityDb")
    ?? throw new InvalidOperationException("Connection string 'IdentityDb' not found.");

builder.Services.AddDbContext<IdentityDbContext>(options =>
{
    options.UseSqlServer(connectionString);

    // Configure OpenIddict to use EF Core stores
    options.UseOpenIddict();
});

// ASP.NET Core Identity
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

// OpenIddict Server
builder.Services.AddOpenIddict()
    // Register Entity Framework Core stores
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
            .UseDbContext<IdentityDbContext>();
    })

    // Register ASP.NET Core OpenIddict server components
    .AddServer(options =>
    {
        // Enable the authorization, token, userinfo, and logout endpoints
        options.SetAuthorizationEndpointUris("/connect/authorize")
               .SetTokenEndpointUris("/connect/token")
               .SetUserinfoEndpointUris("/connect/userinfo")
               .SetLogoutEndpointUris("/connect/logout");

        // Enable the authorization code flow
        // This is the recommended flow for server-side web apps and SPAs
        options.AllowAuthorizationCodeFlow()
               .AllowRefreshTokenFlow();

        // Register the signing and encryption credentials
        // For development: use a temporary development certificate
        // For production: use a real certificate from a trusted CA
        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();

        // Register the ASP.NET Core host and configure the ASP.NET Core-specific options
        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableUserinfoEndpointPassthrough()
               .EnableLogoutEndpointPassthrough();

        // Use reference tokens instead of self-contained JWTs
        // Reference tokens are opaque identifiers stored in the database
        // This allows instant revocation and better security
        options.UseReferenceAccessTokens()
               .UseReferenceRefreshTokens();

        // Disable access token encryption for development (easier debugging)
        // Enable in production for better security
        if (builder.Environment.IsDevelopment())
        {
            options.DisableAccessTokenEncryption();
        }

        // Register scopes
        options.RegisterScopes(
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Roles,
            "scheduling-api",
            "billing-api"
        );
    })

    // Register the OpenIddict validation components (for local token validation)
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

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
app.UseAuthorization();

app.MapRazorPages();

app.Run();
```

**Key Configuration Sections Explained:**

1. **Endpoints:**
   - `/connect/authorize`: Where users are redirected to log in
   - `/connect/token`: Where clients exchange authorization codes for access tokens
   - `/connect/userinfo`: Returns user profile information
   - `/connect/logout`: Logs out the user

2. **Flows:**
   - **Authorization Code Flow**: Most secure flow. Client redirects user to login, user authenticates, server returns authorization code, client exchanges code for token
   - **Refresh Token Flow**: Allows clients to get new access tokens without re-authenticating

3. **Reference Tokens:**
   - Tokens are stored in `OpenIddictTokens` table as opaque identifiers
   - APIs validate tokens by calling back to the authorization server
   - Allows instant revocation (just delete from database)
   - Better security than self-contained JWTs (can't be decoded)

4. **Scopes:**
   - `email`, `profile`, `roles`: Standard OIDC scopes
   - `scheduling-api`, `billing-api`: Custom API scopes

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

Create test users and register OAuth clients for development:

```csharp
// C:\projects\DDD\DDD\WebApplications\Identity.WebApi\SeedData\IdentitySeedData.cs
namespace Identity.WebApi.SeedData;

using Identity.WebApi.Data;
using Identity.WebApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

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

        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await context.Database.EnsureCreatedAsync(cancellationToken);

        await SeedRolesAsync(scope.ServiceProvider);
        await SeedUsersAsync(scope.ServiceProvider);
        await SeedClientsAsync(scope.ServiceProvider);
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

    private static async Task SeedClientsAsync(IServiceProvider serviceProvider)
    {
        var manager = serviceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        // Scheduling API client
        if (await manager.FindByClientIdAsync("scheduling-api") == null)
        {
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "scheduling-api",
                ClientSecret = "scheduling-secret",
                DisplayName = "Scheduling API",
                Type = OpenIddictConstants.ClientTypes.Confidential,
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Authorization,
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                    OpenIddictConstants.Permissions.ResponseTypes.Code,
                    OpenIddictConstants.Permissions.Scopes.Email,
                    OpenIddictConstants.Permissions.Scopes.Profile,
                    OpenIddictConstants.Permissions.Scopes.Roles,
                    OpenIddictConstants.Permissions.Prefixes.Scope + "scheduling-api"
                },
                RedirectUris =
                {
                    new Uri("https://localhost:7001/signin-oidc")
                },
                PostLogoutRedirectUris =
                {
                    new Uri("https://localhost:7001/signout-callback-oidc")
                }
            });
        }

        // Billing API client
        if (await manager.FindByClientIdAsync("billing-api") == null)
        {
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "billing-api",
                ClientSecret = "billing-secret",
                DisplayName = "Billing API",
                Type = OpenIddictConstants.ClientTypes.Confidential,
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Authorization,
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                    OpenIddictConstants.Permissions.ResponseTypes.Code,
                    OpenIddictConstants.Permissions.Scopes.Email,
                    OpenIddictConstants.Permissions.Scopes.Profile,
                    OpenIddictConstants.Permissions.Scopes.Roles,
                    OpenIddictConstants.Permissions.Prefixes.Scope + "billing-api"
                },
                RedirectUris =
                {
                    new Uri("https://localhost:7002/signin-oidc")
                },
                PostLogoutRedirectUris =
                {
                    new Uri("https://localhost:7002/signout-callback-oidc")
                }
            });
        }

        // Angular SPA client
        if (await manager.FindByClientIdAsync("angular-spa") == null)
        {
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "angular-spa",
                DisplayName = "Angular SPA",
                Type = OpenIddictConstants.ClientTypes.Public, // No client secret (SPA can't keep secrets)
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Authorization,
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                    OpenIddictConstants.Permissions.ResponseTypes.Code,
                    OpenIddictConstants.Permissions.Scopes.Email,
                    OpenIddictConstants.Permissions.Scopes.Profile,
                    OpenIddictConstants.Permissions.Scopes.Roles,
                    OpenIddictConstants.Permissions.Prefixes.Scope + "scheduling-api",
                    OpenIddictConstants.Permissions.Prefixes.Scope + "billing-api"
                },
                RedirectUris =
                {
                    new Uri("https://localhost:7003/callback"),
                    new Uri("https://localhost:7003/silent-refresh.html")
                },
                PostLogoutRedirectUris =
                {
                    new Uri("https://localhost:7003/")
                },
                Requirements =
                {
                    OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange // PKCE for SPAs
                }
            });
        }
    }
}
```

**Client Registration Explained:**

- **ClientId / ClientSecret**: Credentials for confidential clients (APIs). SPAs are public clients (no secret)
- **Type**: `Confidential` (can keep secrets) vs `Public` (SPAs, native apps)
- **Permissions**: Allowed endpoints, flows, scopes
- **RedirectUris**: Where to redirect after successful login
- **PostLogoutRedirectUris**: Where to redirect after logout
- **PKCE**: Required for SPAs (prevents authorization code interception)

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
    .WithHttpsEndpoint(port: 7010, name: "https");

// Scheduling API (references Identity for authentication)
var schedulingApi = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-webapi")
    .WithReference(messaging)
    .WithReference(identityApi)
    .WaitFor(messaging);

// Billing API (references Identity for authentication)
var billingApi = builder.AddProject<Projects.Billing_WebApi>("billing-webapi")
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
- **Port 7010**: Identity server runs on this port
- APIs and SPA will read this URL to configure OIDC authentication

### Connection String Configuration

Add the IdentityDb connection string to `appsettings.Development.json` or User Secrets:

```json
// C:\projects\DDD\DDD\WebApplications\Identity.WebApi\appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "IdentityDb": "Server=(localdb)\\mssqllocaldb;Database=IdentityDb;Trusted_Connection=True;MultipleActiveResultSets=true"
  }
}
```

---

## Testing the Setup

### 1. Create Database Migration

```bash
cd C:\projects\DDD\DDD\WebApplications\Identity.WebApi
dotnet ef migrations add InitialIdentity
dotnet ef database update
```

This creates:
- ASP.NET Core Identity tables (AspNetUsers, AspNetRoles, etc.)
- OpenIddict tables (OpenIddictApplications, OpenIddictTokens, etc.)
- DataProtectionKeys table

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
  "issuer": "https://localhost:7010/",
  "authorization_endpoint": "https://localhost:7010/connect/authorize",
  "token_endpoint": "https://localhost:7010/connect/token",
  "userinfo_endpoint": "https://localhost:7010/connect/userinfo",
  "end_session_endpoint": "https://localhost:7010/connect/logout",
  "jwks_uri": "https://localhost:7010/.well-known/jwks",
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
    "scheduling-api",
    "billing-api"
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

You should see the login form. Try logging in with the seeded users:
- **admin@test.com** / **Admin123!**
- **user@test.com** / **User123!**
- **doctor@test.com** / **Doctor123!**

### 5. Verify Database

Open SQL Server Management Studio or Azure Data Studio and connect to `(localdb)\mssqllocaldb`.

Check the `IdentityDb` database for:
- **AspNetUsers**: Should contain 3 users
- **AspNetRoles**: Should contain 4 roles (Admin, User, Doctor, Nurse)
- **AspNetUserRoles**: Should link users to their roles
- **OpenIddictApplications**: Should contain 3 registered clients

---

## Next Steps

You now have a fully functional OAuth 2.0/OpenID Connect authorization server. The next steps are:

1. **Create Shared Authentication Infrastructure** (doc 03): Build a `BuildingBlocks.Authentication` library with reusable JWT validation, ICurrentUser abstraction, and authorization policies
2. **Configure APIs** (doc 04): Add OIDC authentication to Scheduling.WebApi and Billing.WebApi
3. **Implement Authorization Policies** (doc 05): Define role-based and claims-based policies
4. **Integrate with Domain Layer** (doc 06): Use ICurrentUser in commands and domain events

---

## Summary

In this document, you:

- Created an **Identity.WebApi** project as the centralized authorization server
- Configured **ASP.NET Core Identity** for user management
- Integrated **OpenIddict** for OAuth 2.0 / OIDC protocol implementation
- Built **Razor Pages** login/register UI
- Seeded **test users, roles, and OAuth clients**
- Registered the authorization server with **.NET Aspire**
- Tested the **discovery endpoint** and **login flow**

This authorization server will issue access tokens that our APIs validate in the next documents.

---

> **Previous:** [01-auth-overview.md](./01-auth-overview.md) - Authentication & Authorization Overview
> **Next:** [03-shared-auth-infrastructure.md](./03-shared-auth-infrastructure.md) - Building the shared authentication BuildingBlock
