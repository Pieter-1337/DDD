# Vue Project Setup

> **Track:** This is the Vue track. The Blazor, Angular, and Vue tracks all build the same patient management UI — choose whichever framework you want to learn. Building more than one enables the BFF pattern exploration in Phase 9.

## Purpose

This guide walks through setting up a Vue 3 single-page application to consume the Scheduling and Billing APIs. The Vue track builds the **same** patient management UI as the Angular track — identical endpoints, DTOs, and validation rules — but with a different, modern stack:

- **Vue 3** with the Composition API and `<script setup>` SFCs
- **Vite** as the build tool and dev server
- **PrimeVue 4** for UI components (the Vue counterpart to Angular Material)
- **Tailwind CSS v4** for utility-first styling (replacing per-component SCSS)
- **TanStack Query** for server-state management (replacing manual RxJS subscriptions)
- **VeeValidate + Zod** for typed, schema-driven form validation
- **Vue Router** for client-side navigation

By implementing the same UI in Vue you gain a third point of comparison: C# component model (Blazor) vs TypeScript with decorators and RxJS (Angular) vs TypeScript with reactive `ref`s and a query cache (Vue).

---

## Why Vue as an Alternative?

### Learning Value

| Aspect | Angular | Vue 3 |
|--------|---------|-------|
| **Component model** | Standalone components with `@Component` decorators | Single-file components (`.vue`) with `<script setup>` |
| **Reactivity** | Signals + RxJS observables | `ref` / `reactive` / `computed` |
| **Server state** | Manual `subscribe()` + reload | TanStack Query cache (auto refetch/invalidate) |
| **DI** | `inject()` + `@Injectable` | Plain ES module imports + composables |
| **UI library** | Angular Material | PrimeVue 4 |
| **Styling** | Component-scoped SCSS | Tailwind utility classes |
| **Build tool** | Angular CLI (esbuild) | Vite |
| **Forms** | Reactive Forms + Validators | VeeValidate + Zod schema |

### When This Matters

- **Lighter-weight SPA** — Vue + Vite has a smaller conceptual surface than Angular for small-to-medium apps
- **Server-state ergonomics** — TanStack Query removes most of the manual loading/error/refetch boilerplate the Angular doc writes by hand
- **Schema-first validation** — A Zod schema can mirror the backend FluentValidation rules in one place
- **BFF exploration** — A third frontend with different needs further motivates the Backend for Frontend pattern in Phase 9

---

## Prerequisites

Before starting, ensure you have:

- [ ] Node.js 20.19+ or 22.12+ installed (`node --version`) — required by Vite 6 / Tailwind v4
- [ ] npm or pnpm package manager
- [ ] Basic TypeScript knowledge (or willingness to learn)

---

## Step 1: Scaffold the Vue Project

Vue's official scaffolding tool is `create-vue` (Vite-based). Navigate to the `Frontend/Vue` directory and create the project.

```bash
cd C:\projects\DDD\DDD\Frontend\Vue

# Interactive scaffold
npm create vue@latest Scheduling.VueApp
```

### Scaffold Prompts

| Prompt | Recommended Choice | Purpose |
|--------|-------------------|---------|
| **TypeScript** | Yes | Type-safe models matching backend DTOs |
| **JSX Support** | No | We use SFC templates, not JSX |
| **Vue Router** | Yes | Client-side routing (counterpart to Angular Router) |
| **Pinia** | No | TanStack Query owns server state; local state uses `ref` |
| **Vitest** | Yes | Unit testing (matches the `.esproj` test convention) |
| **End-to-End Testing** | No (optional) | Skip for the learning project |
| **ESLint / Prettier** | Yes | Linting and formatting |

Then install dependencies:

```bash
cd Scheduling.VueApp
npm install
```

> **Note:** `create-vue` already configures Vite, the `@` → `src` path alias, and a `vite.config.ts`. We extend that config in the steps below rather than starting from scratch.

---

## Step 2: Add to .NET Solution

Like the Angular project, the Vue project is not a .NET project, but Visual Studio supports it via a `.esproj` file so it shows up in Solution Explorer under `05. Frontend > Vue`.

### Step 1: Create the `.esproj` file

The `.esproj` goes inside the Vue project folder, next to `package.json`.

**Terminal:**
```bash
touch Frontend/Vue/Scheduling.VueApp/Scheduling.VueApp.esproj
```

Paste this content:

```xml
<Project Sdk="Microsoft.VisualStudio.JavaScript.Sdk/1.0.4671869">
  <PropertyGroup>
    <StartupCommand>npm run dev</StartupCommand>
    <JavaScriptTestRoot>src/</JavaScriptTestRoot>
    <JavaScriptTestFramework>Vitest</JavaScriptTestFramework>
    <ShouldRunBuildScript>false</ShouldRunBuildScript>
    <PublishAssetsDirectory>$(DefaultItemExcludes);dist\</PublishAssetsDirectory>
  </PropertyGroup>
</Project>
```

**Note:** The SDK version must be the exact NuGet version — find the latest at [nuget.org/packages/Microsoft.VisualStudio.JavaScript.SDK](https://www.nuget.org/packages/Microsoft.VisualStudio.JavaScript.SDK) and use the full version number.

### Step 2: Add to Solution via Visual Studio

1. Open `DDD.sln` in Visual Studio
2. Right-click the **Vue** folder under **05. Frontend** in Solution Explorer
3. **Add → Existing Project**
4. Browse to `Frontend/Vue/Scheduling.VueApp/Scheduling.VueApp.esproj`

**Prerequisite:** The **Node.js development** workload must be installed via the Visual Studio Installer.

### What the `.esproj` does

| Property | Purpose |
|----------|---------|
| `StartupCommand` | Command VS runs when debugging (`npm run dev` runs `vite`) |
| `JavaScriptTestRoot` | Root directory for test discovery |
| `JavaScriptTestFramework` | `Vitest` — the Vue/Vite default test runner |
| `ShouldRunBuildScript` | `false` — Vite handles builds, skip on VS build |
| `PublishAssetsDirectory` | Where production build output goes (`dist/`) |

---

## Step 3: Add PrimeVue 4

[PrimeVue](https://primevue.org/) is a rich UI component library for Vue — the counterpart to Angular Material. PrimeVue 4 uses a **styled theming system** based on design-token presets (`@primeuix/themes`).

```bash
npm install primevue @primeuix/themes
```

> **Important:** PrimeVue 4 moved its theme presets to the `@primeuix/themes` package. (The older `@primevue/themes` name is PrimeVue 3-era — do not use it.)

### Register PrimeVue in `main.ts`

**File: `src/main.ts`**

```typescript
import { createApp } from 'vue';
import PrimeVue from 'primevue/config';
import Aura from '@primeuix/themes/aura';
import ToastService from 'primevue/toastservice';
import App from './App.vue';
import router from './router';
import './style.css';

const app = createApp(App);

app.use(router);
app.use(PrimeVue, {
  theme: {
    preset: Aura,
    options: {
      // Wrap PrimeVue styles in a CSS layer so Tailwind utilities can override
      // them predictably. Order matters — see Step 4.
      cssLayer: {
        name: 'primevue',
        order: 'theme, base, primevue',
      },
    },
  },
});
app.use(ToastService); // Powers the notification toasts (see doc 02)

app.mount('#app');
```

| Piece | Purpose |
|-------|---------|
| `PrimeVue` config | Registers the component library and the active theme preset |
| `Aura` preset | One of PrimeVue's built-in design-token themes (others: Lara, Nora, Material) |
| `cssLayer` | Places PrimeVue styles in a named CSS layer for clean Tailwind interop |
| `ToastService` | Global service that backs `useToast()` for success/error notifications |

PrimeVue 4 tree-shakes by default — import components per-SFC (e.g. `import Button from 'primevue/button'`) rather than registering them all globally.

---

## Step 4: Add Tailwind CSS v4

Tailwind v4 is configured through a **Vite plugin and a CSS import** — there is no `tailwind.config.js` by default (config moved into CSS).

```bash
npm install tailwindcss @tailwindcss/vite
npm install tailwindcss-primeui
```

| Package | Purpose |
|---------|---------|
| `tailwindcss` | The Tailwind v4 engine |
| `@tailwindcss/vite` | First-party Vite plugin (replaces the old PostCSS setup) |
| `tailwindcss-primeui` | Bridges PrimeVue's design tokens into Tailwind (adds `bg-primary`, `text-surface-*`, etc.) |

### Add the Vite plugin

**File: `vite.config.ts`** — add `tailwindcss()` to the plugins array:

```typescript
import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [
    vue(),
    tailwindcss(),
  ],
  // ... resolve.alias, server config added in later steps
});
```

### Import Tailwind and the PrimeUI bridge

**File: `src/style.css`** — replace the contents with:

```css
@import "tailwindcss";
@import "tailwindcss-primeui";
```

That's it — no `content` globbing or `tailwind.config.js` needed for v4. The PrimeVue `cssLayer` order from Step 3 (`theme, base, primevue`) ensures Tailwind utility classes win over PrimeVue's component styles when they collide, so you can do things like `<Button class="mt-4 w-full" />` and have the spacing/width utilities apply.

---

## Step 5: Add TanStack Query (Vue Query)

[TanStack Query](https://tanstack.com/query/latest/docs/framework/vue/overview) manages **server state** — fetching, caching, background refetching, and invalidation. It replaces the manual `subscribe()` + `loadPatients()` + loading-flag pattern the Angular doc writes by hand.

```bash
npm install @tanstack/vue-query
```

### Register the Vue Query plugin

**File: `src/main.ts`** — add the plugin with a shared `QueryClient`:

```typescript
import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000, // Treat data as fresh for 30s before background refetch
      retry: 1,
    },
  },
});

app.use(VueQueryPlugin, { queryClient });
```

We'll use `useQuery` for reads and `useMutation` (with `queryClient.invalidateQueries`) for writes in doc 02. The `QueryClient` is created once and provided app-wide.

---

## Step 6: Configure HTTPS with a Self-Signed Certificate

The backend APIs use HTTPS. To avoid mixed-content issues and match the Angular setup, run Vite over HTTPS using a locally-trusted certificate from [mkcert](https://github.com/FiloSottile/mkcert).

### Install mkcert and generate certificates

```bash
# Windows (via Chocolatey or Scoop)
choco install mkcert
mkcert -install

# From the Vue project root
cd Frontend/Vue/Scheduling.VueApp
mkdir certs
mkcert -cert-file certs/local-cert.pem -key-file certs/local-key.pem localhost "*.localhost"
```

### Configure Vite to use HTTPS

**File: `vite.config.ts`** — add a `server` block. Vite reads the cert files at startup:

```typescript
import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import tailwindcss from '@tailwindcss/vite';
import { fileURLToPath, URL } from 'node:url';
import { readFileSync } from 'node:fs';

export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      '@core': fileURLToPath(new URL('./src/core', import.meta.url)),
      '@features': fileURLToPath(new URL('./src/features', import.meta.url)),
      '@shared': fileURLToPath(new URL('./src/shared', import.meta.url)),
    },
  },
  server: {
    port: 7004,
    https: {
      key: readFileSync('certs/local-key.pem'),
      cert: readFileSync('certs/local-cert.pem'),
    },
  },
});
```

### Exclude certificates from Git

**File: `.gitignore`** — add to the Vue project's `.gitignore`:

```
# SSL certificates (generated locally via mkcert)
certs/*
!certs/README.md
```

Add a `certs/README.md` (same content as the Angular track) so other developers know how to regenerate their own certs.

---

## Step 7: Standardize the Vue Dev Port

The Angular track owns `https://localhost:7003`. The Vue app gets its **own** port so both can run side by side (the whole point of building multiple tracks for Phase 9).

| Service | HTTPS | HTTP |
|---------|-------|------|
| Scheduling.WebApi | `https://localhost:7001` | `http://localhost:5001` |
| Billing.WebApi | `https://localhost:7002` | `http://localhost:5002` |
| Angular | `https://localhost:7003` | - |
| **Vue** | **`https://localhost:7004`** | - |

The port is set in `vite.config.ts` (`server.port: 7004`, Step 6).

---

## Step 8: Configure CORS (Additive)

Vue runs on `https://localhost:7004`, a different origin from the APIs, so the backend must allow it via CORS. **Do not replace** the existing `"Angular"` policy — add the Vue origin alongside it so both frontends keep working.

### Option A: Add the Vue origin to the existing policy

**File: `WebApplications/Scheduling.WebApi/Program.cs`**

```csharp
builder.Services.AddCors(options =>
    options.AddPolicy("Spa", policy => policy
        .WithOrigins(
            "https://localhost:7003",  // Angular
            "https://localhost:7004")  // Vue
        .AllowAnyHeader()
        .AllowAnyMethod()));
```

```csharp
app.UseCors("Spa");
```

### Option B: A separate named policy per frontend

If you prefer explicit policies, add a second one rather than editing the first:

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("Angular", policy => policy
        .WithOrigins("https://localhost:7003").AllowAnyHeader().AllowAnyMethod());
    options.AddPolicy("Vue", policy => policy
        .WithOrigins("https://localhost:7004").AllowAnyHeader().AllowAnyMethod());
});
```

> **Note:** A request is only matched by a single CORS policy. If you keep separate policies, apply the right one per endpoint group; the simpler path for this learning project is **Option A** (one `"Spa"` policy listing both origins). Repeat whichever option you choose in `Billing.WebApi/Program.cs`.

> **Production:** Replace localhost origins with real frontend domains. Never use `.AllowAnyOrigin()`.

---

## Step 9: Environment Configuration

Vite exposes environment variables prefixed with `VITE_` via `import.meta.env`. This replaces Angular's `environment.ts` files.

### Development

**File: `.env.development`**

```
VITE_SCHEDULING_API_URL=https://localhost:7001
VITE_BILLING_API_URL=https://localhost:7002
```

### Production

**File: `.env.production`**

```
VITE_SCHEDULING_API_URL=https://scheduling-api.yourdomain.com
VITE_BILLING_API_URL=https://billing-api.yourdomain.com
```

### Typed access

To get IntelliSense and type-checking on `import.meta.env`, declare the variables:

**File: `src/env.d.ts`**

```typescript
interface ImportMetaEnv {
  readonly VITE_SCHEDULING_API_URL: string;
  readonly VITE_BILLING_API_URL: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
```

Usage in an API module:

```typescript
const baseUrl = `${import.meta.env.VITE_SCHEDULING_API_URL}/api/patients`;
```

---

## Step 10: Project Structure

The project grows to mirror the domain structure (and the Angular track's `core` / `features` / `shared` split) as you work through the subsequent docs. Create folders as you need them.

### End-State Folder Structure (Reference)

```
Frontend/Vue/Scheduling.VueApp/
├── src/
│   ├── core/                              # Singletons: API clients, models, query composables
│   │   ├── api/
│   │   │   ├── patient-api.ts             # Patient HTTP calls
│   │   │   └── billing-api.ts             # Billing HTTP calls
│   │   ├── models/
│   │   │   ├── patient.ts                 # Patient TypeScript interfaces
│   │   │   └── billing-profile.ts
│   │   └── composables/
│   │       ├── use-patients.ts            # TanStack Query hooks (queries + mutations)
│   │       └── use-notifications.ts       # Toast wrapper
│   ├── features/                          # Feature components (lazy-loaded via router)
│   │   └── patients/
│   │       ├── PatientList.vue
│   │       ├── PatientDetail.vue
│   │       └── CreatePatient.vue
│   ├── shared/                            # Reusable models, components
│   │   └── models/
│   │       └── success-or-failure-response.ts
│   ├── router/
│   │   └── index.ts                       # Route configuration
│   ├── App.vue                            # Root component (navbar + <router-view/>)
│   ├── main.ts                            # App bootstrap (plugins registered here)
│   ├── env.d.ts                           # import.meta.env typings
│   └── style.css                          # Tailwind + PrimeUI imports
├── certs/                                 # Local SSL certificates (gitignored)
├── .env.development                       # Dev API URLs
├── .env.production                        # Prod API URLs
├── index.html                             # HTML shell
├── vite.config.ts                         # Vite + plugins + alias + HTTPS
├── package.json
└── tsconfig.json
```

### Folder Conventions

| Folder | Purpose | Examples |
|--------|---------|----------|
| `core/` | Singletons — API clients, models, query composables | `patient-api.ts`, `use-patients.ts` |
| `features/` | Feature components and routes | `patients/PatientList.vue` |
| `shared/` | Reusable models and UI utilities | `success-or-failure-response.ts` |

Path aliases (`@core`, `@features`, `@shared`) were added in `vite.config.ts` (Step 6). Mirror them in `tsconfig.json` so the TypeScript language server resolves them too:

```json
{
  "compilerOptions": {
    "baseUrl": ".",
    "paths": {
      "@/*": ["src/*"],
      "@core/*": ["src/core/*"],
      "@features/*": ["src/features/*"],
      "@shared/*": ["src/shared/*"]
    }
  }
}
```

---

## Step 11: Register with Aspire

.NET Aspire orchestrates the Vue dev server alongside the APIs, just like the Angular app. Aspire injects a `PORT` environment variable that the dev server must read.

### Add an `start-aspire` script

**File: `package.json`** — Vite reads `--port`, and `--host` exposes it to Aspire's proxy:

```json
"scripts": {
  "dev": "vite",
  "start-aspire": "vite --port %PORT% --host",
  "build": "vue-tsc -b && vite build",
  "preview": "vite preview",
  "test:unit": "vitest"
}
```

### Add the JavaScript app to the AppHost

**File: `Aspire.AppHost/AppHost.cs`** — register the Vue app next to the Angular one:

```csharp
// Vue SPA — own port so it can run alongside the Angular SPA.
builder.AddJavaScriptApp("scheduling-vueapp", "../Frontend/Vue/Scheduling.VueApp", "start-aspire")
    .WithReference(schedulingApi)
    .WithReference(billingApi)
    .WithHttpsEndpoint(port: 7004, env: "PORT")
    .WithExternalHttpEndpoints();
```

> **Note:** `AddJavaScriptApp` (Aspire 13+) replaces the deprecated `AddNpmApp`. The third argument (`"start-aspire"`) is the npm script Aspire runs; it passes the Aspire-assigned `PORT` to Vite. The plain `dev` script (port 7004 from `vite.config.ts`) is for standalone use without Aspire.

The `Aspire.Hosting.JavaScript` package is already referenced for the Angular app — no new package is needed.

---

## Step 12: Verify Installation

### Start via Aspire (Recommended)

```bash
dotnet run --project Aspire.AppHost
```

The Aspire dashboard lists `scheduling-vueapp`. Click its URL to open the app.

### Or start standalone

```bash
cd Frontend/Vue/Scheduling.VueApp
npm run dev
```

Open `https://localhost:7004`.

### Smoke-test PrimeVue + Tailwind

**File: `src/App.vue`**

```vue
<script setup lang="ts">
import Button from 'primevue/button';
</script>

<template>
  <div class="p-6">
    <h1 class="text-2xl font-bold text-primary">Patient Management</h1>
    <Button label="Test Button" class="mt-4" />
  </div>
</template>
```

Expected result: a styled PrimeVue button renders, the heading uses Tailwind's `text-2xl font-bold` and the PrimeUI `text-primary` token, and the `mt-4` margin from Tailwind applies on top of PrimeVue's button styles — confirming the CSS-layer ordering works.

---

## Vue vs Angular Comparison (Project Setup)

### Setup Commands

| Task | Angular | Vue 3 |
|------|---------|-------|
| **Create Project** | `ng new Scheduling.AngularApp` | `npm create vue@latest Scheduling.VueApp` |
| **UI Library** | `ng add @angular/material` | `npm install primevue @primeuix/themes` |
| **Styling** | SCSS (per component) | `npm install tailwindcss @tailwindcss/vite` |
| **Server State** | RxJS (built into `HttpClient`) | `npm install @tanstack/vue-query` |
| **Start Dev Server** | `ng serve` | `npm run dev` (Vite) |

### Configuration Files

| Aspect | Angular | Vue 3 |
|--------|---------|-------|
| **Dependencies** | `package.json` | `package.json` |
| **Build Config** | `angular.json`, `tsconfig.json` | `vite.config.ts`, `tsconfig.json` |
| **App Bootstrap** | `app.config.ts` (providers) | `main.ts` (`app.use(...)` plugins) |
| **Env Config** | `environment.ts` files | `.env.*` + `import.meta.env` |
| **Dev Server** | Angular dev server (esbuild) | Vite |
| **Component Library** | Angular Material | PrimeVue 4 |

### Language & Paradigm

| Aspect | Angular | Vue 3 |
|--------|---------|-------|
| **Component Model** | Standalone components + decorators | SFC (`.vue`) with `<script setup>` |
| **Reactivity** | Signals + RxJS | `ref` / `reactive` / `computed` |
| **DI** | `inject()` + `@Injectable` | ES module imports + composables |
| **Two-Way Binding** | `[(ngModel)]` / reactive forms | `v-model` |

---

## Verification Checklist

### Project and Tooling

- [ ] Vue project scaffolded at `Frontend/Vue/Scheduling.VueApp` (`npm create vue@latest`)
- [ ] `npm install` completes without errors
- [ ] Dev server starts over HTTPS at `https://localhost:7004`

### PrimeVue + Tailwind

- [ ] `primevue` and `@primeuix/themes` installed; `PrimeVue` registered with Aura preset in `main.ts`
- [ ] `tailwindcss` + `@tailwindcss/vite` installed; plugin added to `vite.config.ts`
- [ ] `@import "tailwindcss"` and `@import "tailwindcss-primeui"` in `style.css`
- [ ] `cssLayer` order set to `theme, base, primevue`
- [ ] Smoke-test button renders styled with Tailwind utilities applying on top

### TanStack Query

- [ ] `@tanstack/vue-query` installed; `VueQueryPlugin` registered with a shared `QueryClient`

### CORS & Env

- [ ] Vue origin (`https://localhost:7004`) added to backend CORS **without removing** the Angular origin
- [ ] `.env.development` / `.env.production` define `VITE_SCHEDULING_API_URL` and `VITE_BILLING_API_URL`
- [ ] No CORS errors in the browser console when calling the API

### Aspire Integration

- [ ] `start-aspire` script added to `package.json`
- [ ] `AddJavaScriptApp("scheduling-vueapp", ...)` registered in `AppHost.cs` with port 7004
- [ ] Vue app appears in the Aspire dashboard alongside the Angular app

---

## Common Issues and Solutions

### Tailwind utilities don't override PrimeVue styles

**Symptom:** `<Button class="w-full" />` doesn't stretch.

**Solution:** Verify the PrimeVue `cssLayer.order` is exactly `theme, base, primevue`. The `primevue` layer must come **before** Tailwind's unlayered utilities so utilities win. Also confirm `@import "tailwindcss-primeui"` is present.

### `@primeuix/themes` not found

**Symptom:** Import error for the Aura preset.

**Solution:** PrimeVue 4 uses `@primeuix/themes` (not `@primevue/themes`). Reinstall with the correct package name.

### CORS error from the Vue origin

**Symptom:** `blocked by CORS policy` for `https://localhost:7004`.

**Solution:** Ensure `7004` is listed in the backend CORS policy (Step 8) and the API was restarted after the change.

---

## Next Steps

The next document covers consuming the backend APIs with TanStack Query:

1. **TypeScript models** matching the backend DTOs (shared with the Angular contract)
2. **API layer** — thin `fetch` wrappers per bounded context
3. **Query composables** — `useQuery` for reads, `useMutation` + `invalidateQueries` for writes
4. **Toast notifications** via PrimeVue's `useToast()`

---

## Summary

### What You Accomplished

- Scaffolded a Vue 3 + Vite SPA
- Added PrimeVue 4 with the Aura theme preset
- Wired Tailwind CSS v4 with the PrimeUI bridge and correct CSS-layer ordering
- Registered TanStack Query for server-state management
- Configured HTTPS, a dedicated port (7004), additive CORS, and environment variables
- Registered the Vue app with .NET Aspire alongside Angular

### Key Files Created

| File | Purpose |
|------|---------|
| `vite.config.ts` | Vite plugins (Vue, Tailwind), path aliases, HTTPS, port |
| `src/main.ts` | App bootstrap — PrimeVue, Tailwind CSS, Vue Query, Router, ToastService |
| `src/style.css` | Tailwind + PrimeUI imports |
| `.env.development` | Dev API base URLs |

---

## Navigation

- **Previous:** [../00-frontend-overview.md](../00-frontend-overview.md)
- **Next:** [02-vue-consuming-apis.md](./02-vue-consuming-apis.md)
