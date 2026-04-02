# Phase 8: Angular Authentication Integration

> Previous: [04-api-resource-protection.md](./04-api-resource-protection.md)

This document explains how to integrate authentication into the Angular SPA. Unlike traditional SPA authentication patterns, this implementation uses a **cookie-based architecture** where the API handles the entire OIDC flow, dramatically simplifying Angular's responsibilities.

---

## Table of Contents

1. [Why Cookie-Based Auth Simplifies Angular](#why-cookie-based-auth-simplifies-angular)
2. [Architecture Overview](#architecture-overview)
3. [Angular's Responsibilities](#angulars-responsibilities)
4. [HTTP Configuration: Sending Cookies](#http-configuration-sending-cookies)
5. [Auth Service with Signals](#auth-service-with-signals)
6. [Auth Interceptor for 401 Handling](#auth-interceptor-for-401-handling)
7. [Route Guards](#route-guards)
8. [App Initialization](#app-initialization)
9. [UI Integration](#ui-integration)
10. [Proxy Configuration](#proxy-configuration)
11. [Complete Authentication Flow](#complete-authentication-flow)
12. [Security Considerations](#security-considerations)

---

## Why Cookie-Based Auth Simplifies Angular

### Traditional SPA Authentication (What We're NOT Doing)

In typical Angular applications with OIDC:

```typescript
// ❌ Traditional approach - NOT needed in our architecture
import { OAuthService } from 'angular-oauth2-oidc';

export class AuthService {
  constructor(private oauthService: OAuthService) {
    this.configureOAuth();
    this.oauthService.loadDiscoveryDocumentAndTryLogin();
  }

  // Angular manages:
  // - Token storage (access tokens, refresh tokens, ID tokens)
  // - Token refresh logic
  // - PKCE flow implementation
  // - Silent refresh iframes
  // - Token expiration handling
  // - Security vulnerabilities (XSS can steal tokens)
}
```

**Complexity:**
- Install `angular-auth-oidc-client` or `angular-oauth2-oidc` package
- Configure OIDC settings in Angular
- Implement token refresh logic
- Handle token storage (localStorage, sessionStorage, or memory)
- Manage PKCE flow
- Tokens exposed to JavaScript (XSS risk)

### Cookie-Based Approach (What We ARE Doing)

```typescript
// ✅ Cookie-based approach - Simple and secure
export class AuthService {
  checkAuth() {
    // API handles OIDC, Angular just checks if user is authenticated
    return this.http.get<UserInfo>('/auth/me');
  }

  login() {
    // Full page redirect to API's login endpoint
    window.location.href = '/auth/login?returnUrl=' + currentUrl;
  }
}
```

**Benefits:**
- ✅ No OIDC library needed
- ✅ No token management in JavaScript
- ✅ Tokens never exposed to browser (more secure against XSS)
- ✅ Browser automatically sends cookies with requests
- ✅ Much less code in Angular
- ✅ API controls all authentication logic

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                      Angular SPA (Port 7003)                    │
│                                                                  │
│  ┌──────────────────┐      ┌─────────────────────────────────┐ │
│  │  AuthService     │      │  HttpClient + Interceptor       │ │
│  │  - checkAuth()   │      │  withCredentials: true          │ │
│  │  - login()       │      │  (sends cookies automatically)  │ │
│  │  - logout()      │      └─────────────────────────────────┘ │
│  └──────────────────┘                                           │
│         │                                                        │
│         │ GET /auth/me                                          │
│         │ (with cookie)                                         │
│         ▼                                                        │
└─────────────────────────────────────────────────────────────────┘
         │
         │ HTTPS with cookies
         ▼
┌─────────────────────────────────────────────────────────────────┐
│              Scheduling API (Port 7001)                         │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ Auth Endpoints (from doc 03)                             │  │
│  │ - GET  /auth/me        → returns user info from cookie  │  │
│  │ - GET  /auth/login     → redirects to Auth0             │  │
│  │ - GET  /auth/callback  → handles OIDC callback          │  │
│  │ - POST /auth/logout    → clears cookie                  │  │
│  └──────────────────────────────────────────────────────────┘  │
│         │                                                        │
│         │ OIDC flow                                             │
│         ▼                                                        │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ OpenIdConnect Authentication Handler                     │  │
│  │ - Manages OIDC protocol                                  │  │
│  │ - Stores tokens securely in encrypted cookie            │  │
│  │ - Handles token refresh automatically                   │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
         │
         │ OIDC protocol
         ▼
┌─────────────────────────────────────────────────────────────────┐
│                  Auth0 / Auth Server (Port 7010)                │
│  - Login UI                                                      │
│  - User management                                              │
│  - Token issuance                                               │
└─────────────────────────────────────────────────────────────────┘
```

---

## Angular's Responsibilities

Angular has three simple responsibilities:

| Responsibility | Implementation | Why |
|---------------|----------------|-----|
| **Send cookies with requests** | `withCredentials: true` on HttpClient | Browser sends authentication cookie to API |
| **Handle 401 responses** | Interceptor redirects to `/auth/login` | Session expired or not logged in |
| **Check auth state** | Call `/auth/me` endpoint | Get current user info and roles |

That's it. No token management, no PKCE flow, no refresh logic.

---

## HTTP Configuration: Sending Cookies

Angular must send cookies with cross-origin requests. Configure this globally using `provideHttpClient` and an interceptor.

### Step 1: Create Auth Interceptor

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/core/interceptors/auth.interceptor.ts
import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * Auth interceptor that:
 * 1. Adds withCredentials: true to all requests (sends cookies)
 * 2. Handles 401 responses by redirecting to login
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);

  // Clone request with credentials enabled
  // This tells the browser to send cookies with the request
  const authReq = req.clone({
    withCredentials: true
  });

  return next(authReq).pipe(
    catchError((error: HttpErrorResponse) => {
      // If we get a 401 (Unauthorized) response
      if (error.status === 401) {
        // Don't redirect for /auth/me endpoint
        // (401 is expected when user is not logged in)
        if (!req.url.includes('/auth/me')) {
          // Redirect to login with current URL as return URL
          authService.login(window.location.pathname);
        }
      }

      return throwError(() => error);
    })
  );
};
```

### Step 2: Register Interceptor in App Config

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/app.config.ts
import { ApplicationConfig, APP_INITIALIZER } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors, withFetch } from '@angular/common/http';
import { routes } from './app.routes';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { AuthService } from './core/services/auth.service';
import { Observable } from 'rxjs';

/**
 * Initialize authentication on app startup
 */
export function initializeAuth(authService: AuthService): () => Observable<any> {
  return () => authService.checkAuth();
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([authInterceptor]),
      withFetch()
    ),
    {
      provide: APP_INITIALIZER,
      useFactory: initializeAuth,
      deps: [AuthService],
      multi: true
    }
  ]
};
```

**Key points:**
- `withCredentials: true` → Browser sends cookies with every request
- Interceptor runs on every HTTP call
- 401 handling is centralized

---

## Auth Service with Signals

The `AuthService` manages authentication state using Angular signals for reactive state management.

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/core/services/auth.service.ts
import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { tap, catchError } from 'rxjs/operators';

/**
 * User information returned from /auth/me endpoint
 */
export interface UserInfo {
  userId: string;
  email: string;
  name: string;
  roles: string[];
  isAuthenticated: boolean;
}

/**
 * Authentication service using Angular signals for reactive state.
 *
 * This service does NOT manage tokens or implement OIDC flow.
 * The API handles all authentication. Angular just:
 * 1. Checks auth state via /auth/me
 * 2. Redirects to /auth/login for login
 * 3. Calls /auth/logout for logout
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  // Private writable signals
  private currentUser = signal<UserInfo | null>(null);
  private loading = signal<boolean>(true);

  // Public readonly signals
  user = this.currentUser.asReadonly();
  isAuthenticated = computed(() => this.currentUser() !== null);
  isLoading = this.loading.asReadonly();

  // Computed role checks
  isAdmin = computed(() => this.currentUser()?.roles.includes('Admin') ?? false);
  isDoctor = computed(() => this.currentUser()?.roles.includes('Doctor') ?? false);

  constructor(private http: HttpClient) {}

  /**
   * Check authentication status by calling /auth/me
   * This is called on app initialization and after login
   *
   * The API returns user info if cookie is valid, 401 if not
   */
  checkAuth(): Observable<UserInfo | null> {
    this.loading.set(true);

    return this.http.get<UserInfo>('/auth/me').pipe(
      tap(user => {
        this.currentUser.set(user);
        this.loading.set(false);
      }),
      catchError(() => {
        // 401 expected when not logged in
        this.currentUser.set(null);
        this.loading.set(false);
        return of(null);
      })
    );
  }

  /**
   * Redirect to login endpoint with return URL
   * The API handles the OIDC flow and sets authentication cookie
   *
   * @param returnUrl - URL to return to after successful login
   */
  login(returnUrl: string = window.location.pathname): void {
    const encodedReturnUrl = encodeURIComponent(returnUrl);
    window.location.href = `/auth/login?returnUrl=${encodedReturnUrl}`;
  }

  /**
   * Log out by calling /auth/logout
   * This clears the authentication cookie on the server
   */
  logout(): Observable<void> {
    return this.http.post<void>('/auth/logout', {}).pipe(
      tap(() => {
        this.currentUser.set(null);
      })
    );
  }

  /**
   * Check if user has a specific role
   */
  hasRole(role: string): boolean {
    return this.currentUser()?.roles.includes(role) ?? false;
  }
}
```

**Signal benefits:**
- `user()` is reactive - components automatically update when auth state changes
- `isAuthenticated()` computed signal - derived state
- No manual subscription management
- Type-safe access to user info

---

## Auth Interceptor for 401 Handling

The interceptor (shown earlier) handles two critical functions:

### 1. Send Cookies with Every Request

```typescript
const authReq = req.clone({
  withCredentials: true
});
```

**Why:** Cross-origin requests don't send cookies by default. This flag tells the browser to include cookies in the request.

### 2. Redirect to Login on 401

```typescript
catchError((error: HttpErrorResponse) => {
  if (error.status === 401 && !req.url.includes('/auth/me')) {
    authService.login(window.location.pathname);
  }
  return throwError(() => error);
})
```

**Why:** 401 means the cookie is invalid or expired. Redirect to login to re-authenticate.

**Special case:** Don't redirect for `/auth/me` - a 401 there just means "not logged in" and is expected.

---

## Route Guards

Protect routes that require authentication using Angular's functional guard API.

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/core/guards/auth.guard.ts
import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';

/**
 * Route guard that requires authentication
 * Redirects to login if user is not authenticated
 */
export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);

  if (authService.isAuthenticated()) {
    return true;
  }

  // Not authenticated - redirect to login with return URL
  authService.login(state.url);
  return false;
};

/**
 * Route guard that requires a specific role
 * Returns 403 if user doesn't have the required role
 */
export function roleGuard(role: string): CanActivateFn {
  return (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    if (!authService.isAuthenticated()) {
      authService.login(state.url);
      return false;
    }

    if (!authService.hasRole(role)) {
      // User is authenticated but doesn't have required role
      router.navigate(['/forbidden']);
      return false;
    }

    return true;
  };
}
```

### Apply Guards to Routes

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/app.routes.ts
import { Routes } from '@angular/router';
import { authGuard, roleGuard } from './core/guards/auth.guard';
import { PatientListComponent } from './features/patients/patient-list/patient-list.component';
import { PatientCreateComponent } from './features/patients/patient-create/patient-create.component';
import { PatientDetailComponent } from './features/patients/patient-detail/patient-detail.component';
import { HomeComponent } from './features/home/home.component';
import { ForbiddenComponent } from './features/forbidden/forbidden.component';

export const routes: Routes = [
  {
    path: '',
    component: HomeComponent
  },
  {
    path: 'patients',
    component: PatientListComponent,
    canActivate: [authGuard]
  },
  {
    path: 'patients/new',
    component: PatientCreateComponent,
    canActivate: [authGuard]
  },
  {
    path: 'patients/:id',
    component: PatientDetailComponent,
    canActivate: [authGuard]
  },
  {
    path: 'admin',
    loadChildren: () => import('./features/admin/admin.routes').then(m => m.routes),
    canActivate: [roleGuard('Admin')]
  },
  {
    path: 'forbidden',
    component: ForbiddenComponent
  },
  {
    path: '**',
    redirectTo: ''
  }
];
```

---

## App Initialization

Check authentication status when the app starts using Angular's `APP_INITIALIZER`.

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/app.config.ts
import { ApplicationConfig, APP_INITIALIZER } from '@angular/core';
import { AuthService } from './core/services/auth.service';
import { Observable } from 'rxjs';

/**
 * Initialize authentication on app startup
 * This ensures auth state is loaded before the app renders
 */
export function initializeAuth(authService: AuthService): () => Observable<any> {
  return () => authService.checkAuth();
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([authInterceptor]),
      withFetch()
    ),
    {
      provide: APP_INITIALIZER,
      useFactory: initializeAuth,
      deps: [AuthService],
      multi: true
    }
  ]
};
```

**What happens:**
1. App starts to load
2. `APP_INITIALIZER` calls `authService.checkAuth()`
3. Request to `/auth/me` is sent
4. If cookie is valid → user info is returned and stored in signal
5. If cookie is invalid → 401 response, user remains null
6. App finishes loading and routes are activated

**Result:** Auth state is always known before the user sees the UI.

---

## UI Integration

Show login/logout UI based on authentication state using signals in templates.

### Navigation Bar Component

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/core/layout/navbar/navbar.component.ts
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  templateUrl: './navbar.component.html',
  styleUrl: './navbar.component.css'
})
export class NavbarComponent {
  authService = inject(AuthService);
  private router = inject(Router);

  logout(): void {
    this.authService.logout().subscribe(() => {
      // Redirect to home after logout
      this.router.navigate(['/']);
    });
  }
}
```

### Navigation Bar Template

```html
<!-- Frontend/Angular/Scheduling.AngularApp/src/app/core/layout/navbar/navbar.component.html -->
<nav class="navbar">
  <div class="navbar-brand">
    <a routerLink="/">Patient Scheduling</a>
  </div>

  <ul class="navbar-nav">
    @if (authService.isAuthenticated()) {
      <li>
        <a routerLink="/patients" routerLinkActive="active">Patients</a>
      </li>

      @if (authService.isAdmin()) {
        <li>
          <a routerLink="/admin" routerLinkActive="active">Admin</a>
        </li>
      }
    }
  </ul>

  <div class="navbar-user">
    @if (authService.isLoading()) {
      <span>Loading...</span>
    } @else if (authService.isAuthenticated()) {
      <span class="user-name">{{ authService.user()?.name }}</span>
      <button class="btn-logout" (click)="logout()">Logout</button>
    } @else {
      <button class="btn-login" (click)="authService.login()">Login</button>
    }
  </div>
</nav>
```

### Conditional Content in Components

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/features/patients/patient-list/patient-list.component.ts
import { Component, inject } from '@angular/core';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-patient-list',
  standalone: true,
  template: `
    <div class="patient-list">
      <h1>Patients</h1>

      @if (authService.hasRole('Doctor') || authService.isAdmin()) {
        <button (click)="createPatient()">Create New Patient</button>
      }

      <!-- Patient list content -->
    </div>
  `
})
export class PatientListComponent {
  authService = inject(AuthService);

  createPatient(): void {
    // Navigate to create patient page
  }
}
```

**Signal reactivity:**
- When `authService.user()` changes, the template automatically updates
- No manual subscriptions needed
- Clean, declarative syntax with `@if` control flow

---

## Proxy Configuration

Configure Angular's dev server to proxy API requests to the backend.

### Option 1: proxy.conf.json

```json
// Frontend/Angular/Scheduling.AngularApp/proxy.conf.json
{
  "/auth": {
    "target": "https://localhost:7001",
    "secure": false,
    "changeOrigin": true,
    "logLevel": "debug"
  },
  "/api": {
    "target": "https://localhost:7001",
    "secure": false,
    "changeOrigin": true,
    "logLevel": "debug"
  }
}
```

### Update angular.json

```json
// Frontend/Angular/Scheduling.AngularApp/angular.json
{
  "projects": {
    "scheduling-angular-app": {
      "architect": {
        "serve": {
          "options": {
            "proxyConfig": "proxy.conf.json"
          }
        }
      }
    }
  }
}
```

### Option 2: Environment-Based Configuration

For production builds, use environment configuration:

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/environments/environment.ts
export const environment = {
  production: false,
  apiUrl: 'https://localhost:7001'
};

// Frontend/Angular/Scheduling.AngularApp/src/environments/environment.prod.ts
export const environment = {
  production: true,
  apiUrl: 'https://scheduling-api.yourcompany.com'
};
```

Update HTTP calls to use environment:

```typescript
// In services
import { environment } from '../../../environments/environment';

checkAuth(): Observable<UserInfo | null> {
  return this.http.get<UserInfo>(`${environment.apiUrl}/auth/me`);
}
```

**For development:** Use proxy to avoid CORS issues and simplify configuration.

---

## Complete Authentication Flow

### Flow 1: First Visit (Not Logged In)

```
1. User navigates to Angular app (http://localhost:4200)
   │
   ├─> APP_INITIALIZER calls authService.checkAuth()
   │   │
   │   ├─> GET /auth/me (no cookie)
   │   │   │
   │   │   └─> 401 Unauthorized
   │   │
   │   └─> authService.currentUser.set(null)
   │
   ├─> App renders with "Login" button visible
   │
   └─> User clicks "Login"
       │
       ├─> authService.login('/patients')
       │   │
       │   └─> window.location.href = '/auth/login?returnUrl=%2Fpatients'
       │
       ├─> Browser redirects to API's /auth/login endpoint
       │   │
       │   └─> API redirects to Auth0
       │
       ├─> User authenticates with Auth0
       │
       ├─> Auth0 redirects back to /auth/callback
       │   │
       │   └─> API validates token, creates cookie, redirects to returnUrl
       │
       └─> Browser returns to Angular with authentication cookie
           │
           ├─> APP_INITIALIZER calls authService.checkAuth()
           │   │
           │   ├─> GET /auth/me (with cookie)
           │   │   │
           │   │   └─> 200 OK with UserInfo
           │   │
           │   └─> authService.currentUser.set(userInfo)
           │
           └─> App renders with user info and "Logout" button
```

### Flow 2: Protected Route Access

```
1. User clicks link to /patients (protected route)
   │
   ├─> Router evaluates authGuard
   │   │
   │   ├─> Check: authService.isAuthenticated()
   │   │   │
   │   │   ├─> If TRUE: allow navigation
   │   │   │
   │   │   └─> If FALSE: authService.login('/patients')
   │   │       │
   │   │       └─> Redirect to /auth/login
   │   │
   │   └─> Route is activated or redirected
   │
   └─> Component loads and makes API calls
       │
       ├─> All requests include cookie (withCredentials: true)
       │
       └─> API validates cookie and returns data
```

### Flow 3: Session Expiration

```
1. User is logged in and using the app
   │
   ├─> Cookie expires (e.g., after 8 hours)
   │
   └─> User clicks button that makes API call
       │
       ├─> GET /api/patients (with expired cookie)
       │   │
       │   └─> 401 Unauthorized
       │
       ├─> authInterceptor catches 401
       │   │
       │   └─> authService.login(currentUrl)
       │       │
       │       └─> Redirect to /auth/login
       │
       └─> User re-authenticates and returns to where they left off
```

### Flow 4: Logout

```
1. User clicks "Logout" button
   │
   ├─> authService.logout()
   │   │
   │   ├─> POST /auth/logout
   │   │   │
   │   │   └─> API clears authentication cookie
   │   │       │
   │   │       └─> 200 OK
   │   │
   │   └─> authService.currentUser.set(null)
   │
   └─> Router navigates to home page
       │
       └─> App renders with "Login" button visible
```

---

## Security Considerations

### 1. Why Cookies Are More Secure Than Tokens in localStorage

| Storage | XSS Risk | CSRF Risk | Best Practice |
|---------|----------|-----------|---------------|
| **localStorage** | ❌ High - JavaScript can read tokens | ✅ Low - Need explicit code to send | Don't use for sensitive tokens |
| **sessionStorage** | ❌ High - JavaScript can read tokens | ✅ Low - Need explicit code to send | Don't use for sensitive tokens |
| **HttpOnly Cookie** | ✅ Low - JavaScript cannot access | ❌ Medium - Mitigated with SameSite | ✅ **Recommended** |

**Our implementation:**
- Cookies are `HttpOnly` (JavaScript cannot read them)
- Cookies are `Secure` (only sent over HTTPS)
- Cookies use `SameSite=Lax` (CSRF protection)
- Even if XSS attack injects malicious JavaScript, it cannot steal the authentication cookie

### 2. CORS Configuration

The API must allow credentials from Angular's origin:

```csharp
// From doc 03 - already configured
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200", "https://localhost:7003")
              .AllowCredentials()  // Required for cookies
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
```

**Critical:** `AllowCredentials()` must be set for `withCredentials: true` to work.

### 3. Return URL Validation

Always validate return URLs to prevent open redirect attacks:

```typescript
// In AuthService
login(returnUrl: string = '/'): void {
  // Validate return URL is relative (starts with /)
  if (!returnUrl.startsWith('/')) {
    returnUrl = '/';
  }

  const encodedReturnUrl = encodeURIComponent(returnUrl);
  window.location.href = `/auth/login?returnUrl=${encodedReturnUrl}`;
}
```

### 4. Loading State Handling

Always show loading state during auth checks:

```html
@if (authService.isLoading()) {
  <div class="loading-spinner">Loading...</div>
} @else if (authService.isAuthenticated()) {
  <!-- Protected content -->
} @else {
  <button (click)="authService.login()">Login</button>
}
```

**Why:** Prevents flash of wrong content during app initialization.

---

## Summary

### What Angular Does

1. **Sends cookies** with every request (`withCredentials: true`)
2. **Checks auth state** via `/auth/me` endpoint
3. **Redirects to login** when not authenticated
4. **Protects routes** with guards
5. **Shows/hides UI** based on auth state

### What Angular Does NOT Do

1. ❌ Manage tokens (API does this)
2. ❌ Implement OIDC flow (API does this)
3. ❌ Handle token refresh (browser + API do this)
4. ❌ Store sensitive data in JavaScript (cookies are HttpOnly)

### Key Files Created

```
Frontend/Angular/Scheduling.AngularApp/src/app/
├── core/
│   ├── guards/
│   │   └── auth.guard.ts                  # Route protection
│   ├── interceptors/
│   │   └── auth.interceptor.ts            # withCredentials + 401 handling
│   ├── services/
│   │   └── auth.service.ts                # Auth state management with signals
│   └── layout/
│       └── navbar/
│           ├── navbar.component.ts        # Login/logout UI
│           └── navbar.component.html
├── app.config.ts                           # APP_INITIALIZER for auth check
└── app.routes.ts                           # Route guards applied
proxy.conf.json                              # Proxy configuration
```

### Benefits of This Architecture

✅ **Simple** - No OIDC library, minimal Angular code
✅ **Secure** - Tokens never exposed to JavaScript
✅ **Maintainable** - Auth logic centralized in API
✅ **Reactive** - Signals provide automatic UI updates
✅ **User-friendly** - Seamless redirects, preserved return URLs

---

> Next: [06-user-context-and-authorization.md](./06-user-context-and-authorization.md) - User Context in Domain Layer
