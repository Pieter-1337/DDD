# Angular — Consuming APIs

> **Track:** Angular frontend track. For the Blazor equivalent, see the Blazor documentation.

---

## Overview

This document covers how Angular applications consume backend APIs using HttpClient, including service architecture, RxJS patterns, CORS configuration, and error handling strategies. We'll implement the Patient service that communicates with the Scheduling.WebApi.

---

## How Angular Calls APIs

Angular uses the `HttpClient` service from `@angular/common/http` to make HTTP requests:

- **Returns RxJS Observables** — HTTP calls are lazy and don't execute until subscribed
- **CORS Configuration** — In development, CORS is configured on the backend to allow cross-origin requests from Angular
- **Environment Files** — In production, configure the base URL via environment files
- **Automatic JSON Parsing** — Request and response bodies are automatically serialized/deserialized

### HttpClient Setup

The `HttpClient` is provided in `app.config.ts`:

```typescript
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';

export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(withInterceptorsFromDi()),
    // ... other providers
  ]
};
```

---

## Patient Model

Define TypeScript interfaces that match the backend DTOs.

**File**: `src/app/core/models/patient.model.ts`

```typescript
/**
 * Patient entity returned from API
 */
export interface Patient {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  dateOfBirth: string;  // ISO 8601 date string
  status: string;       // "Active" | "Suspended"
}

/**
 * Request model for creating a new patient
 */
export interface CreatePatientRequest {
  firstName: string;
  lastName: string;
  email: string;
  dateOfBirth: string;  // yyyy-MM-dd format
}

/**
 * Response from CreatePatient command
 */
export interface CreatePatientResponse {
  success: boolean;
  patientId: string;
  errors?: string[];
}

/**
 * Query parameters for filtering patients
 */
export interface PatientFilterParams {
  status?: string;
}
```

**Key Points**:
- Match property names exactly to backend DTOs (case-sensitive)
- Use `string` for dates — convert to/from `Date` objects in components as needed
- Include optional properties with `?` for nullable fields
- Document interfaces with JSDoc comments for IDE IntelliSense

---

## Patient Service

Services in Angular are singletons that encapsulate API communication logic.

**File**: `src/app/core/services/patient.service.ts`

```typescript
import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  Patient,
  CreatePatientRequest,
  CreatePatientResponse,
  PatientFilterParams
} from '../models/patient.model';
import { environment } from '../../../environments/environment';

/**
 * Service for managing patient data via Scheduling.WebApi
 */
@Injectable({ providedIn: 'root' })
export class PatientService {
  private http = inject(HttpClient);
  private baseUrl = `${environment.schedulingApiUrl}/api/patients`;

  /**
   * Get all patients with optional filtering
   * @param params Optional filter parameters (e.g., status)
   * @returns Observable of patient array
   */
  getAll(params?: PatientFilterParams): Observable<Patient[]> {
    let httpParams = new HttpParams();

    if (params?.status) {
      httpParams = httpParams.set('status', params.status);
    }

    return this.http.get<Patient[]>(this.baseUrl, { params: httpParams });
  }

  /**
   * Get a single patient by ID
   * @param id Patient ID (GUID)
   * @returns Observable of patient
   */
  getById(id: string): Observable<Patient> {
    return this.http.get<Patient>(`${this.baseUrl}/${id}`);
  }

  /**
   * Create a new patient
   * @param request Patient creation data
   * @returns Observable of creation response
   */
  create(request: CreatePatientRequest): Observable<CreatePatientResponse> {
    return this.http.post<CreatePatientResponse>(this.baseUrl, request);
  }

  /**
   * Suspend a patient (change status to Suspended)
   * @param id Patient ID
   * @returns Observable of boolean indicating success
   */
  suspend(id: string): Observable<boolean> {
    return this.http.post<boolean>(`${this.baseUrl}/${id}/suspend`, null);
  }

  /**
   * Activate a patient (change status to Active)
   * @param id Patient ID
   * @returns Observable of boolean indicating success
   */
  activate(id: string): Observable<boolean> {
    return this.http.post<boolean>(`${this.baseUrl}/${id}/activate`, null);
  }
}
```

**Design Decisions**:
- Use `inject()` function instead of constructor injection (Angular 14+ style)
- All methods return `Observable<T>` — never subscribe inside the service
- Use `HttpParams` to build query strings safely
- Pass `null` as body for POST requests that don't require a body
- `providedIn: 'root'` makes the service a singleton

---

## RxJS Basics for API Calls

### What is an Observable?

An `Observable` is a lazy stream of values. HTTP calls emit one value (the response) then complete.

```typescript
// Observable is lazy — this does NOT make the HTTP call
const patients$ = this.patientService.getAll();

// Subscribing triggers the HTTP call
patients$.subscribe(patients => {
  console.log('Received patients:', patients);
});
```

### Common RxJS Operators

Use `pipe()` to chain operators that transform or handle the observable stream:

```typescript
import { map, catchError, tap } from 'rxjs/operators';
import { of } from 'rxjs';

this.patientService.getAll().pipe(
  tap(patients => console.log('Raw response:', patients)),  // Side effect (logging)
  map(patients => patients.filter(p => p.status === 'Active')),  // Transform
  catchError(error => {
    console.error('Failed to load patients', error);
    return of([]);  // Return empty array on error
  })
).subscribe(activePatients => {
  this.patients.set(activePatients);
});
```

| Operator | Purpose | Example |
|----------|---------|---------|
| `map()` | Transform each emitted value | `map(patients => patients.length)` |
| `tap()` | Side effects without changing value | `tap(data => console.log(data))` |
| `catchError()` | Handle errors and return fallback | `catchError(() => of([]))` |
| `switchMap()` | Map to new observable, cancel previous | Used for dependent requests |
| `filter()` | Emit only if predicate is true | `filter(p => p.status === 'Active')` |

### Subscribing in Components

Always unsubscribe to prevent memory leaks. Two common patterns:

**Pattern 1: Async Pipe (Recommended)**
```typescript
// Component
patients$ = this.patientService.getAll();

// Template
<div *ngFor="let patient of patients$ | async">
  {{ patient.firstName }}
</div>
```

**Pattern 2: Manual Subscription with Signal**
```typescript
export class PatientListComponent implements OnInit {
  patients = signal<Patient[]>([]);

  ngOnInit() {
    this.patientService.getAll().subscribe(
      patients => this.patients.set(patients)
    );
  }
}
```

---

## CORS Configuration

### Why CORS is Needed

- **Angular dev server**: Runs on `https://localhost:7003` (via Aspire)
- **Backend API**: Runs on an HTTPS port (check Aspire dashboard or `launchSettings.json`)
- **Different Origins**: Different protocols, domains, or ports = cross-origin request
- **Browser Security**: Browsers block cross-origin requests by default
- **Solution**: Configure CORS on the backend to allow requests from the Angular origin

### Configure CORS on Backend

Add CORS policy in `Program.cs` for both `Scheduling.WebApi` and `Billing.WebApi`:

```csharp
// Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("Angular", policy => policy
        .WithOrigins("https://localhost:7003")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// ... other service registrations

var app = builder.Build();

// Apply CORS middleware (before UseAuthorization)
app.UseCors("Angular");

// ... other middleware
```

**Key Points**:
- `WithOrigins("https://localhost:7003")` — Allow requests from Angular dev server
- `AllowAnyHeader()` — Allow all HTTP headers (Authorization, Content-Type, etc.)
- `AllowAnyMethod()` — Allow all HTTP methods (GET, POST, PUT, DELETE)
- `UseCors()` must be called **before** `UseAuthorization()` in the middleware pipeline
- Apply this configuration to **both** `Scheduling.WebApi` and `Billing.WebApi`

### Verify CORS is Working

1. Start backend API (via Aspire or directly)
2. Start Angular dev server: `ng serve`
3. Open browser console (F12)
4. Make an API call from Angular
5. Verify:
   - No CORS errors in console
   - Network tab shows successful responses
   - Response headers include `Access-Control-Allow-Origin: https://localhost:7003`

---

## Environment Configuration

For production builds, use environment files to configure the base URL.

### Development Environment

**File**: `src/environments/environment.ts`

```typescript
export const environment = {
  production: false,
  schedulingApiUrl: 'https://localhost:7001', // Scheduling.WebApi
  billingApiUrl: 'https://localhost:7002',    // Billing.WebApi
};
```

### Production Environment

**File**: `src/environments/environment.prod.ts`

```typescript
export const environment = {
  production: true,
  schedulingApiUrl: 'https://scheduling-api.yourdomain.com',
  billingApiUrl: 'https://billing-api.yourdomain.com',
};
```

### Use Environment in Service

```typescript
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class PatientService {
  private http = inject(HttpClient);
  private baseUrl = `${environment.schedulingApiUrl}/api/patients`;

  // ... methods
}
```

### Build for Production

```bash
# Build with production environment
ng build --configuration production

# Output will use environment.prod.ts values
```

---

## Angular HttpClient vs Blazor HttpClient

Understanding the differences helps when working in both ecosystems:

| Aspect | Blazor HttpClient | Angular HttpClient |
|--------|------------------|-------------------|
| **Language** | C# | TypeScript |
| **Return Type** | `Task<T>` | `Observable<T>` |
| **Lazy Execution** | No (executes immediately) | Yes (executes on subscribe) |
| **Registration** | `builder.Services.AddHttpClient<T>()` | `provideHttpClient()` in `app.config.ts` |
| **Base URL** | Aspire service discovery / configuration | Environment config + CORS (dev) / environment (prod) |
| **CORS** | No issues (server-to-server) | CORS configured on backend |
| **Error Handling** | try/catch | `catchError` operator |
| **Serialization** | System.Text.Json (automatic) | Automatic JSON parsing |
| **Streaming** | `IAsyncEnumerable<T>` | Observables support streaming naturally |
| **Cancellation** | `CancellationToken` | `takeUntil()` operator or unsubscribe |
| **Testing** | Mock with Moq | Mock with Jasmine/Jest spies |

**Key Insight**: Blazor's `Task` is eager (starts immediately), while Angular's `Observable` is lazy (starts on subscribe). This makes Observables more composable but requires explicit subscription.

---

## Error Handling Pattern

### Service-Level Error Handling

Create a reusable error handler:

**File**: `src/app/core/services/error-handler.service.ts`

```typescript
import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';

/**
 * Centralized error handling for HTTP requests
 */
@Injectable({ providedIn: 'root' })
export class ErrorHandlerService {
  /**
   * Handle HTTP errors and return user-friendly message
   * @param error HttpErrorResponse from Angular
   * @returns Observable that errors with a formatted message
   */
  handleError(error: HttpErrorResponse): Observable<never> {
    let message = 'An unexpected error occurred';

    if (error.error?.errors && Array.isArray(error.error.errors)) {
      // Validation errors from backend (FluentValidation)
      message = error.error.errors.join(', ');
    } else if (error.error?.message) {
      // Single error message from backend
      message = error.error.message;
    } else if (error.status === 404) {
      message = 'Resource not found';
    } else if (error.status === 401) {
      message = 'Unauthorized. Please log in.';
    } else if (error.status === 403) {
      message = 'You do not have permission to perform this action';
    } else if (error.status === 0) {
      message = 'Cannot reach the server. Please check your connection.';
    } else if (error.status >= 500) {
      message = 'A server error occurred. Please try again later.';
    }

    console.error('HTTP Error:', error);
    return throwError(() => new Error(message));
  }
}
```

### Using Error Handler in Service

```typescript
import { catchError } from 'rxjs/operators';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class PatientService {
  private http = inject(HttpClient);
  private errorHandler = inject(ErrorHandlerService);
  private baseUrl = `${environment.schedulingApiUrl}/api/patients`;

  getAll(params?: PatientFilterParams): Observable<Patient[]> {
    let httpParams = new HttpParams();
    if (params?.status) {
      httpParams = httpParams.set('status', params.status);
    }

    return this.http.get<Patient[]>(this.baseUrl, { params: httpParams }).pipe(
      catchError(error => this.errorHandler.handleError(error))
    );
  }

  // Apply to all methods...
}
```

### Displaying Errors in Component

```typescript
export class PatientListComponent implements OnInit {
  patients = signal<Patient[]>([]);
  errorMessage = signal<string>('');
  isLoading = signal<boolean>(false);

  ngOnInit() {
    this.loadPatients();
  }

  loadPatients() {
    this.isLoading.set(true);
    this.errorMessage.set('');

    this.patientService.getAll().subscribe({
      next: patients => {
        this.patients.set(patients);
        this.isLoading.set(false);
      },
      error: (error: Error) => {
        this.errorMessage.set(error.message);
        this.isLoading.set(false);
      }
    });
  }
}
```

**Template**:
```html
<div *ngIf="errorMessage()" class="alert alert-danger">
  {{ errorMessage() }}
</div>

<div *ngIf="isLoading()">Loading...</div>

<div *ngIf="!isLoading() && patients().length === 0">
  No patients found.
</div>
```

### Global Error Interceptor (Advanced)

For cross-cutting concerns like authentication or logging:

```typescript
import { HttpInterceptorFn } from '@angular/common/http';
import { catchError } from 'rxjs/operators';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    catchError(error => {
      // Log all errors to monitoring service
      console.error('Global error:', error);

      // Re-throw to let component handle it
      throw error;
    })
  );
};

// Register in app.config.ts
export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(
      withInterceptors([errorInterceptor])
    ),
  ]
};
```

---

## Complete Example: Patient List Component

Putting it all together:

**File**: `src/app/features/patients/patient-list/patient-list.component.ts`

```typescript
import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PatientService } from '../../../core/services/patient.service';
import { Patient } from '../../../core/models/patient.model';

@Component({
  selector: 'app-patient-list',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './patient-list.component.html',
  styleUrl: './patient-list.component.css',
})
export class PatientListComponent implements OnInit {
  private patientService = inject(PatientService);

  patients = signal<Patient[]>([]);
  errorMessage = signal<string>('');
  isLoading = signal<boolean>(false);

  ngOnInit() {
    this.loadPatients();
  }

  loadPatients() {
    this.isLoading.set(true);
    this.errorMessage.set('');

    this.patientService.getAll().subscribe({
      next: patients => {
        this.patients.set(patients);
        this.isLoading.set(false);
      },
      error: (error: Error) => {
        this.errorMessage.set(error.message);
        this.isLoading.set(false);
      }
    });
  }

  suspendPatient(id: string) {
    if (!confirm('Are you sure you want to suspend this patient?')) {
      return;
    }

    this.patientService.suspend(id).subscribe({
      next: () => this.loadPatients(),
      error: (error: Error) => this.errorMessage.set(error.message)
    });
  }

  activatePatient(id: string) {
    this.patientService.activate(id).subscribe({
      next: () => this.loadPatients(),
      error: (error: Error) => this.errorMessage.set(error.message)
    });
  }
}
```

**File**: `src/app/features/patients/patient-list/patient-list.component.html`

```html
<div class="container">
  <h2>Patients</h2>

  <div *ngIf="errorMessage()" class="alert alert-danger">
    {{ errorMessage() }}
  </div>

  <div *ngIf="isLoading()">
    <p>Loading patients...</p>
  </div>

  <table *ngIf="!isLoading() && patients().length > 0" class="table">
    <thead>
      <tr>
        <th>Name</th>
        <th>Email</th>
        <th>Date of Birth</th>
        <th>Status</th>
        <th>Actions</th>
      </tr>
    </thead>
    <tbody>
      <tr *ngFor="let patient of patients()">
        <td>{{ patient.firstName }} {{ patient.lastName }}</td>
        <td>{{ patient.email }}</td>
        <td>{{ patient.dateOfBirth | date }}</td>
        <td>
          <span [class]="'badge bg-' + (patient.status === 'Active' ? 'success' : 'secondary')">
            {{ patient.status }}
          </span>
        </td>
        <td>
          <button
            *ngIf="patient.status === 'Active'"
            (click)="suspendPatient(patient.id)"
            class="btn btn-sm btn-warning">
            Suspend
          </button>
          <button
            *ngIf="patient.status === 'Suspended'"
            (click)="activatePatient(patient.id)"
            class="btn btn-sm btn-success">
            Activate
          </button>
        </td>
      </tr>
    </tbody>
  </table>

  <div *ngIf="!isLoading() && patients().length === 0">
    <p>No patients found.</p>
  </div>
</div>
```

**File**: `src/app/features/patients/patient-list/patient-list.component.css`

```css
.container { padding: 2rem; }
.alert { margin-bottom: 1rem; }
```

---

## Verification Checklist

- [ ] Patient model interfaces defined (`patient.model.ts`)
- [ ] PatientService created with all CRUD methods
- [ ] All service methods return `Observable<T>`
- [ ] `inject()` function used instead of constructor injection
- [ ] CORS configured on backend APIs (`Program.cs`)
- [ ] GetAll works with and without status filter
- [ ] GetById returns patient data
- [ ] Create posts data and returns response with validation errors
- [ ] Suspend and Activate call correct endpoints
- [ ] Error handling implemented with `catchError`
- [ ] ErrorHandlerService created for reusable error handling
- [ ] Environment files configured for dev/prod
- [ ] Signals used in component for reactive state
- [ ] Loading and error states displayed in UI
- [ ] API requests succeed without CORS errors in browser console
- [ ] No CORS errors in browser console

---

## Navigation

- **Previous**: [02-angular-components-and-routing.md](./02-angular-components-and-routing.md)
- **Next**: [04-angular-state-management.md](./04-angular-state-management.md)
- **Up**: [Phase 7 Index](../README.md)
