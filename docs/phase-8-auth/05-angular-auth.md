# Phase 8: Angular Authentication Integration

> Previous: [04-api-resource-protection.md](./04-api-resource-protection.md)

This document explains how to integrate authentication into the Angular SPA. Unlike traditional SPA authentication patterns, this implementation uses a **cookie-based architecture** where the API handles the entire OIDC flow, dramatically simplifying Angular's responsibilities.

---

## Table of Contents

1. [Why Cookie-Based Auth Simplifies Angular](#why-cookie-based-auth-simplifies-angular)
2. [Architecture Overview](#architecture-overview)
3. [Angular's Responsibilities](#angulars-responsibilities)
4. [Auth Service with Signals](#auth-service-with-signals)
5. [Auth Interceptor](#auth-interceptor)
6. [App Configuration](#app-configuration)
7. [Route Guards](#route-guards)
8. [UI Integration](#ui-integration)
9. [Complete Authentication Flow](#complete-authentication-flow)
10. [Security Considerations](#security-considerations)
11. [Common Issues](#common-issues)

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
    return this.http.get<UserInfo>(`${environment.schedulingApiUrl}/auth/current-user`);
  }

  login() {
    // Full page redirect to API's login endpoint
    window.location.href = `${environment.schedulingApiUrl}/auth/login`;
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
│                                                                 │
│  ┌──────────────────┐      ┌─────────────────────────────────┐  │
│  │  AuthService     │      │  HttpClient + Interceptor       │  │
│  │  - checkAuth()   │      │  withCredentials: true          │  │
│  │  - login()       │      │  (sends cookies automatically)  │  │
│  │  - logout()      │      └─────────────────────────────────┘  │
│  └──────────────────┘                                           │
│         │                                                       │
│         │ GET /auth/current-user                                │
│         │ (with cookie)                                         │
│         ▼                                                       │
└─────────────────────────────────────────────────────────────────┘
         │
         │ HTTPS with cookies
         ▼
┌──────────────────────────────────────────────────────────────────────┐
│              Scheduling API (Port 7001)                              │
│                                                                      │
│  ┌──────────────────────────────────────────────────────────────────┐│
│  │ Auth Endpoints (from doc 03)                                     ││
│  │ - GET  /auth/current-user        → returns user info from cookie |│
│  │ - GET  /auth/login     → redirects to IdentityServer             |│
│  │ - GET  /auth/callback  → handles OIDC callback                   ││
│  │ - POST /auth/logout    → clears cookie                           ││
│  └──────────────────────────────────────────────────────────────────┘│
│         │                                                            │
│         │ OIDC flow                                                  │
│         ▼                                                            │
│  ┌──────────────────────────────────────────────────────────┐        │
│  │ OpenIdConnect Authentication Handler                     │        │
│  │ - Manages OIDC protocol                                  │        │
│  │ - Stores tokens securely in encrypted cookie             │        │
│  │ - Handles token refresh automatically                    │        │
│  └──────────────────────────────────────────────────────────┘        │
└──────────────────────────────────────────────────────────────────────┘
         │
         │ OIDC protocol
         ▼
┌─────────────────────────────────────────────────────────────────┐
│    Duende IdentityServer / Auth Server (Port 7010)              │
│  - Login UI                                                     │
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
| **Check auth state** | Call `/auth/current-user` endpoint | Get current user info and roles |

That's it. No token management, no PKCE flow, no refresh logic.

---

## Auth Service with Signals

The `AuthService` manages authentication state using Angular signals for reactive state management.

First, define the `UserInfo` model:

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/core/models/user-info.model.ts

/**
 * User information returned from /auth/current-user endpoint
 */
export interface UserInfo {
  userId: string;
  email: string;
  name: string;
  roles: string[];
  isAuthenticated: boolean;
}
```

Define typed role constants:

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/core/constants/approles.ts

export class AppRoles {
  static Admin = 'Admin';
  static Doctor = 'Doctor';
}
```

Then the service:

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/core/services/auth.ts
import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { tap, catchError } from 'rxjs/operators';
import { UserInfo } from '../models/user-info.model';
import { environment } from '../../../environments/environment';

/**
 * Authentication service using Angular signals for reactive state.
 *
 * This service does NOT manage tokens or implement OIDC flow.
 * The API handles all authentication. Angular just:
 * 1. Checks auth state via /auth/current-user
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

  constructor(private http: HttpClient) {}

  /**
   * Check authentication status by calling /auth/current-user
   * This is called on app initialization and after login
   *
   * The API returns user info if cookie is valid, 401 if not
   *
   * Note: If the user is not logged in, the API returns 401.
   * The auth interceptor catches this and redirects to login before
   * the catchError below runs — this is the forced login behavior.
   * The catchError still handles non-401 errors (network failures, 500s).
   */
  checkAuth(): Observable<UserInfo | null> {
    this.loading.set(true);

    return this.http.get<UserInfo>(`${environment.schedulingApiUrl}/auth/current-user`).pipe(
      tap(user => {
        this.currentUser.set(user);
        this.loading.set(false);
      }),
      catchError(() => {
        // Non-401 errors (network failures, 500s)
        this.currentUser.set(null);
        this.loading.set(false);
        return of(null);
      })
    );
  }

  /**
   * Redirect to API's login endpoint.
   * The API handles the OIDC flow and redirects back to the app.
   */
  login(): void {
    window.location.href = `${environment.schedulingApiUrl}/auth/login`;
  }

  /**
   * Log out by navigating to the API's logout endpoint.
   * This triggers a redirect chain: API clears cookie → IdentityServer logout → back to app.
   * Must be a full page navigation (not AJAX) because the OIDC logout flow involves redirects.
   */
  logout(): void {
    const returnUrl = encodeURIComponent(window.location.origin);
    window.location.href = `${environment.schedulingApiUrl}/auth/logout?returnUrl=${returnUrl}`;
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

> **Signal pitfall — always invoke with `()`**
>
> Inside `computed()`, reading a signal without `()` gives you the signal reference (a function object), not the current value. A reference is always truthy, so comparisons like `!== null` silently lie:
>
> ```typescript
> // Wrong — this.currentUser is the signal reference, never null
> isAuthenticated = computed(() => this.currentUser !== null);  // always true
>
> // Correct — invoke the signal to get its current value
> isAuthenticated = computed(() => this.currentUser() !== null);
> ```
>
> The same rule applies anywhere you want the value: `computed()`, `effect()`, template expressions. The one exception is `asReadonly()` — that deliberately passes the signal reference through so consumers invoke it themselves. Reading vs. exposing a signal are different operations.

---

## Auth Interceptor

The interceptor handles two critical functions: sending cookies with every request and handling 401/403 responses.

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/core/interceptors/auth.interceptor.ts
import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, EMPTY, throwError } from 'rxjs';
import { AuthService } from '../services/auth';

/**
 * Auth interceptor that:
 * 1. Adds withCredentials: true to all requests (sends cookies)
 * 2. Adds X-Requested-With header so the API returns 401 instead of redirecting to IdentityServer
 * 3. Handles 401 responses by redirecting to login
 * 4. Re-throws all other errors for component-level handling
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);

  // Clone request with credentials and X-Requested-With header.
  // withCredentials tells the browser to send cookies with cross-origin requests.
  // X-Requested-With tells the API this is an AJAX call — the API returns 401
  // instead of redirecting to IdentityServer (which would fail via AJAX/CORS).
  const authReq = req.clone({
    withCredentials: true,
    setHeaders: { 'X-Requested-With': 'XMLHttpRequest' }
  });

  return next(authReq).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401) {
        // Redirect to login page
        authService.login();
        // Return EMPTY to cancel the original request observable
        return EMPTY;
      }

      // For all other errors, re-throw for component-level handling
      return throwError(() => error);
    })
  );
};
```

### 1. Send Cookies and Identify AJAX Requests

```typescript
const authReq = req.clone({
  withCredentials: true,
  setHeaders: { 'X-Requested-With': 'XMLHttpRequest' }
});
```

**Why:** `withCredentials` tells the browser to include cookies in cross-origin requests. `X-Requested-With` tells the API this is an AJAX call — the API's `OnRedirectToIdentityProvider` handler checks this header and returns 401 instead of redirecting to IdentityServer (which would fail via AJAX due to CORS).

### 2. Redirect to Login on 401

```typescript
catchError((error: HttpErrorResponse) => {
  if (error.status === 401) {
    authService.login();
    return EMPTY;
  }
  return throwError(() => error);
})
```

**Why:** 401 means the cookie is invalid, expired, or absent. The interceptor immediately redirects to login. Returning `EMPTY` (instead of `throwError`) cancels the observable — the page is navigating away anyway, so there's no point propagating the error. All other errors (403, 500, network failures) are re-thrown for component-level handling.

---

## App Configuration

Configure interceptor registration and app initialization in `app.config.ts`.

The existing `app.config.ts` already has `provideHttpClient` and `provideRouter`. We need to add the auth interceptor and an app initializer:

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/app.config.ts
import { ApplicationConfig, inject, provideBrowserGlobalErrorListeners, provideAppInitializer, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { routes } from './app.routes';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { AuthService } from './core/services/auth';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([authInterceptor])
    ),
    // Check auth state on startup — if no valid cookie, the interceptor
    // catches the 401 and redirects to login immediately (forced login)
    provideAppInitializer(() => inject(AuthService).checkAuth())
  ]
};
```

**What changed from the existing config:**
- `withInterceptorsFromDi()` → `withInterceptors([authInterceptor])` — the existing `withInterceptorsFromDi()` was a placeholder (no class-based interceptors exist). Replace it with `withInterceptors([...])` to register functional interceptors like `authInterceptor`
- Added `provideAppInitializer()` to check auth state on startup

**What happens on startup:**
1. App starts to load
2. `provideAppInitializer` calls `authService.checkAuth()`
3. Request to `${environment.schedulingApiUrl}/auth/current-user` is sent (with cookie if present)
4. If cookie is valid → 200 OK, user info stored in signal, app renders
5. If cookie is invalid or absent → 401, interceptor redirects to `${environment.schedulingApiUrl}/auth/login` immediately
6. User authenticates with IdentityServer, returns with valid cookie
7. App restarts, `checkAuth()` succeeds, app renders

**Result:** Users are always authenticated before the app loads. There is no "logged out" state in the SPA.

---

## Route Guards

Protect routes that require authentication using Angular's functional guard API.

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/core/guards/auth.guard.ts
import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth';

/**
 * Route guard that requires authentication
 * Redirects to login if user is not authenticated
 */
export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);

  if (authService.isAuthenticated()) {
    return true;
  }

  // Not authenticated - redirect to login
  authService.login();
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
      authService.login();
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

The existing routes already use `loadComponent` with lazy imports. Add guards to protect them:

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/app.routes.ts
import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  {
    path: '',
    redirectTo: '/patients',
    pathMatch: 'full'
  },
  {
    path: 'patients',
    loadComponent: () =>
      import('./features/patients/patient-list/patient-list')
        .then(m => m.PatientList),
    canActivate: [authGuard]
  },
  {
    path: 'patients/create',
    loadComponent: () =>
      import('./features/patients/create-patient/create-patient')
        .then(m => m.CreatePatient),
    canActivate: [authGuard]
  },
  {
    path: 'patients/:id',
    loadComponent: () =>
      import('./features/patients/patient-detail/patient-detail')
        .then(m => m.PatientDetail),
    canActivate: [authGuard]
  },
  {
    path: '**',
    redirectTo: ''
  }
];
```

**What changed from the existing routes:**
- Added `authGuard` to patient routes

---

## UI Integration

Show login/logout UI based on authentication state using signals in templates.

### Navigation Bar Component

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/core/layout/navbar/navbar.ts
import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../services/auth';
import { AppRoles } from '../../constants/approles';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './navbar.html',
  styleUrl: './navbar.scss'
})
export class Navbar {
  authService = inject(AuthService);
  protected readonly AppRoles = AppRoles;

  logout(): void {
    this.authService.logout();
  }
}
```

### Navigation Bar Template

```html
<!-- Frontend/Angular/Scheduling.AngularApp/src/app/core/layout/navbar/navbar.html -->
<mat-toolbar color="primary">
  <a mat-button routerLink="/">Patient Scheduling</a>

  <span class="spacer"></span>

  @if (authService.isAuthenticated()) {
    <a mat-button routerLink="/patients" routerLinkActive="active">Patients</a>

    @if (authService.hasRole(AppRoles.Admin)) {
      <a mat-button routerLink="/admin" routerLinkActive="active">Admin</a>
    }
  }

  <span class="spacer"></span>

  @if (authService.isLoading()) {
    <mat-spinner diameter="20" />
  } @else if (authService.isAuthenticated()) {
    <span>{{ authService.user()?.name }}</span>
    <button mat-button (click)="logout()">Logout</button>
  } @else {
    <button mat-flat-button (click)="authService.login()">Login</button>
  }
</mat-toolbar>
```

### Adding the Navbar to the App

Update the root component to use `<app-navbar>` instead of the default toolbar:

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/app.ts
import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Navbar } from '@core/layout/navbar/navbar';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Navbar],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {}
```

```html
<!-- Frontend/Angular/Scheduling.AngularApp/src/app/app.html -->
<app-navbar />

<main class="content">
  <router-outlet></router-outlet>
</main>
```

### Conditional Content in Components

Use role checks from `AuthService` to show or hide actions based on user roles. The patient detail page is a good example — delete is admin-only, suspend/activate is doctor or admin:

**Component** (add `AuthService` and computed role signals):

```typescript
// Frontend/Angular/Scheduling.AngularApp/src/app/features/patients/patient-detail/patient-detail.ts
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatProgressSpinnerModule } from "@angular/material/progress-spinner";
import { DatePipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { Patient } from '@core/models/patient.model';
import { PatientApi } from '@core/services/patient-api';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { NotificationService } from '@core/services/notification';
import { AuthService } from '../../../core/services/auth';          // ← inject AuthService
import { AppRoles } from '../../../core/constants/approles';

@Component({
  selector: 'app-patient-detail',
  standalone: true,
  imports: [MatProgressSpinnerModule, DatePipe, MatCardModule, MatButtonModule, MatIconModule],
  templateUrl: './patient-detail.html',
  styleUrl: './patient-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PatientDetail implements OnInit {
  private patientService = inject(PatientApi);
  private route = inject(ActivatedRoute);
  private notification = inject(NotificationService);
  private authService = inject(AuthService);          // ← inject AuthService
  router = inject(Router);

  patient = signal<Patient | null>(null);
  isSuspended = computed(() => this.patient()!.status === 'Suspended');
  isDeleted = computed(() => this.patient()!.status === 'Deleted');
  isLoading = signal<boolean>(false);

  // Role-based visibility
  canDelete = computed(() => this.authService.hasRole(AppRoles.Admin));
  canSuspend = computed(() =>
    this.authService.hasRole(AppRoles.Doctor) || this.authService.hasRole(AppRoles.Admin));

  // ... ngOnInit, loadPatient, suspend, activate, delete methods unchanged
}
```

**Template** (wrap buttons with role checks):

```html
<!-- Frontend/Angular/Scheduling.AngularApp/src/app/features/patients/patient-detail/patient-detail.html -->
@if(isLoading()){
  <mat-spinner />
} @else if(patient(); as p) {
  <div class="detail-header">
    <h1>{{p.firstName}} {{p.lastName}}</h1>
    @if(!isDeleted() && canDelete()){
      <button mat-icon-button color="warn" (click)="delete()" aria-label="Delete patient">
        <mat-icon>delete</mat-icon>
      </button>
    }
  </div>

  <mat-card>
    <mat-card-content>
      <p><strong>Email:</strong> {{ p.email }}</p>
      <p><strong>Status:</strong> {{p.status}}</p>
      <p><strong>Date of birth</strong> {{p.dateOfBirth | date}}</p>
    </mat-card-content>
    <mat-card-actions>
      @if(!isDeleted() && canSuspend()){
        <button mat-flat-button color="warn" (click)="isSuspended() ? activate() : suspend()">
          {{isSuspended() ? 'Activate' : 'Suspend'}}
        </button>
      }
      <button mat-button (click)="router.navigate(['/patients'])">Back to list</button>
    </mat-card-actions>
  </mat-card>
}
```

> **Note**: The "Create patient" button in `PatientList` doesn't need a role check — per the [role matrix](./04-api-resource-protection.md), all authenticated users (Nurse, Doctor, Admin) can create patients. The backend `UserValidator<T>` enforces this as a safety net.

**Signal reactivity:**
- When `authService.user()` changes, computed signals like `canDelete()` automatically update
- No manual subscriptions needed
- Clean, declarative syntax with `@if` control flow
- Role logic stays in TypeScript; templates just call boolean signals

---

## Complete Authentication Flow

### Flow 1: First Visit (Not Logged In)

```
1. User navigates to Angular app (http://localhost:4200)
   │
   ├─> APP_INITIALIZER calls authService.checkAuth()
   │   │
   │   ├─> GET /auth/current-user (no cookie)
   │   │   │
   │   │   └─> 401 Unauthorized
   │   │
   │   └─> Auth interceptor catches 401
   │       │
   │       └─> authService.login()
   │           │
   │           └─> window.location.href = `${environment.schedulingApiUrl}/auth/login`
   │
   ├─> Browser redirects to API's /auth/login endpoint
   │   │
   │   └─> API redirects to Duende IdentityServer
   │
   ├─> User authenticates with IdentityServer
   │
   ├─> IdentityServer redirects back to /auth/callback
   │   │
   │   └─> API validates token, creates cookie, redirects to app
   │
   └─> Browser returns to Angular with authentication cookie
       │
       ├─> APP_INITIALIZER calls authService.checkAuth()
       │   │
       │   ├─> GET /auth/current-user (with cookie)
       │   │   │
       │   │   └─> 200 OK with UserInfo
       │   │
       │   └─> authService.currentUser.set(userInfo)
       │
       └─> App renders with user info and protected routes available
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
       ├─> Auth interceptor catches 401
       │   │
       │   ├─> authService.login() (preserves current URL)
       │   │   │
       │   │   └─> window.location.href = `${environment.schedulingApiUrl}/auth/login`
       │   │
       │   └─> Returns EMPTY — no error propagated
       │
       └─> User re-authenticates and returns to where they left off
```

### Flow 4: Logout

```
1. User clicks "Logout" button
   │
   ├─> authService.logout()
   │   │
   │   └─> window.location.href = `${environment.schedulingApiUrl}/auth/logout`
   │       (full page navigation, not AJAX — OIDC logout involves redirects)
   │
   ├─> Browser navigates to API's /auth/logout endpoint
   │   │
   │   ├─> API clears DDD.Auth cookie
   │   │
   │   └─> API redirects to IdentityServer's end-session endpoint
   │       │
   │       └─> IdentityServer clears its session cookie
   │           │
   │           └─> Redirects back to app (/)
   │
   └─> App restarts
       │
       ├─> APP_INITIALIZER calls authService.checkAuth()
       │   │
       │   ├─> GET /auth/current-user (no cookie — was cleared)
       │   │   │
       │   │   └─> 401 Unauthorized
       │   │
       │   └─> Auth interceptor catches 401
       │       │
       │       └─> authService.login()
       │           │
       │           └─> window.location.href = `${environment.schedulingApiUrl}/auth/login`
       │
       └─> User is redirected to login immediately
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

---

## Common Issues

### Route guards allow unauthenticated access / login endpoint hit twice on startup

**Symptom:** Unauthenticated users can navigate to protected routes (e.g. `/patients`). The API's `/auth/login` endpoint is hit twice on startup — once from `APP_INITIALIZER`'s `checkAuth()` and a second time from the interceptor inside a child component.

**Root cause:** `isAuthenticated` was defined as:

```typescript
isAuthenticated = computed(() => this.currentUser !== null);  // bug
```

`this.currentUser` is the signal reference (a function), which is never `null`. So `isAuthenticated()` always returned `true`. `authGuard` trusted that value and let every navigation through. Child components then made their own API calls, received 401s, and the interceptor called `authService.login()` a second time.

**Fix:** Invoke the signal inside `computed()`:

```typescript
isAuthenticated = computed(() => this.currentUser() !== null);  // correct
```

**Verify:** After the fix, navigating to a protected route without a valid cookie must redirect to login before any component is instantiated, and only a single `/auth/login` redirect should occur during startup.

---

## Summary

### What Angular Does

1. **Sends cookies** with every request (`withCredentials: true`)
2. **Checks auth state** via `/auth/current-user` endpoint
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
│   ├── models/
│   │   └── user-info.model.ts             # UserInfo interface
│   ├── constants/
│   │   └── approles.ts                    # Typed role constants
│   ├── services/
│   │   └── auth.ts                        # Auth state management with signals
│   ├── interceptors/
│   │   └── auth.interceptor.ts            # withCredentials + 401 handling
│   ├── guards/
│   │   └── auth.guard.ts                  # Route protection
│   └── layout/
│       └── navbar/
│           ├── navbar.ts                  # Login/logout UI
│           └── navbar.html
├── app.config.ts                           # Interceptor registration + APP_INITIALIZER
└── app.routes.ts                           # Route guards applied
```

### Benefits of This Architecture

✅ **Simple** - No OIDC library, minimal Angular code
✅ **Secure** - Tokens never exposed to JavaScript
✅ **Maintainable** - Auth logic centralized in API
✅ **Reactive** - Signals provide automatic UI updates
✅ **User-friendly** - Seamless redirects, forced login on startup

### Key Takeaways

- Inside `computed()` (and anywhere you need the current value), always invoke the signal with `()`. Reading the signal without `()` gives you the function reference — always truthy, never `null`.
- `asReadonly()` intentionally passes the signal reference through; consumers call it themselves. That is the only correct place to use a signal without `()` in the service.
- A silent `computed()` bug on `isAuthenticated` breaks route guards and causes double redirects on startup. See [Common Issues](#common-issues) for the full symptom chain.

---

> Next: [06-user-context-and-authorization.md](./06-user-context-and-authorization.md) - User Context in Domain Layer
