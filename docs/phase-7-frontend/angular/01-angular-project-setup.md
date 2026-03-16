# Angular Project Setup

> **Track:** This is the Angular track. Both Blazor and Angular build the same patient management UI — choose whichever framework you want to learn. Building both enables the BFF pattern exploration in Phase 8.

## Purpose

This guide walks through setting up an Angular standalone component application to consume the Scheduling and Billing APIs. By implementing the same patient management UI in both Blazor and Angular, you gain:

- **Framework comparison** - C# component model vs TypeScript declarative templates
- **API design validation** - Same REST APIs consumed by different clients
- **BFF pattern foundation** - Two frontends enable future exploration of Backend for Frontend patterns
- **TypeScript experience** - Complements C# skills with modern JavaScript ecosystem

---

## Why Angular as an Alternative?

### Learning Value

| Aspect | Blazor Server | Angular |
|--------|--------------|---------|
| **Language** | C# (familiar) | TypeScript (new paradigm) |
| **Rendering** | Server-side via SignalR | Client-side SPA |
| **State Flow** | Scoped services, event callbacks | RxJS observables, signals |
| **Component Model** | Code-behind classes | Standalone components with decorators |
| **Build Output** | .NET app with Kestrel | Static bundle (JS/CSS) |

### When This Matters

- **Multi-platform teams** - Some teams prefer TypeScript, others C#
- **BFF exploration** - Different frontends justify different aggregation backends
- **Career growth** - Understanding both paradigms makes you a stronger architect
- **Deployment flexibility** - SPAs can be hosted on CDNs, Blazor Server requires app servers

---

## Prerequisites

Before starting, ensure you have:

- [ ] Node.js 20+ installed (`node --version`)
- [ ] npm or pnpm package manager
- [ ] Backend APIs running via Aspire (Scheduling.WebApi, Billing.WebApi)
- [ ] Basic TypeScript knowledge (or willingness to learn)

### Verify Backend APIs

```bash
# Check Aspire dashboard (typically http://localhost:15000)
# Verify Scheduling.WebApi is running (e.g., https://localhost:7001)
# Verify Billing.WebApi is running (e.g., https://localhost:7002)
```

---

## Step 1: Install Angular CLI

The Angular CLI is the standard toolchain for creating and managing Angular projects.

```bash
# Install globally (one-time setup)
npm install -g @angular/cli

# Verify installation
ng version
```

Expected output:
```
Angular CLI: 19.x.x
Node: 20.x.x
Package Manager: npm 10.x.x
```

---

## Step 2: Create Angular Project

Navigate to the `WebApplications` directory and create the Angular project with standalone components.

```bash
cd C:\projects\DDD\DDD\WebApplications

# Create project with options:
# - Standalone components (modern Angular)
# - SCSS for styling
# - Routing enabled
# - SSR disabled (SPA mode)
ng new Scheduling.AngularApp --style=scss --routing=true --ssr=false --standalone=true
```

### Project Creation Options

| Option | Value | Purpose |
|--------|-------|---------|
| `--standalone` | `true` | Use standalone components (no NgModules) |
| `--style` | `scss` | SASS for advanced CSS features |
| `--routing` | `true` | Enable Angular Router |
| `--ssr` | `false` | Client-side only (no server-side rendering) |

---

## Step 3: Add Angular Material

Angular Material provides pre-built UI components following Material Design principles.

```bash
cd Scheduling.AngularApp
ng add @angular/material
```

### Configuration Prompts

| Prompt | Recommended Choice | Notes |
|--------|-------------------|-------|
| **Theme** | Indigo/Pink or Azure Blue | Choose a pre-built theme or custom |
| **Typography** | Yes | Sets global Material typography styles |
| **Animations** | Yes | Enables Angular animations module |

**Result:** Angular Material is installed and configured in `app.config.ts`.

---

## Step 4: Project Structure

Organize the Angular project to mirror the domain structure and follow Angular best practices.

### Recommended Folder Structure

```
WebApplications/Scheduling.AngularApp/
├── src/
│   ├── app/
│   │   ├── core/                           # Singleton services, core logic
│   │   │   ├── services/
│   │   │   │   ├── patient.service.ts      # Patient API client
│   │   │   │   └── billing.service.ts      # Billing API client
│   │   │   ├── models/
│   │   │   │   ├── patient.model.ts        # Patient TypeScript interfaces
│   │   │   │   └── billing-profile.model.ts
│   │   │   └── interceptors/
│   │   │       └── error.interceptor.ts    # Global HTTP error handling
│   │   ├── features/                       # Feature modules (lazy-loadable)
│   │   │   └── patients/
│   │   │       ├── patient-list/
│   │   │       │   ├── patient-list.component.ts
│   │   │       │   ├── patient-list.component.html
│   │   │       │   └── patient-list.component.scss
│   │   │       ├── patient-detail/
│   │   │       │   ├── patient-detail.component.ts
│   │   │       │   ├── patient-detail.component.html
│   │   │       │   └── patient-detail.component.scss
│   │   │       └── create-patient/
│   │   │           ├── create-patient.component.ts
│   │   │           ├── create-patient.component.html
│   │   │           └── create-patient.component.scss
│   │   ├── shared/                         # Reusable components, directives, pipes
│   │   │   ├── components/
│   │   │   │   └── loading-spinner/
│   │   │   └── pipes/
│   │   │       └── date-format.pipe.ts
│   │   ├── app.component.ts                # Root component
│   │   ├── app.component.html
│   │   ├── app.routes.ts                   # Route configuration
│   │   └── app.config.ts                   # DI providers
│   ├── environments/
│   │   ├── environment.ts                  # Development config
│   │   └── environment.prod.ts             # Production config
│   ├── proxy.conf.json                     # Dev server API proxy
│   ├── styles.scss                         # Global styles
│   └── index.html                          # HTML shell
├── angular.json                            # Angular CLI config
├── package.json                            # npm dependencies
└── tsconfig.json                           # TypeScript config
```

### Folder Conventions

| Folder | Purpose | Examples |
|--------|---------|----------|
| `core/` | Singleton services, guards, interceptors | `PatientService`, `AuthGuard` |
| `features/` | Feature-specific components and routes | `patients/`, `appointments/` |
| `shared/` | Reusable UI components and utilities | `LoadingSpinnerComponent`, `DatePipe` |

---

## Step 5: Configure API Proxy

Angular's dev server runs on a different port than the backend APIs (e.g., `http://localhost:4200` for Angular, `https://localhost:7001` for Scheduling API). To avoid CORS issues during development, configure a proxy to forward API requests.

### Create Proxy Configuration

**File: `src/proxy.conf.json`**

```json
{
  "/api": {
    "target": "https://localhost:7001",
    "secure": false,
    "changeOrigin": true,
    "logLevel": "debug"
  }
}
```

### Proxy Options Explained

| Option | Value | Purpose |
|--------|-------|---------|
| `target` | `https://localhost:7001` | Backend API base URL (Scheduling.WebApi) |
| `secure` | `false` | Accept self-signed SSL certificates in dev |
| `changeOrigin` | `true` | Change `Host` header to match target |
| `logLevel` | `debug` | Log proxy requests to console |

### Update Angular CLI Configuration

**File: `angular.json`**

```json
{
  "projects": {
    "Scheduling.AngularApp": {
      "architect": {
        "serve": {
          "options": {
            "proxyConfig": "src/proxy.conf.json"
          }
        }
      }
    }
  }
}
```

### Proxy Flow

```
Browser request to http://localhost:4200/api/patients
        |
        v
Angular dev server intercepts /api/* requests
        |
        v
Proxy forwards to https://localhost:7001/api/patients
        |
        v
Scheduling.WebApi responds
        |
        v
Response returned to browser
```

**Production Note:** In production, replace the proxy with environment-specific API base URLs in `environment.prod.ts`.

---

## Step 6: Configure HttpClient

Angular's `HttpClient` is the standard way to make HTTP requests. Configure it as a global provider.

### Update Application Configuration

**File: `src/app/app.config.ts`**

```typescript
import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(withInterceptorsFromDi()), // Enable HttpClient
    provideAnimationsAsync(),                     // Enable Angular Material animations
  ],
};
```

### Provider Explanations

| Provider | Purpose |
|----------|---------|
| `provideZoneChangeDetection` | Enables Angular's change detection with optimizations |
| `provideRouter` | Configures Angular Router with defined routes |
| `provideHttpClient` | Enables `HttpClient` for dependency injection |
| `withInterceptorsFromDi()` | Allows classic class-based interceptors |
| `provideAnimationsAsync` | Enables Angular Material animations (lazy-loaded) |

---

## Step 7: Environment Configuration

Configure environment-specific settings for API base URLs.

### Development Environment

**File: `src/environments/environment.ts`**

```typescript
export const environment = {
  production: false,
  apiBaseUrl: '', // Empty string uses proxy.conf.json in dev
};
```

### Production Environment

**File: `src/environments/environment.prod.ts`**

```typescript
export const environment = {
  production: true,
  apiBaseUrl: 'https://api.yourdomain.com', // Replace with actual production API URL
};
```

### Usage in Services

```typescript
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class PatientService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/patients`;
  // In dev: '' + '/api/patients' = '/api/patients' (proxied)
  // In prod: 'https://api.yourdomain.com' + '/api/patients'
}
```

---

## Step 8: Register with Aspire (Optional)

.NET Aspire can orchestrate the Angular dev server alongside .NET services. This is optional but provides a unified development experience.

### Add NPM App to AppHost

**File: `Aspire.AppHost/AppHost.cs`**

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Existing services
var rabbitmq = builder.AddRabbitMQ("rabbitmq");
var schedulingApi = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-webapi")
    .WithReference(rabbitmq);
var billingApi = builder.AddProject<Projects.Billing_WebApi>("billing-webapi")
    .WithReference(rabbitmq);

// Add Angular app (optional)
var angularApp = builder.AddNpmApp("scheduling-angularapp",
        "../WebApplications/Scheduling.AngularApp", "start")
    .WithReference(schedulingApi)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

### NPM App Options

| Method | Purpose |
|--------|---------|
| `AddNpmApp(name, path, scriptName)` | Registers an npm-based app (runs `npm run start`) |
| `.WithReference(schedulingApi)` | Injects service discovery for the API |
| `.WithHttpEndpoint(env: "PORT")` | Exposes the Angular dev server HTTP endpoint |
| `.WithExternalHttpEndpoints()` | Allows external access (browser) |

### Aspire vs Manual Dev Server

| Approach | When to Use |
|----------|------------|
| **Aspire orchestration** | Unified dashboard, service discovery, one `F5` to start everything |
| **Manual `ng serve`** | Simpler setup, faster iteration, already familiar workflow |

**Recommendation:** Start with manual `ng serve` and proxy configuration. Add Aspire integration later if you want unified orchestration.

---

## Step 9: Verify Installation

### Start Development Server

```bash
cd C:\projects\DDD\DDD\WebApplications\Scheduling.AngularApp
ng serve
```

Expected output:
```
✔ Browser application bundle generation complete.
Initial Chunk Files | Names         |  Raw Size
main.js             | main          | 250.45 kB |
styles.css          | styles        |  75.23 kB |

Application bundle generation complete. [2.345 seconds]
Watch mode enabled. Watching for file changes...
➜ Local:   http://localhost:4200/
```

### Test API Proxy

1. Open `http://localhost:4200` in browser
2. Open browser DevTools (F12) → Network tab
3. Navigate to a patient list page (once implemented)
4. Verify requests to `/api/patients` are proxied to `https://localhost:7001/api/patients`

### Test Angular Material

Create a simple test component with a Material button:

**File: `src/app/app.component.html`**

```html
<mat-toolbar color="primary">
  <span>Patient Management</span>
</mat-toolbar>

<div style="padding: 20px;">
  <button mat-raised-button color="accent">Test Button</button>
</div>
```

**File: `src/app/app.component.ts`**

```typescript
import { Component } from '@angular/core';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [MatToolbarModule, MatButtonModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent {
  title = 'Scheduling.AngularApp';
}
```

Expected result: A Material Design toolbar and button render correctly.

---

## Blazor vs Angular Comparison (Project Setup)

### Setup Commands

| Task | Blazor Server | Angular |
|------|--------------|---------|
| **Create Project** | `dotnet new blazor` | `ng new Scheduling.AngularApp` |
| **Add UI Library** | `dotnet add package Microsoft.FluentUI.AspNetCore.Components` | `ng add @angular/material` |
| **Start Dev Server** | `dotnet run` or F5 | `ng serve` |

### Configuration Files

| Aspect | Blazor Server | Angular |
|--------|--------------|---------|
| **Dependencies** | `*.csproj` (NuGet packages) | `package.json` (npm packages) |
| **Build Config** | `*.csproj`, `appsettings.json` | `angular.json`, `tsconfig.json` |
| **API Client** | Typed `HttpClient` via DI | `HttpClient` + proxy.conf.json |
| **Dev Server** | Kestrel (ASP.NET Core) | Webpack Dev Server (ng serve) |

### API Access

| Framework | Development | Production |
|-----------|-------------|------------|
| **Blazor** | Aspire service discovery (`https+http://scheduling-webapi`) | Configured via `appsettings.json` or env variables |
| **Angular** | `proxy.conf.json` proxies `/api/*` to backend | `environment.prod.ts` with full API URL |

### Language & Paradigm

| Aspect | Blazor Server | Angular |
|--------|--------------|---------|
| **Language** | C# 12 | TypeScript 5.x |
| **Component Model** | Razor components (`.razor` files) | Standalone components with decorators |
| **State Management** | Scoped services, `StateHasChanged()` | RxJS observables, signals |
| **Two-Way Binding** | `@bind` directive | `[(ngModel)]` or reactive forms |
| **Dependency Injection** | ASP.NET Core DI | Angular DI with `@Injectable()` |

### Build Output

| Framework | Output | Deployment |
|-----------|--------|-----------|
| **Blazor Server** | ASP.NET Core app (Kestrel required) | IIS, Azure App Service, Kubernetes |
| **Angular** | Static files (HTML/CSS/JS bundle) | nginx, CDN, Azure Static Web Apps |

---

## Verification Checklist

Before proceeding to the next document, verify:

### Angular CLI and Project

- [ ] Angular CLI installed (`ng version` works)
- [ ] Angular project created at `C:\projects\DDD\DDD\WebApplications\Scheduling.AngularApp`
- [ ] Angular Material installed and configured
- [ ] Dev server starts successfully (`ng serve`)
- [ ] App loads at `http://localhost:4200`

### API Proxy

- [ ] `proxy.conf.json` created with correct target URL
- [ ] `angular.json` updated to use proxy configuration
- [ ] Proxy logs show requests forwarded to backend (check terminal)

### HttpClient Configuration

- [ ] `provideHttpClient()` added to `app.config.ts`
- [ ] `provideAnimationsAsync()` added for Material animations
- [ ] No console errors on page load

### Backend Availability

- [ ] Scheduling.WebApi running and accessible (e.g., `https://localhost:7001`)
- [ ] Billing.WebApi running and accessible (e.g., `https://localhost:7002`)
- [ ] Scalar UI available for testing APIs (`/scalar/v1`)

### Optional: Aspire Integration

- [ ] (If using Aspire) `AddNpmApp()` registered in `AppHost.cs`
- [ ] (If using Aspire) Angular app appears in Aspire dashboard
- [ ] (If using Aspire) Aspire dashboard shows Angular app as healthy

---

## Common Issues and Solutions

### Issue: CORS Errors in Browser Console

**Symptom:**
```
Access to fetch at 'https://localhost:7001/api/patients' from origin 'http://localhost:4200'
has been blocked by CORS policy
```

**Solution:**
- Verify `proxy.conf.json` is configured correctly
- Ensure `angular.json` references the proxy config
- Restart `ng serve` after changing proxy configuration

### Issue: Self-Signed Certificate Errors

**Symptom:**
```
Error: unable to verify the first certificate
```

**Solution:**
- Set `"secure": false` in `proxy.conf.json`
- Alternatively, trust the development certificate in your OS

### Issue: Angular Material Styles Not Applying

**Symptom:**
Material components render without styling.

**Solution:**
- Verify `@angular/material` is installed (`npm list @angular/material`)
- Check `styles.scss` imports the chosen theme:
  ```scss
  @import '@angular/material/prebuilt-themes/indigo-pink.css';
  ```
- Ensure `provideAnimationsAsync()` is in `app.config.ts`

### Issue: `ng serve` Fails to Start

**Symptom:**
```
Error: ENOENT: no such file or directory
```

**Solution:**
- Ensure you're in the correct directory (`Scheduling.AngularApp`)
- Run `npm install` to restore dependencies
- Delete `node_modules` and run `npm install` again if corruption suspected

---

## Next Steps

Now that the Angular project is set up, the next document will cover:

1. **Creating patient components** - List, detail, and create patient pages
2. **Configuring routing** - Angular Router with lazy-loaded modules
3. **Material UI integration** - Tables, forms, buttons, and navigation
4. **Component architecture** - Smart vs presentational components

---

## Summary

### What You Accomplished

- Installed Angular CLI and created a standalone component project
- Added Angular Material UI library
- Configured API proxy for CORS-free local development
- Set up `HttpClient` for making HTTP requests
- (Optional) Registered Angular app with .NET Aspire

### Key Files Created

| File | Purpose |
|------|---------|
| `angular.json` | Angular CLI build and serve configuration |
| `src/app/app.config.ts` | Application-wide DI providers |
| `src/proxy.conf.json` | Dev server proxy for backend API |
| `src/environments/environment.ts` | Environment-specific configuration |

### Blazor vs Angular Setup Comparison

| Aspect | Blazor Server | Angular |
|--------|--------------|---------|
| **CLI Command** | `dotnet new blazor` | `ng new --standalone` |
| **Component Library** | FluentUI Blazor (NuGet) | Angular Material (npm) |
| **API Access (Dev)** | Aspire service discovery | proxy.conf.json |
| **Dev Server** | Kestrel (port 5000-5999) | Webpack Dev Server (port 4200) |
| **Language** | C# | TypeScript |
| **Aspire Integration** | `AddProject<Projects.Blazor_App>()` | `AddNpmApp(name, path, script)` |

---

## Navigation

- **Previous:** [../00-frontend-overview.md](../00-frontend-overview.md)
- **Next:** [02-angular-components-and-routing.md](./02-angular-components-and-routing.md)
