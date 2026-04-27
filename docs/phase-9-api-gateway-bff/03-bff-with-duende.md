# Phase 9: BFF with Duende.BFF (CHOSEN IMPLEMENTATION PATH)

> NOTE: This document describes the CHOSEN Phase 9 implementation path for this project — Duende.BFF as the BFF host. It supersedes `02-bff-pattern-optional.md` (which describes a generic YARP + Refit + custom-aggregation BFF) for this codebase. The older doc remains as an alternative reference for non-Duende ecosystems.

## Table of Contents

1. [Why Duende.BFF Over Raw YARP](#1-why-duendebff-over-raw-yarp)
2. [Target Architecture](#2-target-architecture)
3. [BuildingBlocks.Infrastructure.Auth Changes](#3-buildblocksinfrastructureauth-changes)
   - [AddOidcCookieAuth — extend, do not duplicate](#31-addoidccookieauth--extend-do-not-duplicate)
   - [AddJwtBearerAuth — new method](#32-addjwtbearerauth--new-method)
   - [AuthController — stays put with explanatory comment](#33-authcontroller--stays-put-with-explanatory-comment)
4. [Identity.WebApi Changes](#4-identitywebapi-changes)
5. [New Bff.WebApi Project](#5-new-bffwebapi-project)
6. [Scheduling.WebApi and Billing.WebApi Changes](#6-schedulingwebapi-and-billingwebapi-changes)
7. [Aspire.AppHost Changes](#7-aspireapphost-changes)
8. [Angular Changes](#8-angular-changes)
9. [Server-Side Sessions](#9-server-side-sessions)
10. [Logout Semantics](#10-logout-semantics)
11. [Anti-Forgery — Why, Not Just How](#11-anti-forgery--why-not-just-how)
12. [Headers in Flight: X-Requested-With vs X-XSRF-TOKEN](#12-headers-in-flight-x-requested-with-vs-x-xsrf-token)
13. [CORS](#13-cors)
14. [Data Protection](#14-data-protection)
15. [Verification Checklist](#15-verification-checklist)
16. [Out of Scope / Future Work](#16-out-of-scope--future-work)

---

## 1. Why Duende.BFF Over Raw YARP

`02-bff-pattern-optional.md` describes a BFF built from raw YARP + Refit + custom aggregation endpoints. That approach works well in a Duende-free ecosystem. This project already runs Duende IdentityServer (Phase 8), so a different trade-off applies.

| Concern | Raw YARP BFF | Duende.BFF |
|---|---|---|
| OIDC + cookie wiring | Manual (`AddAuthentication`, event hooks, redirect handling) | Built-in — same code already in `AddOidcCookieAuth` |
| Token forwarding to downstream APIs | Manual (`ITokenStore`, custom `DelegatingHandler`) | `RequireAccessToken()` on each proxy route |
| Token refresh | Manual (intercept 401 from resource API, call `/connect/token`, retry) | Handled automatically by Duende.BFF middleware |
| Anti-forgery | Manual (`IAntiforgery`, header check middleware) | Header check baked in; configure the header name once |
| Back-channel logout | Manual (receive POST, find session, invalidate) | `MapBffManagementEndpoints()` registers `/bff/backchannel` |
| Server-side session store | Manual (`ITicketStore`) | `AddServerSideSessions()` — one call |
| New cost | None — the Duende license is already required for IdentityServer | None |

The key insight: every capability listed under "Manual" for raw YARP is code you would have to write, test, and maintain. Duende.BFF ships all of it. Because the license cost already exists, the only real trade-off is an additional NuGet dependency — which is a reasonable price.

---

## 2. Target Architecture

After Phase 9 the public surface of the system collapses to a single host.

```
Browser (Angular SPA, port 7003)
        |
        | cookie (HttpOnly, SameSite=Lax)
        | X-Requested-With: XMLHttpRequest
        | X-XSRF-TOKEN: <token>
        v
+---------------------+
|    Bff.WebApi        |  port 7000 — only public-facing host
|   (Duende.BFF)       |  cookie auth + OIDC client + reverse proxy
+---------------------+
        |                            |
        | Bearer token               | OIDC code flow (browser redirect)
        | (from session store)       |
        v                            v
+------------------+    +------------------+
| Scheduling.WebApi|    | Identity.WebApi  |
| (JWT resource)   |    | (Duende IdSrv)   |
| port 7001        |    | port 7010        |
+------------------+    +------------------+
        |
        | Bearer token
        v
+------------------+
| Billing.WebApi   |
| (JWT resource)   |
| port 7002        |
+------------------+
```

Key changes from Phase 8:

- **Angular no longer calls Scheduling.WebApi or Billing.WebApi directly.** All traffic goes through the BFF.
- **Scheduling.WebApi and Billing.WebApi switch from OIDC cookie auth to JWT bearer auth.** They are no longer client-facing; only the BFF calls them, server-to-server.
- **The BFF holds access tokens in a server-side session store.** The browser never sees a token.
- **Identity.WebApi is unchanged except for client registration** — one new `bff` client replaces the three existing ones.

---

## 3. BuildingBlocks.Infrastructure.Auth Changes

The auth building block lives at `BuildingBlocks/BuildingBlocks.Infrastructure.Auth/AuthExtensions.cs` (currently 205 lines). Two changes are needed: an extension to `AddOidcCookieAuth` and a new `AddJwtBearerAuth` method.

### 3.1 AddOidcCookieAuth — extend, do not duplicate

The method currently has a fixed signature:

```csharp
// Current signature (AuthExtensions.cs, line 22)
public static IServiceCollection AddOidcCookieAuth(
    this IServiceCollection services,
    IConfiguration configuration)
```

Add two optional parameters so the BFF can request additional scopes and instruct the OIDC middleware to save tokens (which Duende.BFF needs to forward them downstream):

```csharp
// Updated signature
public static IServiceCollection AddOidcCookieAuth(
    this IServiceCollection services,
    IConfiguration configuration,
    bool saveTokens = false,
    IEnumerable<string>? additionalScopes = null)
```

Both parameters default to their current values, so every existing call site (`Scheduling.WebApi/Program.cs` line 50, `Billing.WebApi/Program.cs` line 64) keeps working without modification — even though those APIs will eventually switch to `AddJwtBearerAuth`, the default-safety matters during migration.

**Inside the method — two targeted edits:**

Replace line 75:
```csharp
// Before
options.SaveTokens = false;

// After
options.SaveTokens = saveTokens;
```

After line 71 (`options.Scope.Add("roles");`):
```csharp
if (additionalScopes != null)
    foreach (var scope in additionalScopes) options.Scope.Add(scope);
```

Update the comment on line 191 (`SetApplicationName`). The current comment reads `// MUST be same across all APIs`. Replace with:
```csharp
// Shared application name lets multiple processes decrypt the same cookie
// (legacy multi-API setup, or multi-instance BFF). Harmless for a single host.
dataProtection.SetApplicationName("DDD.WebApis");
```

The reason this matters: Phase 8 used a shared application name so that both Scheduling.WebApi and Billing.WebApi could decrypt each other's cookies. In Phase 9 the BFF is the only cookie issuer, so the shared name is no longer strictly necessary — but leaving it in place is harmless and makes the code honest about its history.

**BFF call site:**
```csharp
// Bff.WebApi/Program.cs
builder.Services.AddOidcCookieAuth(builder.Configuration,
    saveTokens: true,
    additionalScopes: ["scheduling_api", "billing_api", "offline_access"]);
```

- `saveTokens: true` — Duende.BFF reads the access token from the authentication ticket to forward it to resource APIs. Without this the token is never stored in the session and `RequireAccessToken()` silently fails.
- `additionalScopes` — the BFF must request the API scopes on behalf of the Angular SPA. `offline_access` enables refresh tokens so sessions survive access token expiry.

### 3.2 AddJwtBearerAuth — new method

Resource APIs (Scheduling.WebApi, Billing.WebApi) no longer perform the OIDC dance. They receive a Bearer token issued by IdentityServer and validate it using the standard JWT bearer middleware. Add this method to the same `AuthExtensions.cs` class:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;

public static IServiceCollection AddJwtBearerAuth(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var authority = configuration["Auth:Authority"]
        ?? throw new InvalidOperationException("Auth:Authority is not configured but required.");
    var audience  = configuration["Auth:Audience"]
        ?? throw new InvalidOperationException("Auth:Audience is not configured but required.");

    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = authority;
                o.Audience  = audience;
                o.TokenValidationParameters.NameClaimType = "name";
                o.TokenValidationParameters.RoleClaimType = "role";
            });

    services.AddHttpContextAccessor();
    services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
    return services;
}
```

Why a new method rather than branching inside the existing one? The two flows have different configuration keys (`Auth:ClientId`/`Auth:ClientSecret` vs `Auth:Audience`), different middleware registrations, and different semantics. Keeping them separate makes each call site self-documenting.

`Auth:Authority` is the same key in both methods. The JWT bearer middleware uses it to fetch the JWKS endpoint (`<authority>/.well-known/openid-configuration`) and validate token signatures automatically — there is no need to hard-code the signing key.

`Auth:Audience` must match the API scope name registered in IdentityServer (`scheduling_api` or `billing_api`). IdentityServer embeds the scope name as the `aud` claim in access tokens when the scope is requested.

Everything downstream of the auth boundary — `[Authorize(Roles=...)]`, `UserValidator<T>`, `ICurrentUser`, role-based 403 mapping in `ValidationErrorWrapper` — is **unchanged**. The JWT bearer middleware places claims on `HttpContext.User` in the same shape the cookie middleware previously did, because both use the same `NameClaimType = "name"` and `RoleClaimType = "role"` configuration.

### 3.3 AuthController — stays put with explanatory comment

`BuildingBlocks/BuildingBlocks.Infrastructure.Auth/AuthController.cs` provides `/auth/login`, `/auth/logout`, and `/auth/current-user`. In Phase 9, the BFF replaces these with Duende's own `/bff/login`, `/bff/logout`, `/bff/user`, and `/bff/back-channel-logout` endpoints. The controller is not deleted because:

1. It is auto-discovered by any host that references the `BuildingBlocks.Infrastructure.Auth` assembly. Deleting it would be a breaking change for any future host that uses `AddOidcCookieAuth` without Duende.BFF.
2. The routes are unreachable from outside in Phase 9 — the BFF only proxies `/api/*` paths; nothing routes to `/auth/*` on the resource APIs.

Add this XML comment at the class level:

```csharp
/// <summary>
/// Reference implementation for OIDC cookie auth endpoints when an API hosts its own
/// cookie session (no BFF in front of it). Pair with <see cref="AuthExtensions.AddOidcCookieAuth"/>.
///
/// NOT used in the current Phase 9 architecture — Duende.BFF provides /bff/login,
/// /bff/logout, /bff/user, /bff/silent-login, /bff/back-channel-logout out of the box,
/// and the Angular SPA targets those endpoints directly.
///
/// Kept here as an alternative reference for hosts that handle their own cookie auth
/// (monoliths, single-API deployments, or scenarios where Duende.BFF is not used).
///
/// Side note: this controller is auto-discovered by any host that references this
/// assembly. On hosts where the "oidc" challenge scheme is not registered (e.g., the
/// resource APIs after Phase 9), the routes still load but will fail at runtime if hit.
/// They are unreachable from the outside because the BFF only proxies /api/* routes.
/// </summary>
```

---

## 4. Identity.WebApi Changes

**File**: `WebApplications/Identity.WebApi/Config/IdentityServerConfig.cs`

The current `Clients` collection has three entries (lines 36-113):

| ClientId | Purpose | Phase 9 fate |
|---|---|---|
| `billing-api` | Billing.WebApi's own OIDC client | Remove — resource APIs no longer do OIDC |
| `scheduling-api` | Scheduling.WebApi's own OIDC client | Remove — resource APIs no longer do OIDC |
| `angular-spa` | Angular SPA direct OIDC (public client, PKCE) | Remove — BFF handles OIDC on behalf of Angular |

Replace all three with one new confidential client:

```csharp
new Client
{
    ClientId   = "bff",
    ClientName = "Angular BFF",

    ClientSecrets = { new Secret("bff-secret".Sha256()) },
    // Dev only — use Key Vault or equivalent in production.

    AllowedGrantTypes = GrantTypes.Code,
    RequirePkce = true,

    RedirectUris           = { "https://localhost:7000/signin-oidc" },
    PostLogoutRedirectUris = { "https://localhost:7000/signout-callback-oidc" },

    // Back-channel logout lets IdentityServer notify the BFF when any session ends
    // (e.g., IdentityServer admin revokes the user). Registered but not required for
    // basic operation — the BFF handles it automatically via MapBffManagementEndpoints().
    BackChannelLogoutUri = "https://localhost:7000/bff/backchannel",

    AllowedScopes =
    {
        IdentityServerConstants.StandardScopes.OpenId,
        IdentityServerConstants.StandardScopes.Profile,
        IdentityServerConstants.StandardScopes.Email,
        "roles",
        "scheduling_api",
        "billing_api"
    },

    AllowOfflineAccess = true // Refresh tokens
}
```

Why remove the old clients instead of keeping them alongside the new one? Keeping them would leave active grant types registered for URIs that no longer exist, which is a security hygiene issue. Any stale client secret is an unnecessary attack surface.

The `ApiScopes` and `IdentityResources` collections are **unchanged** — the scope names `scheduling_api` and `billing_api` are still valid; they are now requested by the BFF rather than by the resource APIs themselves.

---

## 5. New Bff.WebApi Project

Create the project under `WebApplications/Bff.WebApi/`.

**Packages:**
- `Duende.BFF` — core BFF middleware and management endpoints
- `Duende.BFF.Yarp` — reverse proxy integration (YARP is a transitive dependency of this package)
- `Microsoft.AspNetCore.Authentication.OpenIdConnect` — required for the OIDC handler registered by `AddOidcCookieAuth`

**Project references:**
- `BuildingBlocks.Infrastructure.Auth` — for `AddOidcCookieAuth`
- `BuildingBlocks.WebApplications` — for `UseOpenApiWithScalar` and related middleware
- `Aspire.ServiceDefaults` — for telemetry, health checks, resilience

### Program.cs

```csharp
using BuildingBlocks.Infrastructure.Auth;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// 1. OIDC cookie auth — same extension method, BFF-flavored parameters.
//    saveTokens: true is required so Duende.BFF can read the access token
//    from the authentication ticket and forward it to downstream APIs.
//    offline_access requests a refresh token so sessions survive token expiry.
builder.Services.AddOidcCookieAuth(builder.Configuration,
    saveTokens: true,
    additionalScopes: ["scheduling_api", "billing_api", "offline_access"]);

// 2. Duende.BFF — server-side sessions store the ticket server-side; the browser
//    cookie only holds a session key. AddRemoteApis() enables the YARP integration
//    so MapRemoteBffApiEndpoint() works below.
builder.Services.AddBff(o =>
{
    // Reuse the X-XSRF-TOKEN header for Duende's built-in marker-header check.
    // This means one header satisfies both Duende's anti-forgery check and the
    // IAntiforgery double-submit validation — they cooperate rather than conflict.
    o.AntiForgeryHeaderName = "X-XSRF-TOKEN";
})
.AddServerSideSessions()
.AddRemoteApis();

// 3. IAntiforgery — double-submit cookie pattern. No extra store needed; stateless
//    validation via Data Protection keys (the same keys the auth cookie uses).
builder.Services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");

// 4. CORS — the BFF is the single origin Angular talks to.
//    Resource APIs no longer need CORS (they are called server-to-server).
builder.Services.AddCors(options =>
{
    options.AddPolicy("Angular", policy => policy
        .WithOrigins("https://localhost:7003")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.AddControllers();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseHttpsRedirection();
app.UseCors("Angular");
app.UseAuthentication();

// Issue XSRF-TOKEN cookie on every response so Angular's built-in
// HttpXsrfInterceptor can read it (HttpOnly=false is intentional here —
// the value is not a secret, protection comes from the preflight machinery).
app.Use(async (ctx, next) =>
{
    var af = ctx.RequestServices.GetRequiredService<IAntiforgery>();
    var tokens = af.GetAndStoreTokens(ctx);
    ctx.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!,
        new CookieOptions { HttpOnly = false, Secure = true });
    await next();
});

// Validate X-XSRF-TOKEN on state-changing requests.
// GET/HEAD/OPTIONS are safe methods and do not carry side effects, so they
// are exempt — this is standard practice and matches the CSRF threat model.
app.Use(async (ctx, next) =>
{
    if (HttpMethods.IsGet(ctx.Request.Method)   ||
        HttpMethods.IsHead(ctx.Request.Method)  ||
        HttpMethods.IsOptions(ctx.Request.Method))
    {
        await next();
        return;
    }
    var af = ctx.RequestServices.GetRequiredService<IAntiforgery>();
    if (!await af.IsRequestValidAsync(ctx))
    {
        ctx.Response.StatusCode = 400;
        return;
    }
    await next();
});

// Duende.BFF middleware — applies its own marker-header check (using the
// AntiForgeryHeaderName configured above) and session management.
app.UseBff();
app.UseAuthorization();

// Duende management endpoints:
//   /bff/login             — initiate OIDC login
//   /bff/logout            — sign out (local + IdentityServer)
//   /bff/user              — return claims as JSON array (Angular reads this)
//   /bff/silent-login      — iframe-based session check
//   /bff/back-channel-logout — receives IdentityServer back-channel notifications
app.MapBffManagementEndpoints();

// Reverse proxy — Duende attaches the access token from the server-side session
// to each forwarded request. The resource API receives a Bearer token and validates
// it against IdentityServer's JWKS endpoint.
app.MapRemoteBffApiEndpoint(
        "/api/scheduling",
        builder.Configuration["Downstream:Scheduling"]!)
    .RequireAccessToken();

app.MapRemoteBffApiEndpoint(
        "/api/billing",
        builder.Configuration["Downstream:Billing"]!)
    .RequireAccessToken();

app.Run();
```

### appsettings.json

```json
{
  "Auth": {
    "Authority":    "https://localhost:7010",
    "ClientId":     "bff",
    "ClientSecret": "bff-secret"
  },
  "Downstream": {
    "Scheduling": "https://localhost:7001",
    "Billing":    "https://localhost:7002"
  }
}
```

Note the absence of `Auth:SharedKeysPath`. The BFF is a single process, so ASP.NET Core's default DPAPI keys (on Windows) are sufficient for development. A shared key store is only required when multiple instances of the BFF run behind a load balancer — see [Section 14](#14-data-protection).

Note also the absence of `Auth:Audience`. The BFF is an OIDC client, not a resource API. It requests tokens; it does not validate them.

---

## 6. Scheduling.WebApi and Billing.WebApi Changes

### Auth middleware

**Scheduling.WebApi/Program.cs, line 50:**
```csharp
// Before
builder.Services.AddOidcCookieAuth(builder.Configuration);

// After
builder.Services.AddJwtBearerAuth(builder.Configuration);
```

**Billing.WebApi/Program.cs, line 64:**
```csharp
// Before
builder.Services.AddOidcCookieAuth(builder.Configuration);

// After
builder.Services.AddJwtBearerAuth(builder.Configuration);
```

### CORS — remove entirely

The `AddCors` block and `app.UseCors("Angular")` call in both `Program.cs` files are deleted. Resource APIs are no longer reached cross-origin. The BFF calls them server-to-server through the reverse proxy. Removing CORS from resource APIs also shrinks their attack surface — a browser cannot make direct CORS-preflight requests to them at all.

Current CORS block in Scheduling.WebApi (lines 52-62) and Billing.WebApi (lines 66-76) — both are removed.

### appsettings.json

For each resource API, update `appsettings.json` (or user secrets):

| Key | Before | After |
|---|---|---|
| `Auth:ClientId` | `"scheduling-api"` or `"billing-api"` | Remove |
| `Auth:ClientSecret` | secret value | Remove |
| `Auth:SharedKeysPath` | path to shared key directory | Remove |
| `Auth:Audience` | (not present) | `"scheduling_api"` or `"billing_api"` |
| `Auth:Authority` | `"https://localhost:7010"` | Unchanged |

The audience value must match the API scope name registered in IdentityServer. The JWT bearer middleware validates the `aud` claim against `Auth:Audience` and rejects tokens that do not include it.

### What stays unchanged

The `.csproj` project reference to `BuildingBlocks.Infrastructure.Auth` stays — it is still needed for `AddJwtBearerAuth` and `HttpContextCurrentUser`. Everything below the auth boundary is unchanged:

- `[Authorize(Roles = ...)]` attributes on controllers
- `UserValidator<T>` base class and all validator implementations
- `ICurrentUser` injection in validators and command handlers
- `ValidationErrorWrapper` mapping `ERR_FORBIDDEN` to HTTP 403

The JWT bearer middleware deposits claims on `HttpContext.User` in the same shape the OIDC cookie middleware previously did, because both are configured with `NameClaimType = "name"` and `RoleClaimType = "role"`. The `ICurrentUser` implementation (`HttpContextCurrentUser`) reads those claims — it does not know or care which auth middleware put them there.

---

## 7. Aspire.AppHost Changes

**File**: `Aspire.AppHost/AppHost.cs`

Current file (lines 1-34) registers `identity-webapi`, `scheduling-webapi`, `billing-webapi`, and `scheduling-angularapp`. Phase 9 adds the BFF and rewires references.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var messagingPassword = builder.AddParameter("messaging-password");
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin()
    .WithDataVolume();

var identityApi = builder.AddProject<Projects.Identity_WebApi>("identity-webapi")
    .WithHttpsEndpoint(port: 7010, name: "identity-https");

// Resource APIs — drop WithReference(identityApi).
// The JWT bearer middleware fetches IdentityServer's JWKS via Auth:Authority at startup;
// there is no runtime Aspire service-discovery dependency on identityApi.
// Keep the dev HTTPS endpoints so you can call the APIs directly with a tool like
// Scalar or curl during development without going through the BFF.
var schedulingApi = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-webapi")
    .WithHttpsEndpoint(port: 7001, name: "scheduling-https")
    .WithReference(messaging)
    .WaitFor(messaging);

var billingApi = builder.AddProject<Projects.Billing_WebApi>("billing-webapi")
    .WithHttpsEndpoint(port: 7002, name: "billing-https")
    .WithReference(messaging)
    .WaitFor(messaging);

// BFF — the only public-facing host.
var bffApi = builder.AddProject<Projects.Bff_WebApi>("bff-webapi")
    .WithHttpsEndpoint(port: 7000, name: "bff-https")
    .WithReference(identityApi)    // Auth:Authority config injection
    .WithReference(schedulingApi)  // Downstream:Scheduling config injection
    .WithReference(billingApi)     // Downstream:Billing config injection
    .WithExternalHttpEndpoints();

// Angular SPA — now references only the BFF.
builder.AddJavaScriptApp("scheduling-angularapp",
        "../Frontend/Angular/Scheduling.AngularApp", "start-aspire")
    .WithReference(bffApi)
    .WithHttpsEndpoint(port: 7003, env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

Why remove `WithReference(identityApi)` from the resource APIs? `WithReference` injects the service's URL as an environment variable that Aspire service discovery resolves at runtime. The JWT bearer middleware does contact IdentityServer — but it does so via the static `Auth:Authority` configuration string, not via Aspire's dynamic service discovery. Leaving the reference in would work, but it implies a runtime dependency that does not actually exist.

---

## 8. Angular Changes

### Environment file

**File**: `Frontend/Angular/Scheduling.AngularApp/src/environments/environment.ts`

```typescript
// Before
export const environment = {
  production: false,
  schedulingApiUrl: 'https://localhost:7001',
  billingApiUrl:    'https://localhost:7002'
}

// After
export const environment = {
  production: false,
  apiBaseUrl: 'https://localhost:7000'
}
```

All service classes that currently reference `environment.schedulingApiUrl` or `environment.billingApiUrl` are updated to use `environment.apiBaseUrl`. Route prefixes become `/api/scheduling/` and `/api/billing/` — the BFF strips the prefix before forwarding to the resource API.

### Auth service

**File**: `Frontend/Angular/Scheduling.AngularApp/src/app/core/services/auth.ts`

The current service (lines 48-82) calls `/auth/current-user`, `/auth/login`, and `/auth/logout` on `environment.schedulingApiUrl`. In Phase 9 those endpoints are replaced by Duende's `/bff/*` management endpoints on the BFF.

```typescript
checkAuth(): Observable<UserInfo | null> {
  this.loading.set(true);

  // /bff/user returns a JSON array of { type: string, value: string } objects.
  // Map to the existing UserInfo shape in the tap callback.
  return this.http.get<{ type: string; value: string }[]>(
      `${environment.apiBaseUrl}/bff/user`).pipe(
    tap(claims => {
      const get = (type: string) =>
        claims.find(c => c.type === type)?.value ?? '';
      const getRoles = () =>
        claims.filter(c => c.type === 'role').map(c => c.value);

      this.currentUser.set({
        userId: get('sub'),
        name:   get('name'),
        email:  get('email'),
        roles:  getRoles()
      });
      this.loading.set(false);
    }),
    catchError(() => {
      this.currentUser.set(null);
      this.loading.set(false);
      return of(null);
    })
  );
}

login(): void {
  const returnUrl = encodeURIComponent(window.location.origin);
  window.location.href = `${environment.apiBaseUrl}/bff/login?returnUrl=${returnUrl}`;
}

logout(): void {
  // The /bff/user response includes a 'bff:logout-url' claim containing the full
  // logout URL with id_token_hint pre-populated by Duende.BFF. Navigate to it
  // directly — do not construct the URL manually.
  const claims = this.currentUser();
  const logoutUrl = (claims as any)?._logoutUrl;
  if (logoutUrl) {
    window.location.href = logoutUrl;
  } else {
    window.location.href = `${environment.apiBaseUrl}/bff/logout`;
  }
}
```

In practice, store the logout URL during `checkAuth()` — the `bff:logout-url` claim is one of the entries in the array returned by `/bff/user`. The BFF generates it with the `id_token_hint` already embedded so IdentityServer can populate the logout context and redirect back to `PostLogoutRedirectUri`. This is the same mechanism `OnRedirectToIdentityProviderForSignOut` handles in `AuthExtensions.cs` (lines 163-172) — Duende.BFF does it automatically.

### HTTP interceptor

**File**: `Frontend/Angular/Scheduling.AngularApp/src/app/core/interceptors/auth.interceptor.ts`

The current interceptor (lines 12-36) adds `withCredentials: true` and `X-Requested-With: XMLHttpRequest`. Both stay. No other changes are needed.

`X-Requested-With: XMLHttpRequest` still matters. It triggers the `OnRedirectToIdentityProvider` event hook in `AddOidcCookieAuth` (AuthExtensions.cs lines 99-108), which returns a 401 instead of an HTML redirect when an Angular AJAX call hits an unauthenticated BFF route. This prevents the AJAX response body from being a redirect to IdentityServer (which CORS would block anyway).

Do **not** add manual `X-XSRF-TOKEN` header logic to the interceptor. Angular's built-in `HttpXsrfInterceptor` reads the `XSRF-TOKEN` cookie and copies its value to the `X-XSRF-TOKEN` header automatically on every non-safe request. This only works when cookie and API share the same origin, which they do — Angular (port 7003) calls the BFF (port 7000), and the `XSRF-TOKEN` cookie is issued by the BFF with `SameSite=Lax`. The manual double-submit middleware in `Program.cs` then validates the header server-side.

### app.config.ts

**File**: `Frontend/Angular/Scheduling.AngularApp/src/app/app.config.ts`

The current file (line 13) uses `provideHttpClient(withInterceptors([authInterceptor]))`. Angular's built-in XSRF protection is enabled by default with `provideHttpClient` — the default cookie name is `XSRF-TOKEN` and the default header name is `X-XSRF-TOKEN`, which match what the BFF sets. No extra configuration is needed.

---

## 9. Server-Side Sessions

By default, ASP.NET Core's cookie authentication stores the entire authentication ticket — claims, properties, and tokens — inside the encrypted cookie itself. With `SaveTokens = true` the cookie also contains the access token, the refresh token, and the id_token. This can push the cookie past the browser's 4 KB limit and certainly reveals more to the network layer than necessary.

`AddServerSideSessions()` inverts this. Duende.BFF generates a short opaque session key, stores that key in the cookie, and persists the full ticket in a server-side store keyed by the session ID. The browser cookie shrinks to a few bytes regardless of how many tokens or claims the user has.

The default store is in-memory. This is sufficient for local development and single-instance deployments. It means sessions are lost on process restart, which is acceptable in development.

For production, switch to the Entity Framework-backed store:

```csharp
// Bff.WebApi/Program.cs (production variant)
builder.Services.AddBff()
    .AddServerSideSessions()
    .AddEntityFrameworkServerSideSessions<BffDbContext>(options =>
        options.UseSqlServer(connectionString))
    .AddRemoteApis();
```

A separate `BffDbContext` is not strictly required — the sessions table can coexist in any `DbContext`. Using a dedicated one keeps the BFF's persistence concerns isolated.

Server-side sessions also enable server-initiated session revocation, described in the next section.

---

## 10. Logout Semantics

Two distinct patterns exist and it is important not to conflate them.

| Pattern | Trigger | Mechanism | Scope |
|---|---|---|---|
| Standard logout | User clicks "Sign out" | `/bff/logout` — Duende clears the session row and redirects through IdentityServer's end-session endpoint | This browser's cookie jar only |
| Account-wide revocation | Password change, "sign out everywhere" button, admin force-logout, suspicious activity | `ISessionRevocationService.RevokeSessionsAsync(new SessionFilter { SubjectId = sub })` — deletes every session row for that subject | All sessions for that user, across all browsers and devices |

**Standard logout is per cookie jar, not per device.** If a user is logged in on Chrome and Safari on the same machine, those are two separate cookie jars and therefore two separate sessions. A standard logout from Chrome does not affect Safari. This is the correct default — the user signed in independently in each browser.

**Account-wide revocation requires server-side sessions.** Without a server-side session store there is no database table to query. The BFF cannot know which sessions belong to a given subject. This is why `AddServerSideSessions()` is a prerequisite for the revocation pattern, even if you do not need revocation immediately.

**Single-Sign-Out across multiple clients** (front-channel or back-channel logout from IdentityServer) is a different concept. It applies when multiple client applications are registered in IdentityServer and you want a logout from one to propagate to the others. This is not relevant for the current setup — there is only one BFF client. Registering `BackChannelLogoutUri` in the `bff` client (Section 4) and mapping `MapBffManagementEndpoints()` (Section 5) enables this automatically if a second BFF is ever added.

---

## 11. Anti-Forgery — Why, Not Just How

Cross-Site Request Forgery (CSRF) exploits the browser's automatic cookie-sending behaviour. A page on `evil.com` makes a state-changing request to `bff.example.com`; the browser attaches the `DDD.Auth` cookie without the user's knowledge. Three independent layers prevent this.

**Layer 1: `SameSite=Lax` on `DDD.Auth`**

Configured in `AddOidcCookieAuth` (AuthExtensions.cs line 42). The browser refuses to send the cookie on cross-site state-changing requests (POST, PUT, DELETE) initiated from `evil.com`. It does send the cookie on top-level GET navigations, which is why `Lax` (not `Strict`) is used — `Strict` would break IdentityServer's redirect-back flow.

SameSite alone is sufficient for most attacks, but it requires browser support and is bypassed by certain browser configurations. It is a first line of defence, not the only one.

**Layer 2: Strict CORS policy**

Configured in `Bff.WebApi/Program.cs`. Only `https://localhost:7003` (the Angular origin) is in the allowlist. Browsers send a preflight `OPTIONS` request before any cross-origin request with non-simple headers. The BFF rejects preflights from any other origin. This prevents `evil.com` from even completing the cross-origin request, regardless of cookies.

A subtle point: the CORS preflight itself is the protection mechanism. The value of the XSRF token header is NOT a secret. What matters is that a browser on `evil.com` cannot pass the preflight — it cannot set arbitrary CORS-safelisted headers from a different origin without the server's explicit permission.

**Layer 3: Double-submit token (IAntiforgery)**

The BFF issues an `XSRF-TOKEN` cookie with `HttpOnly = false` so Angular's `HttpXsrfInterceptor` can read it. On every state-changing request, Angular copies the value to the `X-XSRF-TOKEN` header. The BFF validates the header value cryptographically against the cookie using Data Protection keys.

This layer is stateless on the server — no session table or cache entry is needed. The validation is a cryptographic operation using the same Data Protection keys the auth cookie uses. A cross-site attacker cannot read the `XSRF-TOKEN` cookie value (same-origin cookie reading policy) and therefore cannot construct a valid `X-XSRF-TOKEN` header.

Duende.BFF has its own built-in marker-header check, which by default looks for `X-CSRF: 1` — a static string, not a cryptographic token. Setting `BffOptions.AntiForgeryHeaderName = "X-XSRF-TOKEN"` tells Duende to check for the same header that `IAntiforgery` validates, so the two layers cooperate:

| Aspect | Duende default (`X-CSRF: 1`) | IAntiforgery double-submit (`X-XSRF-TOKEN`) |
|---|---|---|
| Header value | Static literal | Per-session encrypted token |
| Server-side validation | String comparison | Cryptographic via Data Protection |
| Relies on | CORS preflight | CORS preflight + cross-origin cookie isolation + crypto |
| Stateful? | No | No |

The double-submit approach is strictly stronger. Using one header for both checks avoids the Angular interceptor needing to set two separate headers.

---

## 12. Headers in Flight: X-Requested-With vs X-XSRF-TOKEN

These two headers are easy to conflate because both are added by the Angular layer. They serve entirely different purposes and are processed by different middleware at different points in the pipeline.

| Header | Value | Set by | Processed by | Purpose |
|---|---|---|---|---|
| `X-Requested-With` | `XMLHttpRequest` | `auth.interceptor.ts` (line 21) | `OnRedirectToIdentityProvider` in `AddOidcCookieAuth` (AuthExtensions.cs lines 99-108) | Auth flow control — tells the OIDC middleware "return 401, do not redirect to IdentityServer" |
| `X-XSRF-TOKEN` | Per-session encrypted token | Angular's built-in `HttpXsrfInterceptor` (automatic) | `IAntiforgery.IsRequestValidAsync` in BFF middleware + Duende's header check | Actual CSRF protection — double-submit cookie validation |

`X-Requested-With` controls which response the unauthenticated user gets. Without it, the OIDC middleware issues a 302 redirect to `https://localhost:7010/connect/authorize?...`. That redirect fails via AJAX because the IdentityServer login page is on a different origin and the CORS response does not include `Access-Control-Allow-Origin`. The Angular interceptor would receive a CORS error, not a 401, and the login redirect logic would not fire. The header makes the API return a clean 401 that the interceptor can handle.

`X-XSRF-TOKEN` prevents authenticated forged requests. It is only relevant after the user is already logged in and the `DDD.Auth` cookie exists.

They serve orthogonal concerns. Removing either one breaks a different part of the auth flow.

---

## 13. CORS

In Phase 8, both Scheduling.WebApi and Billing.WebApi needed CORS configuration because Angular called them directly across origins. In Phase 9, Angular calls only the BFF.

**BFF**: CORS policy allows only `https://localhost:7003` with `AllowCredentials()`. This is required because the auth cookie must be sent on every request.

**Scheduling.WebApi**: Remove `AddCors` and `app.UseCors("Angular")` entirely (lines 52-62 and line 76 of `Program.cs`).

**Billing.WebApi**: Remove `AddCors` and `app.UseCors("Angular")` entirely (lines 66-76 and line 90 of `Program.cs`).

Removing CORS from the resource APIs is not just cleanup. It closes a potential misconfiguration attack vector. If a resource API has CORS enabled with `AllowCredentials()`, a misconfigured allowlist could let a cross-origin attacker call it directly. With no CORS policy at all, the browser cannot initiate a cross-origin preflight to the resource API — the attempt fails before it reaches the application.

In production, go further: put the resource APIs behind a network boundary that rejects any request not originating from the BFF's IP range. The CORS removal is the application-level version of this.

---

## 14. Data Protection

Phase 8 used a shared Data Protection key store (`Auth:SharedKeysPath` pointing to a shared file path) so that Scheduling.WebApi and Billing.WebApi could both issue and decrypt the `DDD.Auth` cookie. In Phase 9 only the BFF issues cookies, so the shared store requirement is gone.

**Development (single instance):** ASP.NET Core uses DPAPI-encrypted keys stored in the user profile by default on Windows. No configuration is needed on the BFF. Remove `Auth:SharedKeysPath` from both resource API configurations — `AddJwtBearerAuth` does not read it, and the Data Protection builder in `AddOidcCookieAuth` only calls `PersistKeysToFileSystem` when the key is present (AuthExtensions.cs lines 193-196).

**Production (multi-instance BFF):** Multiple BFF instances behind a load balancer must share keys so that a cookie issued by instance A can be decrypted by instance B. Configure a shared store before calling `AddOidcCookieAuth`:

```csharp
// Option A: Redis
builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(redisConnection, "DataProtection-Keys")
    .SetApplicationName("DDD.WebApis");

// Option B: Azure Blob Storage
builder.Services.AddDataProtection()
    .PersistKeysToAzureBlobStorage(blobClient)
    .ProtectKeysWithAzureKeyVault(keyVaultKeyId, new DefaultAzureCredential())
    .SetApplicationName("DDD.WebApis");
```

The same shared store also covers antiforgery tokens — `IAntiforgery` uses Data Protection for the cryptographic validation in the double-submit pattern.

---

## 15. Verification Checklist

- [ ] `Bff.WebApi` project created with `Duende.BFF`, `Duende.BFF.Yarp`, and `Microsoft.AspNetCore.Authentication.OpenIdConnect` packages
- [ ] `BuildingBlocks.Infrastructure.Auth`, `BuildingBlocks.WebApplications`, and `Aspire.ServiceDefaults` referenced in `Bff.WebApi.csproj`
- [ ] `AddOidcCookieAuth` signature extended with `saveTokens` and `additionalScopes` parameters; defaults unchanged
- [ ] `AddJwtBearerAuth` method added to `AuthExtensions.cs`
- [ ] `AuthController` class-level XML comment updated
- [ ] `bff` client added to `IdentityServerConfig.Clients`
- [ ] `angular-spa`, `scheduling-api`, and `billing-api` clients removed from `IdentityServerConfig.Clients`
- [ ] `Scheduling.WebApi/Program.cs` line 50 updated from `AddOidcCookieAuth` to `AddJwtBearerAuth`
- [ ] `Billing.WebApi/Program.cs` line 64 updated from `AddOidcCookieAuth` to `AddJwtBearerAuth`
- [ ] CORS blocks removed from `Scheduling.WebApi/Program.cs` (lines 52-62, 76)
- [ ] CORS blocks removed from `Billing.WebApi/Program.cs` (lines 66-76, 90)
- [ ] `Auth:Audience` added to resource API `appsettings.json` (`scheduling_api`, `billing_api`)
- [ ] `Auth:ClientId`, `Auth:ClientSecret`, `Auth:SharedKeysPath` removed from resource API `appsettings.json`
- [ ] `Aspire.AppHost/AppHost.cs` updated: `bff-webapi` added, Angular app references BFF only, resource APIs drop `WithReference(identityApi)`
- [ ] `environment.ts` updated: `schedulingApiUrl`/`billingApiUrl` replaced with `apiBaseUrl: 'https://localhost:7000'`
- [ ] Angular service URLs updated to use `/api/scheduling/` and `/api/billing/` prefixes
- [ ] `AuthService.checkAuth()` calls `/bff/user` and maps the claims array
- [ ] `AuthService.login()` navigates to `/bff/login`
- [ ] `AuthService.logout()` reads `bff:logout-url` claim and navigates there
- [ ] `auth.interceptor.ts` still sends `withCredentials: true` and `X-Requested-With: XMLHttpRequest`; no manual XSRF header logic added
- [ ] `provideHttpClient()` in `app.config.ts` unchanged (built-in XSRF interceptor active by default)
- [ ] BFF issues `XSRF-TOKEN` cookie (`HttpOnly = false`) on every response
- [ ] State-changing requests without a valid `X-XSRF-TOKEN` header receive HTTP 400
- [ ] `GET /bff/user` returns claims JSON when authenticated, 401 when not
- [ ] `GET /api/scheduling/*` proxies to Scheduling.WebApi with `Authorization: Bearer <token>` attached
- [ ] `GET /api/billing/*` proxies to Billing.WebApi with `Authorization: Bearer <token>` attached
- [ ] `[Authorize(Roles=...)]` on resource API controllers still enforced (JWT claims arrive in same shape)
- [ ] Role-based 403 mapping in `ValidationErrorWrapper` still works on resource APIs

---

## 16. Out of Scope / Future Work

**EF-backed server-side sessions** — replace the in-memory session store with `AddEntityFrameworkServerSideSessions<TContext>()` before moving to production. Required for multi-instance BFF and for sessions that survive process restart.

**"Sign out everywhere" button** — a UI button that calls `ISessionRevocationService.RevokeSessionsAsync(new SessionFilter { SubjectId = sub })` from a controller action on the BFF. This deletes all session rows for the current user. Requires the EF-backed session store to be meaningful.

**Multi-instance Data Protection key store** — migrate from DPAPI to Redis or Azure Blob (see Section 14) when the BFF scales beyond one instance.

**Blazor BFF (deferred Phase 7 track)** — if the Blazor frontend is ever implemented, it would get a second BFF client registration in IdentityServer and its own `Bff.BlazorApp` project. Back-channel logout coordination becomes meaningful at that point because IdentityServer can notify both BFFs when a user's session ends at the IDP. The `BackChannelLogoutUri` pattern in Section 4 extends directly to that scenario.

**Integration tests against the live IdentityServer** — tests that start the BFF with the real OIDC flow. Currently the auth layer is unit-tested via mocked `ICurrentUser`. A test that exercises the full code-flow exchange with a running IdentityServer would catch misconfiguration in client registration.

**Token refresh** — `offline_access` is requested and IdentityServer issues refresh tokens. Duende.BFF handles silent token refresh automatically when an access token is near expiry. No additional code is needed, but observing this in the Aspire dashboard (access token lifetime vs refresh token lifetime) is worth verifying during implementation.

---

*Cross-references: `01-api-gateway-optional.md` covers YARP as a standalone API gateway. `02-bff-pattern-optional.md` covers the generic YARP + Refit BFF pattern without Duende. This document assumes Phase 8 auth infrastructure is in place.*
