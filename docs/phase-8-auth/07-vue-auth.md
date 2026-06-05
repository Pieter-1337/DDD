# Phase 8: Vue Authentication Integration

> Previous: [04-api-resource-protection.md](./04-api-resource-protection.md)
>
> **Track:** Vue frontend track. This is the Vue counterpart to [05-angular-auth.md](./05-angular-auth.md) — same cookie-based architecture and the **same backend endpoints**, expressed in Vue idioms (composables, `fetch`, Vue Router, PrimeVue).

This document explains how to integrate authentication into the Vue SPA. As with the Angular track, the API performs the entire OIDC flow and the browser carries an encrypted **HttpOnly cookie** — the SPA never sees a token. That keeps Vue's responsibilities tiny.

---

## Table of Contents

1. [Why Cookie-Based Auth Simplifies Vue](#why-cookie-based-auth-simplifies-vue)
2. [Architecture Overview](#architecture-overview)
3. [Vue's Responsibilities](#vues-responsibilities)
4. [Reusing the Backend Auth Endpoints](#reusing-the-backend-auth-endpoints)
5. [The API Fetch Wrapper (Interceptor Equivalent)](#the-api-fetch-wrapper-interceptor-equivalent)
6. [Auth State with a Composable](#auth-state-with-a-composable)
7. [Forced Login on Startup](#forced-login-on-startup)
8. [Route Guards](#route-guards)
9. [UI Integration](#ui-integration)
10. [Complete Authentication Flow](#complete-authentication-flow)
11. [Security Considerations](#security-considerations)
12. [Common Issues](#common-issues)
13. [Summary](#summary)

---

## Why Cookie-Based Auth Simplifies Vue

### Traditional SPA Authentication (What We're NOT Doing)

In a typical Vue SPA with OIDC you would reach for `oidc-client-ts` (or `vue-oidc-client`) and own the whole token lifecycle in JavaScript:

```typescript
// ❌ Traditional approach - NOT needed in our architecture
import { UserManager } from 'oidc-client-ts';

const userManager = new UserManager({
  authority: 'https://localhost:7010',
  client_id: 'vue-spa',
  redirect_uri: 'https://localhost:7004/callback',
  response_type: 'code',
  scope: 'openid profile scheduling-api',
});

// The SPA now manages:
// - PKCE code flow
// - Access / refresh / id token storage
// - Silent renew via hidden iframe
// - Token expiry + refresh timing
// - The XSS attack surface (any script can read the tokens)
```

**Complexity:**
- Install and configure an OIDC client library
- Implement the PKCE flow and the `/callback` route
- Store tokens (memory, `localStorage`, or `sessionStorage`)
- Implement silent renew and expiry handling
- Tokens are reachable from JavaScript (XSS risk)

### Cookie-Based Approach (What We ARE Doing)

```typescript
// ✅ Cookie-based approach - simple and secure
export function useAuth() {
  // The API handles OIDC. Vue just asks "who am I?" and triggers redirects.
  async function checkAuth() {
    currentUser.value = await apiFetch<UserInfo>(`${api}/auth/current-user`);
  }
  function login() {
    window.location.href = `${api}/auth/login`; // full-page redirect
  }
}
```

**Benefits:**
- ✅ No OIDC library needed
- ✅ No token management in JavaScript
- ✅ Tokens never exposed to the browser (XSS can't steal them)
- ✅ The browser sends the cookie automatically (`credentials: 'include'`)
- ✅ Far less code in Vue
- ✅ The API owns all authentication logic

This is the **identical** trade-off the Angular track makes — only the framework glue differs.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                       Vue SPA (Port 7004)                       │
│                                                                 │
│  ┌──────────────────┐      ┌─────────────────────────────────┐  │
│  │  useAuth()       │      │  apiFetch() wrapper             │  │
│  │  - checkAuth()   │      │  credentials: 'include'         │  │
│  │  - login()       │      │  X-Requested-With: XMLHttpRequest│ │
│  │  - logout()      │      │  (sends cookie, handles 401)    │  │
│  │  - hasRole()     │      └─────────────────────────────────┘  │
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
│  │ Auth Endpoints (from doc 03 — UNCHANGED, shared with Angular)    ││
│  │ - GET  /auth/current-user → returns user info from cookie        │|
│  │ - GET  /auth/login        → redirects to IdentityServer          │|
│  │ - GET  /auth/callback     → handles OIDC callback                ││
│  │ - POST /auth/logout       → clears cookie                        ││
│  └──────────────────────────────────────────────────────────────────┘│
│         │ OIDC flow                                                  │
│         ▼                                                            │
│  ┌──────────────────────────────────────────────────────────┐        │
│  │ OpenIdConnect Authentication Handler                     │        │
│  │ - Manages OIDC protocol                                  │        │
│  │ - Stores tokens securely in an encrypted cookie          │        │
│  └──────────────────────────────────────────────────────────┘        │
└──────────────────────────────────────────────────────────────────────┘
         │ OIDC protocol
         ▼
┌─────────────────────────────────────────────────────────────────┐
│    Duende IdentityServer / Auth Server (Port 7010)              │
│  - Login UI · User management · Token issuance                  │
└─────────────────────────────────────────────────────────────────┘
```

The only difference from the Angular diagram is the SPA port (`7004` instead of `7003`) and that Vue's "interceptor" is a `fetch` wrapper rather than an `HttpInterceptor`.

---

## Vue's Responsibilities

Vue has the same three responsibilities as Angular:

| Responsibility | Implementation | Why |
|----------------|----------------|-----|
| **Send cookies with requests** | `credentials: 'include'` in `apiFetch` | Browser sends the authentication cookie to the API |
| **Handle 401 responses** | `apiFetch` redirects to `/auth/login` | Session expired or not logged in |
| **Check auth state** | Call `/auth/current-user` | Get current user info and roles |

No token management, no PKCE, no refresh logic.

---

## Reusing the Backend Auth Endpoints

**There are no backend changes in this document.** The `/auth/current-user`, `/auth/login`, `/auth/callback`, and `/auth/logout` endpoints, the encrypted cookie, the `X-Requested-With` → 401 behaviour, and the `UserInfo` shape were all built for the Angular track in [doc 03](./03-shared-auth-infrastructure.md). The Vue SPA consumes the **same** endpoints. The only backend prerequisite is that the Vue origin (`https://localhost:7004`) is allowed by CORS with credentials — added additively in [Vue doc 01, Step 8](../phase-7-frontend/vue/01-vue-project-setup.md).

Define the `UserInfo` model — identical contract to Angular's `user-info.model.ts`:

```typescript
// src/core/models/user-info.ts

/** User information returned from GET /auth/current-user */
export interface UserInfo {
  userId: string;
  email: string;
  name: string;
  roles: string[];
  isAuthenticated: boolean;
}
```

And typed role constants:

```typescript
// src/core/constants/app-roles.ts

export const AppRoles = {
  Admin: 'Admin',
  Doctor: 'Doctor',
} as const;
```

---

## The API Fetch Wrapper (Interceptor Equivalent)

The Angular track does two things in an `HttpInterceptor`: attach `withCredentials` + `X-Requested-With` to every request, and redirect to login on 401. Vue's API layer (Vue [doc 02](../phase-7-frontend/vue/02-vue-consuming-apis.md)) uses bare `fetch`, so there is no interceptor to hook. **The single chokepoint is a small `apiFetch` wrapper** that every API function routes through — it is the Vue counterpart to the Angular interceptor.

### A leaf navigation helper (avoids an import cycle)

The 401 redirect is just a full-page navigation — it needs no reactive state. Put it in a dependency-free module that both `apiFetch` and the auth composable can import. This keeps the import graph one-directional (`use-auth → apiFetch → auth-navigation`) and avoids a cycle:

```typescript
// src/core/auth/auth-navigation.ts
const schedulingApiUrl = import.meta.env.VITE_SCHEDULING_API_URL;

/** Full-page redirect to the API login endpoint (starts the OIDC flow). */
export function redirectToLogin(): void {
  window.location.href = `${schedulingApiUrl}/auth/login`;
}

/** Full-page redirect to the API logout endpoint (clears cookie + ends OIDC session). */
export function redirectToLogout(): void {
  const returnUrl = encodeURIComponent(window.location.origin);
  window.location.href = `${schedulingApiUrl}/auth/logout?returnUrl=${returnUrl}`;
}
```

### The wrapper

```typescript
// src/core/api/api-fetch.ts
import { redirectToLogin } from '@core/auth/auth-navigation';

/**
 * Central fetch wrapper — the Vue counterpart to Angular's HTTP interceptor.
 * - Sends the auth cookie on every request (credentials: 'include')
 * - Identifies the call as AJAX (X-Requested-With) so the API returns 401
 *   instead of 302-redirecting to IdentityServer (which fails under CORS)
 * - On 401, redirects to login and never resolves (the page is navigating away)
 * - Throws on other non-2xx so TanStack Query routes them to the error state
 */
export async function apiFetch<T>(input: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(input, {
    ...init,
    credentials: 'include',
    headers: {
      'X-Requested-With': 'XMLHttpRequest',
      ...init.headers,
    },
  });

  if (response.status === 401) {
    redirectToLogin();
    // Suspend the promise — the browser is leaving this page, so there is no
    // point resolving or rejecting (the Vue equivalent of Angular's `return EMPTY`).
    return new Promise<T>(() => {});
  }

  if (!response.ok) {
    throw new Error(`Request failed: ${response.status} ${response.statusText}`);
  }

  // 204 No Content has no body to parse.
  return (response.status === 204 ? undefined : await response.json()) as T;
}
```

**Why `X-Requested-With`:** the API's `OnRedirectToIdentityProvider` handler checks this header and returns 401 instead of a 302 to IdentityServer — a 302 cross-origin redirect would fail a `fetch`/CORS request. Same mechanism as the Angular interceptor.

### Update doc 02's API layer to route through `apiFetch`

The `patient-api.ts` you wrote in Vue doc 02 uses raw `fetch` with a local `json()` helper and **no credentials** — so the cookie is never sent. Replace those calls with `apiFetch`. (Angular introduced `withCredentials` in this auth doc too, not in its consuming-APIs doc — so this is the same sequencing.)

```typescript
// src/core/api/patient-api.ts
import type {
  Patient,
  CreatePatientRequest,
  CreatePatientResponse,
  PatientFilterParams,
} from '@core/models/patient';
import type { SuccessOrFailureResponse } from '@shared/models/success-or-failure-response';
import { apiFetch } from '@core/api/api-fetch';

const baseUrl = `${import.meta.env.VITE_SCHEDULING_API_URL}/api/patients`;

export const patientApi = {
  getAll(params?: PatientFilterParams): Promise<Patient[]> {
    const query = params?.status ? `?status=${encodeURIComponent(params.status)}` : '';
    return apiFetch<Patient[]>(`${baseUrl}${query}`);
  },

  getById(id: string): Promise<Patient> {
    return apiFetch<Patient>(`${baseUrl}/${id}`);
  },

  create(request: CreatePatientRequest): Promise<CreatePatientResponse> {
    return apiFetch<CreatePatientResponse>(baseUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
  },

  suspend(id: string): Promise<SuccessOrFailureResponse> {
    return apiFetch<SuccessOrFailureResponse>(`${baseUrl}/${id}/suspend`, { method: 'POST' });
  },

  activate(id: string): Promise<SuccessOrFailureResponse> {
    return apiFetch<SuccessOrFailureResponse>(`${baseUrl}/${id}/activate`, { method: 'POST' });
  },

  delete(id: string): Promise<SuccessOrFailureResponse> {
    return apiFetch<SuccessOrFailureResponse>(`${baseUrl}/${id}`, { method: 'DELETE' });
  },
};
```

The local `json()` helper from doc 02 is now redundant — `apiFetch` owns the status check and parsing. With this in place, **every** query and mutation carries the cookie and inherits the 401-redirect behaviour for free.

---

## Auth State with a Composable

Angular's `AuthService` is a singleton because it's `@Injectable({ providedIn: 'root' })`. Vue has no DI container; the idiomatic singleton is a composable whose reactive state is declared at **module scope** — created once when the module is first imported and shared by every caller. (Pinia would be the "official" store, but it isn't in this track's stack; a module-scoped composable is the lightweight equivalent — see the [production note](#common-issues).)

```typescript
// src/core/composables/use-auth.ts
import { ref, computed } from 'vue';
import { apiFetch } from '@core/api/api-fetch';
import { redirectToLogin, redirectToLogout } from '@core/auth/auth-navigation';
import type { UserInfo } from '@core/models/user-info';

const schedulingApiUrl = import.meta.env.VITE_SCHEDULING_API_URL;

// MODULE-SCOPED state: declared ONCE, outside useAuth(), so every component that
// calls useAuth() shares the same reactive state — the counterpart to Angular's
// root-provided AuthService. Declaring these INSIDE the function would give each
// caller its own copy and break shared auth state (see Common Issues).
const currentUser = ref<UserInfo | null>(null);
const loading = ref(true);

const user = computed(() => currentUser.value);
const isAuthenticated = computed(() => currentUser.value !== null);
const isLoading = computed(() => loading.value);

/**
 * Resolve auth state by calling /auth/current-user.
 * Called once at startup (see Forced Login) and after returning from login.
 *
 * On 401, apiFetch has already redirected to /auth/login and this code is never
 * reached. We swallow any OTHER error (network/500) so the app can still mount
 * with currentUser = null instead of leaving a blank page.
 */
async function checkAuth(): Promise<void> {
  loading.value = true;
  try {
    currentUser.value = await apiFetch<UserInfo>(`${schedulingApiUrl}/auth/current-user`);
  } catch {
    currentUser.value = null;
  } finally {
    loading.value = false;
  }
}

/** True if the current user holds the given role. */
function hasRole(role: string): boolean {
  return currentUser.value?.roles.includes(role) ?? false;
}

export function useAuth() {
  return {
    user,                    // readonly computed<UserInfo | null>
    isAuthenticated,         // readonly computed<boolean>
    isLoading,               // readonly computed<boolean>
    checkAuth,
    login: redirectToLogin,
    logout: redirectToLogout,
    hasRole,
  };
}
```

**Why module scope matters:** the `ref`s live at the top of the module, not inside `useAuth()`. Vue's module system imports a module once, so all components observe the **same** `currentUser`. Components read `.value` in script and bind the computeds directly in templates (Vue auto-unwraps refs in templates — no `.value` there).

---

## Forced Login on Startup

Angular uses `provideAppInitializer(() => inject(AuthService).checkAuth())` to resolve auth before the app renders. Vue's equivalent is to **`await checkAuth()` before `app.mount()`** in `main.ts`. If there's no valid cookie, `apiFetch` catches the 401 inside `checkAuth` and redirects to `/auth/login` — so `app.mount()` is never reached for an anonymous user.

Wrap the existing `main.ts` bootstrap (PrimeVue, ToastService, router, VueQueryPlugin — all registered in Vue doc 01) in an async function:

```typescript
// src/main.ts
import { createApp } from 'vue';
import App from './App.vue';
import router from './router';
import { useAuth } from '@core/composables/use-auth';
// + PrimeVue / ToastService / VueQueryPlugin imports and styles from doc 01

async function bootstrap() {
  const app = createApp(App);

  app.use(router);
  // app.use(PrimeVue, { ... });  // from doc 01
  // app.use(ToastService);       // from doc 01
  // app.use(VueQueryPlugin);     // from doc 01

  // Forced login: resolve auth state BEFORE mounting. With no valid cookie,
  // apiFetch redirects to /auth/login inside checkAuth and mount never runs.
  await useAuth().checkAuth();

  app.mount('#app');
}

bootstrap();
```

**Result:** users are always authenticated before the app renders. There is no "logged out" state in the SPA — exactly like the Angular track. Because auth state is fully resolved here, the route guards below can be **synchronous**.

---

## Route Guards

Vue Router uses a global `beforeEach` guard plus per-route `meta`, in place of Angular's `CanActivateFn`. First, declare the route-meta shape so `requiresAuth` and `role` are typed:

```typescript
// src/router/index.ts
import 'vue-router';

declare module 'vue-router' {
  interface RouteMeta {
    requiresAuth?: boolean;
    role?: string;
  }
}
```

Add `meta` to the protected routes (extending the router from Vue doc 03) and a `/forbidden` page:

```typescript
import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router';
import { useAuth } from '@core/composables/use-auth';

const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/patients' },
  {
    path: '/patients',
    name: 'patient-list',
    component: () => import('@features/patients/PatientList.vue'),
    meta: { requiresAuth: true },
  },
  {
    path: '/patients/create',
    name: 'create-patient',
    component: () => import('@features/patients/CreatePatient.vue'),
    meta: { requiresAuth: true },
  },
  {
    path: '/patients/:id',
    name: 'patient-detail',
    component: () => import('@features/patients/PatientDetail.vue'),
    props: true,
    meta: { requiresAuth: true },
  },
  {
    path: '/forbidden',
    name: 'forbidden',
    component: () => import('@features/auth/Forbidden.vue'),
  },
];

const router = createRouter({
  history: createWebHistory(),
  routes,
});

// Global guard — runs before every navigation. Auth state is already resolved
// at bootstrap (main.ts), so this can be synchronous.
router.beforeEach((to) => {
  const { isAuthenticated, hasRole, login } = useAuth();

  if (!to.meta.requiresAuth) return true;

  if (!isAuthenticated.value) {
    login();        // full-page redirect to /auth/login
    return false;   // cancel the in-app navigation
  }

  if (to.meta.role && !hasRole(to.meta.role)) {
    return { name: 'forbidden' };  // authenticated but missing the role
  }

  return true;
});

export default router;
```

- `meta.requiresAuth` replaces applying `authGuard` to each route.
- `meta.role` (with the `beforeEach` check) replaces Angular's `roleGuard(role)` factory.
- Returning `false` cancels navigation; returning a location object redirects (e.g. to `/forbidden`).

> The guard reads `isAuthenticated.value` synchronously and trusts it because `checkAuth()` already ran in `main.ts`. This mirrors Angular, where `APP_INITIALIZER` resolves before any guard evaluates.

A minimal forbidden page:

```vue
<!-- src/features/auth/Forbidden.vue -->
<script setup lang="ts">
import { useRouter } from 'vue-router';
import Button from 'primevue/button';
const router = useRouter();
</script>

<template>
  <div class="py-12 text-center">
    <h1 class="text-2xl font-bold mb-2">403 — Forbidden</h1>
    <p class="mb-4">You don't have permission to view this page.</p>
    <Button label="Back to patients" @click="router.push('/patients')" />
  </div>
</template>
```

---

## UI Integration

Show login/logout UI from the shared auth state, and gate role-specific actions.

### Navbar (App.vue)

Extend the `Menubar` from Vue doc 03 with a user section. PrimeVue's `Menubar` exposes an `#end` slot:

```vue
<!-- src/App.vue -->
<script setup lang="ts">
import Toast from 'primevue/toast';
import Menubar from 'primevue/menubar';
import Button from 'primevue/button';
import Menu from 'primevue/menu';
import ProgressSpinner from 'primevue/progressspinner';
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import { useAuth } from '@core/composables/use-auth';

const router = useRouter();
const { user, isAuthenticated, isLoading, login, logout } = useAuth();

const items = [
  { label: 'Patients', icon: 'pi pi-users', command: () => router.push('/patients') },
];

// PrimeVue Menu is toggled imperatively from the user button.
const userMenu = ref();
const userMenuItems = [{ label: 'Logout', icon: 'pi pi-sign-out', command: () => logout() }];
</script>

<template>
  <Toast />
  <Menubar :model="items" class="mb-4">
    <template #start>
      <span class="font-bold text-primary px-2">🏥 Patient Management</span>
    </template>

    <template #end>
      <ProgressSpinner v-if="isLoading" style="width: 1.5rem; height: 1.5rem" />

      <template v-else-if="isAuthenticated">
        <Button text @click="(e) => userMenu.toggle(e)">
          <i class="pi pi-user mr-2" />
          {{ user?.name }}
          <i class="pi pi-angle-down ml-2" />
        </Button>
        <Menu ref="userMenu" :model="userMenuItems" :popup="true" />
      </template>

      <Button v-else label="Login" @click="login" />
    </template>
  </Menubar>

  <main class="px-6">
    <router-view />
  </main>
</template>
```

> In practice the `Login` button is rarely seen — forced login redirects anonymous users before the app renders. It's kept for completeness and for the brief window after logout.

### Role-Gated Actions (PatientDetail.vue)

Per the [role matrix](./04-api-resource-protection.md): delete is **Admin-only**; suspend/activate is **Doctor or Admin**; all authenticated users can create. Add computed role checks to the detail component from Vue doc 03:

```vue
<script setup lang="ts">
import { computed } from 'vue';
// ... existing imports from doc 03 (usePatient, mutations, useNotifications, router) ...
import { useAuth } from '@core/composables/use-auth';
import { AppRoles } from '@core/constants/app-roles';

const { hasRole } = useAuth();

// Role-based visibility — recomputes automatically if auth state changes.
const canDelete = computed(() => hasRole(AppRoles.Admin));
const canSuspend = computed(() => hasRole(AppRoles.Doctor) || hasRole(AppRoles.Admin));

// ... existing patient query, isSuspended/isDeleted, mutation handlers ...
</script>
```

Then gate the buttons in the template with `v-if` (additions shown in **bold** context):

```vue
<template>
  <!-- ... loading spinner ... -->
  <template v-else-if="patient">
    <div class="flex items-center gap-2 mb-4">
      <h1 class="text-2xl font-bold">{{ patient.firstName }} {{ patient.lastName }}</h1>
      <Button
        v-if="!isDeleted && canDelete"
        icon="pi pi-trash" severity="danger" text rounded
        aria-label="Delete patient" :loading="remove.isPending" @click="deletePatient"
      />
    </div>

    <Card>
      <template #content><!-- ... patient fields ... --></template>
      <template #footer>
        <div class="flex gap-2">
          <Button
            v-if="!isDeleted && canSuspend"
            :label="isSuspended ? 'Activate' : 'Suspend'"
            severity="warn" :loading="suspend.isPending || activate.isPending" @click="toggleStatus"
          />
          <Button label="Back to list" text @click="router.push('/patients')" />
        </div>
      </template>
    </Card>
  </template>
</template>
```

> The "Create patient" button in `PatientList` needs no role check — all authenticated roles can create, and the backend `UserValidator<T>` enforces this as the authoritative safety net.

**Reactivity:** because `canDelete` / `canSuspend` are `computed` over the module-scoped auth state, they update automatically if the user ever changes. Role logic stays in the script; the template just reads booleans.

---

## Complete Authentication Flow

### Flow 1: First Visit (Not Logged In)

```
1. User opens the Vue app (https://localhost:7004)
   │
   ├─> main.ts bootstrap() awaits useAuth().checkAuth()
   │   │
   │   ├─> GET /auth/current-user (no cookie)  →  401 Unauthorized
   │   │
   │   └─> apiFetch sees 401 → redirectToLogin()
   │       │
   │       └─> window.location.href = `${api}/auth/login`   (app.mount never runs)
   │
   ├─> API redirects to Duende IdentityServer → user authenticates
   │
   ├─> IdentityServer → /auth/callback → API creates cookie → redirects to app
   │
   └─> Browser returns to Vue WITH the cookie
       │
       ├─> bootstrap() awaits checkAuth() again
       │   └─> GET /auth/current-user (with cookie) → 200 OK + UserInfo
       │       └─> currentUser.value = userInfo
       │
       └─> app.mount() renders; protected routes are now reachable
```

### Flow 2: Protected Route Access

```
1. User navigates to /patients (meta.requiresAuth)
   │
   ├─> router.beforeEach runs
   │   ├─> isAuthenticated.value === true  → allow
   │   └─> false → login() (full-page redirect) and cancel navigation
   │   └─> authenticated but meta.role not held → redirect to /forbidden
   │
   └─> Component mounts; queries call apiFetch (cookie attached) → data returns
```

### Flow 3: Session Expiration

```
1. User is active; cookie expires
   │
   └─> Any query/mutation → apiFetch → GET /api/... → 401
       │
       ├─> apiFetch calls redirectToLogin()
       │   └─> promise never resolves (no error toast — page is leaving)
       │
       └─> User re-authenticates and returns
```

### Flow 4: Logout

```
1. User clicks Logout → logout() → window.location.href = `${api}/auth/logout?returnUrl=...`
   │  (full-page navigation — OIDC logout involves redirects, not AJAX)
   │
   ├─> API clears the auth cookie → redirects to IdentityServer end-session
   │   └─> IdentityServer clears its session → redirects back to the app
   │
   └─> App reloads → bootstrap() → checkAuth() → 401 → redirectToLogin()
       └─> User lands on the login page
```

---

## Security Considerations

### 1. Why Cookies Beat Tokens in `localStorage`

| Storage | XSS Risk | CSRF Risk | Best Practice |
|---------|----------|-----------|---------------|
| **localStorage** | ❌ High — JS can read tokens | ✅ Low | Don't store sensitive tokens |
| **sessionStorage** | ❌ High — JS can read tokens | ✅ Low | Don't store sensitive tokens |
| **HttpOnly Cookie** | ✅ Low — JS cannot read | ❌ Medium — mitigated by SameSite | ✅ **Recommended** |

**Our implementation:** the cookie is `HttpOnly` (invisible to JS), `Secure` (HTTPS only), and `SameSite=Lax` (CSRF mitigation). Even if XSS injects a script into the Vue app, it cannot read the authentication cookie.

### 2. CORS Configuration

The API must allow credentials from the Vue origin. This was added **additively** alongside Angular's origin (see Vue doc 01):

```csharp
options.AddPolicy("Spa", policy =>
{
    policy.WithOrigins("https://localhost:7003", "https://localhost:7004") // Angular + Vue
          .AllowCredentials()   // REQUIRED for cookies
          .AllowAnyHeader()
          .AllowAnyMethod();
});
```

**Critical:** `AllowCredentials()` must be set for `credentials: 'include'` to work, and the origin must be listed explicitly (a wildcard `*` is illegal with credentials).

---

## Common Issues

### Auth state isn't shared — login button shows even when logged in

**Symptom:** one component thinks the user is authenticated, another doesn't; the navbar shows "Login" right after a successful `checkAuth()`.

**Root cause:** the reactive state was declared **inside** `useAuth()`:

```typescript
export function useAuth() {
  const currentUser = ref<UserInfo | null>(null);  // ❌ a NEW ref per caller
  // ...
}
```

Every component that calls `useAuth()` gets its own `currentUser`, so `checkAuth()` in `main.ts` updates a copy that the navbar never sees. This is the Vue analogue of the Angular doc's `isAuthenticated` signal bug — both come from accidentally creating per-instance state where a singleton was intended.

**Fix:** declare the `ref`s at **module scope**, outside `useAuth()` (as shown above). The module is imported once, so all callers share the same state.

### Cookie never sent → infinite redirect to login

**Symptom:** you log in successfully, return to the app, and are immediately bounced back to `/auth/login`.

**Root cause:** an API call used bare `fetch` (or omitted `credentials: 'include'`), so the browser didn't attach the cookie; the API returned 401 and `apiFetch` redirected. Usually a call that wasn't migrated off doc 02's raw `fetch`.

**Fix:** route **every** API call through `apiFetch`. Grep for `fetch(` in `src/core` — only `apiFetch` itself should call `fetch` directly.

### Blank page instead of login after a network/500 error at startup

**Symptom:** the app shows nothing on load when the API is down.

**Root cause:** `checkAuth()` rethrew a non-401 error, so the `await` in `bootstrap()` rejected and `app.mount()` never ran.

**Fix:** `checkAuth()` must swallow non-401 errors (set `currentUser = null`) so bootstrap always reaches `app.mount()`. On 401, `apiFetch` has already navigated away, so that path doesn't reach the `catch`.

---

## Summary

### What Vue Does

1. **Sends cookies** with every request (`credentials: 'include'` in `apiFetch`)
2. **Checks auth state** via `/auth/current-user`
3. **Redirects to login** on 401 (centralised in `apiFetch`)
4. **Protects routes** with a `beforeEach` guard + route `meta`
5. **Shows/hides UI** from shared, reactive auth state

### What Vue Does NOT Do

1. ❌ Manage tokens (the API does)
2. ❌ Implement the OIDC flow (the API does)
3. ❌ Handle token refresh (browser + API do)
4. ❌ Store sensitive data in JavaScript (the cookie is HttpOnly)

### Angular ↔ Vue Mapping

| Concern | Angular | Vue |
|---------|---------|-----|
| **HTTP cross-cutting** | `HttpInterceptor` (`withCredentials`, `X-Requested-With`, 401) | `apiFetch` wrapper |
| **Auth state** | `@Injectable` `AuthService` + signals | module-scoped `useAuth()` composable + `ref`/`computed` |
| **Singleton mechanism** | DI `providedIn: 'root'` | module-scoped state (or Pinia in production) |
| **Startup check** | `provideAppInitializer(checkAuth)` | `await checkAuth()` before `app.mount()` |
| **Route protection** | `authGuard` / `roleGuard(role)` | `beforeEach` + `meta.requiresAuth` / `meta.role` |
| **Forbidden redirect** | `router.navigate(['/forbidden'])` | `return { name: 'forbidden' }` |
| **Login/logout** | `window.location.href = …` | `window.location.href = …` (identical) |
| **Role check in UI** | `computed(() => authService.hasRole(...))` | `computed(() => hasRole(...))` |
| **Reactive value access** | always invoke signal `()` | `.value` in script, auto-unwrapped in template |

### Key Files Created / Changed

```
Frontend/Vue/Scheduling.VueApp/src/
├── core/
│   ├── models/
│   │   └── user-info.ts                  # UserInfo interface
│   ├── constants/
│   │   └── app-roles.ts                  # Typed role constants
│   ├── auth/
│   │   └── auth-navigation.ts            # redirectToLogin / redirectToLogout (leaf)
│   ├── api/
│   │   ├── api-fetch.ts                  # NEW — credentials + X-Requested-With + 401
│   │   └── patient-api.ts                # CHANGED — routes through apiFetch
│   └── composables/
│       └── use-auth.ts                   # module-scoped auth state (singleton)
├── features/
│   └── auth/
│       └── Forbidden.vue                 # 403 page
├── router/
│   └── index.ts                          # CHANGED — meta + beforeEach guard
├── App.vue                               # CHANGED — navbar user menu / login-logout
└── main.ts                               # CHANGED — await checkAuth() before mount
```

### Benefits of This Architecture

✅ **Simple** — no OIDC library, minimal Vue code
✅ **Secure** — tokens never exposed to JavaScript
✅ **Maintainable** — auth logic centralised in the API; one `apiFetch` chokepoint on the client
✅ **Reactive** — module-scoped `ref`/`computed` update the UI automatically
✅ **Consistent** — same backend endpoints and trade-offs as the Angular track

### Key Takeaways

- Declare composable auth state at **module scope** to get a singleton; declaring it inside the function gives each caller its own copy (the Vue analogue of the Angular signal-invocation bug).
- Give 401 **one owner** (`apiFetch`) and have `checkAuth()` swallow other errors, so startup always reaches `app.mount()` and you never get a double redirect to login.
- Route **every** API call through `apiFetch` — the cookie only flows when `credentials: 'include'` is set.

---

> Next: [06-user-context-and-authorization.md](./06-user-context-and-authorization.md) — User Context in the Domain Layer
