# Angular Components and Routing

> **Track:** Angular frontend track.

This document covers Angular's component model, routing configuration, and page implementations for the Patient management UI. It demonstrates standalone components (Angular 17+ default), signal-based reactivity, and lazy-loaded routes.

---

## Angular Component Model

Angular components are self-contained units consisting of a TypeScript class (`.ts`), HTML template (`.html`), and SCSS styles (`.scss`) in separate files. Angular 17+ defaults to **standalone components**, eliminating the need for NgModules.

### Component Anatomy

```typescript
import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-example',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './example.html',
  styleUrl: './example.scss',
})
export class Example {
  title = signal('Hello Angular');   // Signal-based reactive state
}
```

**example.html**:
```html
<h1>{{ title() }}</h1>
```

**example.scss**:
```scss
h1 { color: blue; }
```

### Key Decorator Properties

| Property | Purpose | Example |
|----------|---------|---------|
| `selector` | Component HTML tag name | `'app-patient-list'` |
| `standalone` | Enable standalone mode (no NgModule) | `true` |
| `imports` | Components/modules used in template | `[MatTableModule, FormsModule]` |
| `templateUrl` | External HTML template file | `'./example.html'` |
| `styleUrl` | External SCSS stylesheet file | `'./example.scss'` |
| `template` | Inline HTML template (for trivial cases) | `` `<h1>Hello</h1>` `` |
| `styles` | Inline SCSS styles (for trivial cases) | `['h1 { color: red; }']` |

### Component Lifecycle Hooks

```typescript
import { Component, OnInit, OnDestroy } from '@angular/core';

export class My implements OnInit, OnDestroy {
  ngOnInit() {
    // Called after component initialization
    // Ideal for loading data, subscriptions
  }

  ngOnDestroy() {
    // Called before component destruction
    // Clean up subscriptions, timers
  }
}
```

### Component Communication

```typescript
import { Component, Input, Output, EventEmitter } from '@angular/core';

@Component({
  selector: 'app-child',
  templateUrl: './child.html',
})
export class Child {
  @Input() data!: string;                    // Parent -> Child
  @Output() action = new EventEmitter<void>(); // Child -> Parent

  handleClick() {
    this.action.emit();
  }
}

// Usage in parent:
// <app-child [data]="myData" (action)="handleAction()"></app-child>
```

**child.html**:
```html
<button (click)="handleClick()">Click Me</button>
```

### Signal-Based Reactivity (Angular 17+)

Signals provide fine-grained reactivity without zone-based change detection:

```typescript
import { signal, computed } from '@angular/core';

export class My {
  count = signal(0);                           // Writable signal
  doubleCount = computed(() => this.count() * 2); // Derived signal

  increment() {
    this.count.set(this.count() + 1);          // Update signal
  }
}

// Template usage: {{ count() }} or {{ doubleCount() }}
```

---

## Routing Setup

Angular's router enables client-side navigation with lazy-loaded components for optimal performance.

### Configure Routes

**src/app/app.routes.ts**:
```typescript
import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: '/patients', pathMatch: 'full' },
  {
    path: 'patients',
    loadComponent: () =>
      import('./features/patients/patient-list/patient-list')
        .then(m => m.PatientList)
  },
  {
    path: 'patients/create',
    loadComponent: () =>
      import('./features/patients/create-patient/create-patient')
        .then(m => m.CreatePatient)
  },
  {
    path: 'patients/:id',
    loadComponent: () =>
      import('./features/patients/patient-detail/patient-detail')
        .then(m => m.PatientDetail)
  },
];
```

### Bootstrap the Router

**src/app/app.config.ts**:
```typescript
import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZoneChangeDetection, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptorsFromDi()),
  ]
};
```

**src/app/app.ts**:
```typescript
import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, MatToolbarModule, MatIconModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly title = signal('Scheduling.AngularApp');
}
```

**src/app/app.html**:
```html
<mat-toolbar color="primary">
  <mat-icon>local_hospital</mat-icon>
  <span class="app-title">{{ title() }}</span>
</mat-toolbar>

<main class="content">
  <router-outlet></router-outlet>
</main>
```

**src/app/app.scss**:
```scss
.app-title {
  margin-left: 8px;
  font-size: 1.1rem;
}

.content {
  padding: 24px;
}
```

### Route Configuration Options

| Option | Purpose | Example |
|--------|---------|---------|
| `path` | URL path segment | `'patients/:id'` |
| `redirectTo` | Redirect target | `'/patients'` |
| `pathMatch` | Match strategy | `'full'` or `'prefix'` |
| `loadComponent` | Lazy-load standalone component | `() => import('...')` |
| `loadChildren` | Lazy-load child routes | `() => import('./routes')` |

**pathMatch strategies**: `'prefix'` (default) matches if the URL **starts with** the path — e.g., path `'patients'` matches `/patients`, `/patients/123`, and `/patients/create`. `'full'` matches only if the **entire** URL path equals the path. The most common use case for `'full'` is the root redirect (`{ path: '', redirectTo: '/patients', pathMatch: 'full' }`) — without it, every route would redirect since every URL starts with an empty string.

---

## Page Implementations

Generate all three components from the Angular CLI (run from the `Scheduling.AngularApp/` root):

```bash
ng generate component features/patients/patient-list
ng generate component features/patients/patient-detail
ng generate component features/patients/create-patient
```

This scaffolds each component's `.ts`, `.html`, and `.scss` files in the correct folder. Then replace the generated boilerplate with the code below.

### Patient List Component

The main landing page. It fetches all patients from the Scheduling API on init, displays them in a Material data table, and provides a status dropdown to filter by Active/Suspended. The `loadPatients()` method re-fires whenever the filter changes. A "Create patient" button navigates to the create form. The component uses `OnPush` change detection strategy for optimal performance with signals.

**`src/app/features/patients/patient-list/patient-list.ts`**:
```typescript
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { KeyValuePipe } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { FormsModule } from '@angular/forms';
import { PatientApi } from '@core/services/patient-api';
import { Patient } from '@core/models/patient.model';

@Component({
  selector: 'app-patient-list',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatSelectModule, MatFormFieldModule, MatProgressSpinnerModule, FormsModule, KeyValuePipe],
  templateUrl: './patient-list.html',
  styleUrl: './patient-list.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PatientList implements OnInit {
  private patientService = inject(PatientApi);
  router = inject(Router);

  patients = signal<Patient[]>([]);
  isLoading = signal<boolean>(true);
  selectedStatus = '';
  displayedColumns = ['firstName', 'lastName', 'email', 'status', 'actions'];

  statusOptions: Record<string, string> = {
    '': 'All',
    'Active': 'Active',
    'Suspended': 'Suspended',
    'Deleted': 'Deleted',
  };

  ngOnInit() {
    this.loadPatients();
  }

  loadPatients() : void {
    this.isLoading.set(true);
    this.patientService.getAll({ status: this.selectedStatus || undefined }).subscribe({
      next: (patients) => {
        this.patients.set(patients);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }
}
```

**`src/app/features/patients/patient-list/patient-list.html`**:
```html
<h1>Patients</h1>

<div class="toolbar">
  <mat-form-field>
    <mat-label>Status</mat-label>
    <mat-select [(value)]="selectedStatus" (selectionChange)="loadPatients()">
      @for (option of statusOptions | keyvalue; track option.key) {
        <mat-option [value]="option.key">{{ option.value }}</mat-option>
      }
    </mat-select>
  </mat-form-field>

  <button mat-flat-button (click)="router.navigate(['/patients/create'])">
    Create patient
  </button>
</div>

@if (isLoading()) {
  <div class="spinner-wrapper"><mat-spinner diameter="40" /></div>
} @else {
  <table mat-table [dataSource]="patients()">
    <ng-container matColumnDef="firstName">
      <th mat-header-cell *matHeaderCellDef>First Name</th>
      <td mat-cell *matCellDef="let patient">{{ patient.firstName }}</td>
    </ng-container>

    <ng-container matColumnDef="lastName">
      <th mat-header-cell *matHeaderCellDef>Last Name</th>
      <td mat-cell *matCellDef="let patient">{{ patient.lastName }}</td>
    </ng-container>

    <ng-container matColumnDef="email">
      <th mat-header-cell *matHeaderCellDef>Email</th>
      <td mat-cell *matCellDef="let patient">{{ patient.email }}</td>
    </ng-container>

    <ng-container matColumnDef="status">
      <th mat-header-cell *matHeaderCellDef>Status</th>
      <td mat-cell *matCellDef="let patient">
        <span class="status-badge" [class]="'status-' + patient.status.toLowerCase()">
          {{ patient.status }}
        </span>
      </td>
    </ng-container>

    <ng-container matColumnDef="actions">
      <th mat-header-cell *matHeaderCellDef>Actions</th>
      <td mat-cell *matCellDef="let patient">
        <button mat-button color="primary" (click)="router.navigate(['/patients', patient.id])">View</button>
      </td>
    </ng-container>

    <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
    <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
  </table>
}
```

**`src/app/features/patients/patient-list/patient-list.scss`**:
```scss
.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}

.spinner-wrapper {
  display: flex;
  justify-content: center;
  padding: 48px 0;
}

table {
  width: 100%;
}

.status-badge {
  padding: 4px 12px;
  border-radius: 16px;
  font-size: 0.8rem;
  font-weight: 500;
}

.status-active {
  background-color: #e8f5e9;
  color: #2e7d32;
}

.status-suspended {
  background-color: #fff3e0;
  color: #e65100;
}

.status-deleted {
  background-color: #ffebee;
  color: #c62828;
}
```

### Patient Detail Component

A read-only detail view for a single patient. It reads the `:id` route parameter via `ActivatedRoute`, fetches that patient from the API, and displays their info in a Material card. A toggle button supports both suspending and activating patients, using a `computed` signal to derive the current status and determine the appropriate action. A delete button with a bin icon is placed in the header next to the patient name for soft-deleting a patient — on success, `NotificationService` shows a success toast and the user is redirected to the list. Suspend and activate also show feedback via the notification service. A "Back to list" button navigates back. The component uses `OnPush` change detection strategy for optimal performance with signals.

**`src/app/features/patients/patient-detail/patient-detail.ts`**:
```typescript
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
  router = inject(Router);
  private notification = inject(NotificationService);

  patient = signal<Patient | null>(null);
  isSuspended = computed(() => this.patient()!.status === 'Suspended');
  isDeleted = computed(() => this.patient()!.status === 'Deleted');
  isLoading = signal<boolean>(false);

    ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.loadPatient(id);
  }

  private loadPatient(id: string): void{
    this.isLoading.set(true);
    this.patientService.getById(id).subscribe({
      next: (patient) => {
        this.patient.set(patient)
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    })
  }

  suspend(){
    const id = this.patient()!.id;
    this.patientService.suspend(id).subscribe({
      next: (response) => {
        if (response.success) {
          this.notification.success(response.message);
          this.loadPatient(id);
        } else {
          this.notification.error(response.message);
        }
      }, error: (err) => {
        console.log("Failed to suspend patient", err);
      }
    });
  }

  activate(){
    const id = this.patient()!.id;
    this.patientService.activate(id).subscribe({
      next: (response) => {
        if (response.success) {
          this.notification.success(response.message);
          this.loadPatient(id);
        } else {
          this.notification.error(response.message);
        }
      }, error: (err) => {
        console.log("Failed to activate patient", err);
      }
    });
  }

  delete(){
    const id = this.patient()!.id;
    this.patientService.delete(id).subscribe({
      next: (response) => {
        if(response.success){
          this.notification.success(response.message);
          this.router.navigate(['/patients']);
        } else {
          this.notification.error(response.message);
        }
      },
      error: (err) => {
        console.log("Failed to delete patient", err);
      }
    });
  }
}
```

**`src/app/features/patients/patient-detail/patient-detail.html`**:
```html
@if(isLoading()){
  <mat-spinner />
} @else if(patient(); as p) {
  <div class="detail-header">
    <h1>{{p.firstName}} {{p.lastName}}</h1>
    @if(!isDeleted()){
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
      @if(!isDeleted()){
        <button mat-flat-button color="warn" (click)="isSuspended() ? activate() : suspend()">{{isSuspended() ? 'Activate' : 'Suspend'}}</button>
      }
      <button mat-button (click)="router.navigate(['/patients'])">Back to list</button>
    </mat-card-actions>
  </mat-card>
}
```

**`src/app/features/patients/patient-detail/patient-detail.scss`**:
```scss
.detail-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}
```

### Create Patient Component

A reactive form for creating a new patient. Uses `FormBuilder` to define the form group with built-in validators (required, email). On submit it calls `PatientApi.create()`, navigates back to the list on success, and disables the submit button while the request is in flight to prevent double submissions. The cancel button navigates back without saving.

**`src/app/features/patients/create-patient/create-patient.ts`**:
```typescript
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { PatientApi } from '@core/services/patient-api';
import { CreatePatientRequest, CreatePatientResponse } from '@core/models/patient.model';
import { MatFormField, MatLabel, MatError, MatFormFieldModule } from "@angular/material/form-field";
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatInputModule } from '@angular/material/input';
import { HttpErrorResponse } from '@angular/common/http';
import { NotificationService } from '@core/services/notification';

@Component({
  selector: 'app-create-patient',
  standalone: true,
  imports: [MatFormField, MatLabel, MatError, ReactiveFormsModule, MatDatepickerModule, MatNativeDateModule, MatInputModule, MatButtonModule],
  templateUrl: './create-patient.html',
  styleUrl: './create-patient.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CreatePatient {
  private patientService = inject(PatientApi);
  private fb = inject(FormBuilder);
  private notification = inject(NotificationService);
  router = inject(Router);

  isSubmitting = signal(false);

  form = this.fb.nonNullable.group({
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    dateOfBirth: [null as Date | null, Validators.required],
    status: ['Active']
  });

  submit(): void {
    if (this.form.invalid){
      this.form.markAllAsTouched();
      return;
    }

    const rawValue = this.form.getRawValue();
    const dob = rawValue.dateOfBirth!;

    const request: CreatePatientRequest = {
      firstName: rawValue.firstName,
      lastName: rawValue.lastName,
      email: rawValue.email,
      dateOfBirth: dob.toISOString().split('T')[0],
      status: rawValue.status
    };

    this.isSubmitting.set(true);
    this.patientService.create(request).subscribe({
      next: (response: CreatePatientResponse) => {
        if(response.success){
          this.notification.success(response.message);
          this.router.navigate(['/patients']);
        } else {
          this.notification.error(response.message);
          this.isSubmitting.set(false);
        }
      },
      error: (err: HttpErrorResponse) => {
        console.log("Failed to create patient", err)
        this.isSubmitting.set(false);
      }
    });
  }
}
```

**`src/app/features/patients/create-patient/create-patient.html`**:
```html
<h1>Create Patient</h1>

<form [formGroup]="form" (ngSubmit)="submit()">
  <mat-form-field>
    <mat-label>First Name</mat-label>
    <input matInput formControlName="firstName" required />
    <mat-error>First name is required</mat-error>
  </mat-form-field>

  <mat-form-field>
    <mat-label>Last Name</mat-label>
    <input matInput formControlName="lastName" required />
    <mat-error>Last name is required</mat-error>
  </mat-form-field>

  <mat-form-field>
    <mat-label>Email</mat-label>
    <input matInput type="email" formControlName="email" required />
    <mat-error>Valid email is required</mat-error>
  </mat-form-field>

  <mat-form-field>
    <mat-label>Date of Birth</mat-label>
    <input matInput [matDatepicker]="picker" formControlName="dateOfBirth" required />
    <mat-datepicker-toggle matIconSuffix [for]="picker"></mat-datepicker-toggle>
    <mat-datepicker #picker></mat-datepicker>
  </mat-form-field>

  <div class="actions">
    <button mat-flat-button color="primary" type="submit" [disabled]="isSubmitting()">
      Create
    </button>
    <button mat-button type="button" (click)="router.navigate(['/patients'])">
      Cancel
    </button>
  </div>
</form>
```

**`src/app/features/patients/create-patient/create-patient.scss`**:
```scss
form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  max-width: 500px;
}

.actions {
  display: flex;
  gap: 0.5rem;
}
```

**Snackbar styles** — add to `src/styles.scss` (global, since snackbar renders outside component scope):

```scss
.snackbar-success {
  --mdc-snackbar-container-color: #4caf50;
  --mat-snack-bar-button-color: #ffffff;
}

.snackbar-error {
  --mdc-snackbar-container-color: #f44336;
  --mat-snack-bar-button-color: #ffffff;
}
```

---

## Angular vs Blazor Component Comparison

| Concept | Blazor | Angular |
|---------|--------|---------|
| **Component file** | `.razor` (markup + code) | `.ts` + `.html` + `.scss` |
| **Template syntax** | Razor (`@if`, `@foreach`, `@bind`) | Angular (`@if`, `@for`, `{{ }}`, `[(ngModel)]`) |
| **Routing definition** | `@page "/path"` in component | `Routes` array with `loadComponent` |
| **Route parameters** | `[Parameter]` property attribute | `ActivatedRoute.snapshot.paramMap.get('id')` |
| **Data table** | `FluentDataGrid<T>` | `mat-table` with `*matHeaderCellDef`, `*matCellDef` |
| **Loading indicator** | `<FluentProgressRing />` | `<mat-spinner />` |
| **Navigation** | `NavigationManager.NavigateTo()` | `Router.navigate(['/path'])` |
| **Reactivity** | Manual `StateHasChanged()` | Signals auto-update (`signal()`) |
| **Dependency injection** | `@inject` directive | `inject()` function (Angular 14+) |
| **Forms** | Two-way binding with `@bind` | `ReactiveFormsModule` with `FormBuilder` |
| **Lifecycle** | `OnInitializedAsync()` | `ngOnInit()` |
| **Cleanup** | `IDisposable.Dispose()` | `ngOnDestroy()` |
| **Module system** | None (component-scoped) | Standalone components (no NgModule) |

### Key Differences in Template Syntax

| Feature | Blazor | Angular |
|---------|--------|---------|
| Interpolation | `@value` or `@(expression)` | `{{ value }}` or `{{ expression }}` |
| Conditional | `@if (condition) { ... }` | `@if (condition) { ... }` |
| Loop | `@foreach (var item in items) { ... }` | `@for (item of items; track item.id) { ... }` |
| Event binding | `@onclick="Handler"` | `(click)="handler()"` |
| Property binding | `value="@prop"` | `[value]="prop"` |
| Two-way binding | `@bind="prop"` | `[(ngModel)]="prop"` |

---

## Verification Checklist

After implementing the components, verify:

- [ ] Routes configured in `app.routes.ts` with lazy loading
- [ ] Patient List component displays patients in `mat-table`
- [ ] Status filter dropdown works and reloads data
- [ ] Create Patient button navigates to form
- [ ] Create Patient form validates required fields
- [ ] Form submission creates patient and redirects to list
- [ ] Patient Detail component loads by route parameter (`/patients/:id`)
- [ ] Delete button (bin icon) appears next to patient name in detail view
- [ ] Delete button soft-deletes patient and redirects to list with snackbar
- [ ] Suspend/Activate button toggles based on patient status
- [ ] Suspend and Activate buttons update patient status
- [ ] Suspend and Activate show success/error snackbar feedback
- [ ] Back to List button navigates to patient list
- [ ] Loading spinners display during API calls
- [ ] Navigation between pages works without page refresh
- [ ] Browser back/forward buttons work correctly

---

## Navigation

- **Previous:** [02-angular-consuming-apis.md](./02-angular-consuming-apis.md)
- **Next:** [05-angular-forms-and-validation.md](./05-angular-forms-and-validation.md)
