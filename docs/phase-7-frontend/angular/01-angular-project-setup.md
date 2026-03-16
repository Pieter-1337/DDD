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
- [ ] Basic TypeScript knowledge (or willingness to learn)

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

Navigate to the `Frontend/Angular` directory and create the Angular project with standalone components.

```bash
cd C:\projects\DDD\DDD\Frontend\Angular

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

## Step 3: Add to .NET Solution

Angular projects aren't .NET projects, but Visual Studio supports JavaScript/TypeScript projects via `.esproj` files. This lets the Angular project appear in Solution Explorer under the `05. Frontend > Angular` solution folder.

### Step 1: Create the `.esproj` file

The `.esproj` file goes inside the Angular project folder, next to `package.json`. Create it via VS Code or terminal:

**VS Code:** Open `Frontend/Angular/Scheduling.AngularApp/`, right-click in the Explorer panel → New File → name it `Scheduling.AngularApp.esproj`

**Terminal:**
```bash
touch Frontend/Angular/Scheduling.AngularApp/Scheduling.AngularApp.esproj
```

Paste this content:

```xml
<Project Sdk="Microsoft.VisualStudio.JavaScript.Sdk/1.0.4671869">
  <PropertyGroup>
    <StartupCommand>npm start</StartupCommand>
    <JavaScriptTestRoot>src/</JavaScriptTestRoot>
    <JavaScriptTestFramework>Vitest</JavaScriptTestFramework>
    <ShouldRunBuildScript>false</ShouldRunBuildScript>
    <PublishAssetsDirectory>$(DefaultItemExcludes);dist\</PublishAssetsDirectory>
  </PropertyGroup>
</Project>
```

**Note:** The SDK version must be the exact NuGet version, not just `1.0` — MSBuild can't resolve the short version for this SDK. Find the latest version at [nuget.org/packages/Microsoft.VisualStudio.JavaScript.SDK](https://www.nuget.org/packages/Microsoft.VisualStudio.JavaScript.SDK) — use the full version number shown on the page (e.g. `1.0.4671869`).

### Step 2: Add to Solution via Visual Studio

1. Open `DDD.sln` in Visual Studio
2. Right-click the **Angular** folder under **05. Frontend** in Solution Explorer
3. **Add → Existing Project**
4. Browse to `Frontend/Angular/Scheduling.AngularApp/Scheduling.AngularApp.esproj`

The Angular project now appears in Solution Explorer with full file browsing.

**Prerequisite:** The **Node.js development** workload must be installed via the Visual Studio Installer.

### What the `.esproj` does

The `.esproj` is a lightweight project file that tells Visual Studio how to handle the JavaScript project:

| Property | Purpose |
|----------|---------|
| `StartupCommand` | Command VS runs when debugging (`npm start` runs `ng serve`) |
| `JavaScriptTestRoot` | Root directory for test discovery |
| `JavaScriptTestFramework` | Angular 21+ uses Vitest by default (replaces Karma/Jasmine) |
| `ShouldRunBuildScript` | `false` — skip `npm run build` on VS build (Angular CLI handles builds) |
| `PublishAssetsDirectory` | Where production build output goes (`dist/`) |

---

## Step 4: Add Angular Material

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

**Result:** Angular Material is installed. In Angular 21, Material 3 theming is configured entirely in `styles.scss` (using `mat.theme()`) — no changes to `app.config.ts` are needed. Animations are built into the browser platform by default.

---

## Step 5: Project Structure

The Angular project will grow to mirror the domain structure as you work through the subsequent docs. **Do not create these folders manually** — the Angular CLI generates them when you use commands like `ng generate component` and `ng generate service` in later steps.

### End-State Folder Structure (Reference)

The tree below shows what the project will look like after completing all Phase 7 Angular docs:

```
Frontend/Angular/Scheduling.AngularApp/
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
│   │   ├── features/                       # Feature-specific components (lazy-loadable)
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

## Step 6: Configure CORS

Angular's dev server runs on a different port than the backend APIs (e.g., `http://localhost:4200` for Angular, `https://localhost:7001` for Scheduling API). This creates a cross-origin scenario that requires CORS (Cross-Origin Resource Sharing) configuration on the backend.

### Why CORS is Needed

When the Angular app on `http://localhost:4200` makes HTTP requests to the API on `https://localhost:7001`, browsers enforce the same-origin policy and block the requests unless the API explicitly allows cross-origin requests via CORS headers.

### Configure CORS in ASP.NET Core Backend

**File: `WebApplications/Scheduling.WebApi/Program.cs`**

Add the CORS policy before building the app:

```csharp
builder.Services.AddCors(options =>
    options.AddPolicy("Angular", policy => policy
        .WithOrigins("http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod()));
```

Then enable the CORS middleware in the request pipeline (before `app.MapControllers()`):

```csharp
app.UseCors("Angular");
```

**File: `WebApplications/Billing.WebApi/Program.cs`**

Repeat the same configuration for the Billing API.

### CORS Options Explained

| Option | Purpose |
|--------|---------|
| `.WithOrigins("http://localhost:4200")` | Allow requests from Angular dev server origin |
| `.AllowAnyHeader()` | Accept any HTTP headers (e.g., `Content-Type`, custom headers) |
| `.AllowAnyMethod()` | Accept any HTTP method (GET, POST, PUT, DELETE, etc.) |

### Production Considerations

**Note:** In production, replace `http://localhost:4200` with your actual frontend domain (e.g., `https://app.yourdomain.com`). Never use `.AllowAnyOrigin()` in production — always specify exact allowed origins.

---

## Step 7: Configure HttpClient

Angular's `HttpClient` is the standard way to make HTTP requests. Configure it as a global provider.

### Update Application Configuration

**File: `src/app/app.config.ts`**

```typescript
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(),
  ],
};
```

> **Note:** Angular 21 no longer requires `provideAnimationsAsync()` — it was deprecated in Angular 20.2. Angular Material now uses native CSS animations handled by the browser. No animation provider is needed.

### Provider Explanations

| Provider | Purpose |
|----------|---------|
| `provideBrowserGlobalErrorListeners` | Registers global error and unhandled rejection listeners |
| `provideRouter` | Configures Angular Router with defined routes |
| `provideHttpClient` | Enables `HttpClient` for dependency injection |

---

## Step 8: Environment Configuration

Configure environment-specific settings for API base URLs.

### Development Environment

**File: `src/environments/environment.ts`**

```typescript
export const environment = {
  production: false,
  schedulingApiUrl: 'https://localhost:7001', // Scheduling.WebApi
  billingApiUrl: 'https://localhost:7002',    // Billing.WebApi
};
```

### Production Environment

**File: `src/environments/environment.prod.ts`**

```typescript
export const environment = {
  production: true,
  schedulingApiUrl: 'https://scheduling-api.yourdomain.com', // Replace with actual URL
  billingApiUrl: 'https://billing-api.yourdomain.com',       // Replace with actual URL
};
```

### Usage in Services

```typescript
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class PatientService {
  private readonly baseUrl = `${environment.schedulingApiUrl}/api/patients`;
}
```

---

## Step 9: Register with Aspire (Optional)

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
        "../Frontend/Angular/Scheduling.AngularApp", "start")
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

**Recommendation:** Start with manual `ng serve` and CORS configuration. Add Aspire integration later if you want unified orchestration.

---

## Step 10: Verify Installation

### Start Development Server

```bash
cd C:\projects\DDD\DDD\Frontend\Angular\Scheduling.AngularApp
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

### Test API Connection

1. Open `http://localhost:4200` in browser
2. Open browser DevTools (F12) → Network tab
3. Navigate to a patient list page (once implemented)
4. Verify requests to `/api/patients` succeed with status 200
5. Check Console tab — no CORS errors should appear

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
| **API Client** | Typed `HttpClient` via DI | `HttpClient` + CORS |
| **Dev Server** | Kestrel (ASP.NET Core) | Webpack Dev Server (ng serve) |

### API Access

| Framework | Development | Production |
|-----------|-------------|------------|
| **Blazor** | Aspire service discovery (`https+http://scheduling-webapi`) | Configured via `appsettings.json` or env variables |
| **Angular** | CORS allows cross-origin requests to backend API | `environment.prod.ts` with full API URL |

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
- [ ] Angular project created at `C:\projects\DDD\DDD\Frontend\Angular\Scheduling.AngularApp`
- [ ] Angular Material installed and configured
- [ ] Dev server starts successfully (`ng serve`)
- [ ] App loads at `http://localhost:4200`

### CORS Configuration

- [ ] CORS configured in Scheduling.WebApi (`Program.cs`)
- [ ] CORS configured in Billing.WebApi (`Program.cs`)
- [ ] No CORS errors in browser console when calling API

### HttpClient Configuration

- [ ] `provideHttpClient()` added to `app.config.ts`
- [ ] No animation provider needed (Angular 21 uses native CSS animations)
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
- Verify CORS is configured in the backend API's `Program.cs`
- Ensure `app.UseCors("Angular")` is called before `app.MapControllers()`
- Check the allowed origin matches exactly (`http://localhost:4200`, not `https`)
- Restart the backend API after changing CORS configuration

### Issue: Self-Signed Certificate Errors

**Symptom:**
```
Error: unable to verify the first certificate
```

**Solution:**
- Trust the ASP.NET Core development certificate: `dotnet dev-certs https --trust`
- Restart your browser after trusting the certificate

### Issue: Angular Material Styles Not Applying

**Symptom:**
Material components render without styling.

**Solution:**
- Verify `@angular/material` is installed (`npm list @angular/material`)
- Check `styles.scss` imports the chosen theme:
  ```scss
  @import '@angular/material/prebuilt-themes/indigo-pink.css';
  ```
- Angular 21 uses native CSS animations — no animation provider needed

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
- Configured CORS for cross-origin API access during development
- Set up `HttpClient` for making HTTP requests
- (Optional) Registered Angular app with .NET Aspire

### Key Files Created

| File | Purpose |
|------|---------|
| `angular.json` | Angular CLI build and serve configuration |
| `src/app/app.config.ts` | Application-wide DI providers |
| `src/environments/environment.ts` | Environment-specific configuration |

### Blazor vs Angular Setup Comparison

| Aspect | Blazor Server | Angular |
|--------|--------------|---------|
| **CLI Command** | `dotnet new blazor` | `ng new --standalone` |
| **Component Library** | FluentUI Blazor (NuGet) | Angular Material (npm) |
| **API Access (Dev)** | Aspire service discovery | CORS configuration |
| **Dev Server** | Kestrel (port 5000-5999) | Webpack Dev Server (port 4200) |
| **Language** | C# | TypeScript |
| **Aspire Integration** | `AddProject<Projects.Blazor_App>()` | `AddNpmApp(name, path, script)` |

---

## Navigation

- **Previous:** [../00-frontend-overview.md](../00-frontend-overview.md)
- **Next:** [02-angular-components-and-routing.md](./02-angular-components-and-routing.md)
