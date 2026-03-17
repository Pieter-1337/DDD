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
import { ApplicationConfig } from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    // Other providers...
  ],
};
```

**src/app/app.ts**:
```typescript
import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  templateUrl: './app.html',
})
export class App {}
```

**app.html**:
```html
<nav>
  <a routerLink="/patients">Patients</a>
</nav>
<router-outlet></router-outlet>
```

### Route Configuration Options

| Option | Purpose | Example |
|--------|---------|---------|
| `path` | URL path segment | `'patients/:id'` |
| `redirectTo` | Redirect target | `'/patients'` |
| `pathMatch` | Match strategy | `'full'` or `'prefix'` |
| `loadComponent` | Lazy-load standalone component | `() => import('...')` |
| `loadChildren` | Lazy-load child routes | `() => import('./routes')` |

---

## Page Implementations

### Patient List Component

**features/patients/patient-list/patient-list.ts**:
```typescript
import { Component, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { FormsModule } from '@angular/forms';
import { PatientApi } from '../../../core/services/patient-api';
import { Patient } from '../../../core/models/patient.model';

@Component({
  selector: 'app-patient-list',
  standalone: true,
  imports: [
    MatTableModule,
    MatButtonModule,
    MatSelectModule,
    MatFormFieldModule,
    MatProgressSpinnerModule,
    FormsModule,
  ],
  templateUrl: './patient-list.html',
  styleUrl: './patient-list.scss',
})
export class PatientList implements OnInit {
  private patientService = inject(PatientApi);
  router = inject(Router);

  patients = signal<Patient[]>([]);
  isLoading = signal(true);
  selectedStatus = '';
  displayedColumns = ['firstName', 'lastName', 'email', 'status', 'actions'];

  ngOnInit() {
    this.loadPatients();
  }

  loadPatients() {
    this.isLoading.set(true);
    this.patientService.getAll(this.selectedStatus || undefined).subscribe({
      next: (patients) => {
        this.patients.set(patients);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false),
    });
  }
}
```

**patient-list.html**:
```html
<h1>Patients</h1>

<div class="toolbar">
  <mat-form-field>
    <mat-label>Status</mat-label>
    <mat-select [(ngModel)]="selectedStatus" (selectionChange)="loadPatients()">
      <mat-option value="">All</mat-option>
      <mat-option value="Active">Active</mat-option>
      <mat-option value="Suspended">Suspended</mat-option>
    </mat-select>
  </mat-form-field>

  <button mat-raised-button color="primary" (click)="router.navigate(['/patients/create'])">
    Create Patient
  </button>
</div>

@if (isLoading()) {
  <mat-spinner />
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
      <td mat-cell *matCellDef="let patient">{{ patient.status }}</td>
    </ng-container>

    <ng-container matColumnDef="actions">
      <th mat-header-cell *matHeaderCellDef>Actions</th>
      <td mat-cell *matCellDef="let patient">
        <button mat-button (click)="router.navigate(['/patients', patient.id])">View</button>
      </td>
    </ng-container>

    <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
    <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
  </table>
}
```

**patient-list.scss**:
```scss
.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
}
```

### Patient Detail Component

**features/patients/patient-detail/patient-detail.ts**:
```typescript
import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { DatePipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PatientApi } from '../../../core/services/patient-api';
import { Patient } from '../../../core/models/patient.model';

@Component({
  selector: 'app-patient-detail',
  standalone: true,
  imports: [
    DatePipe,
    MatCardModule,
    MatButtonModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './patient-detail.html',
})
export class PatientDetail implements OnInit {
  private patientService = inject(PatientApi);
  private route = inject(ActivatedRoute);
  router = inject(Router);

  patient = signal<Patient | null>(null);
  isLoading = signal(true);

  ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.loadPatient(id);
  }

  private loadPatient(id: string) {
    this.patientService.getById(id).subscribe({
      next: (patient) => {
        this.patient.set(patient);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false),
    });
  }

  suspend() {
    const id = this.patient()?.id;
    if (!id) return;
    this.patientService.suspend(id).subscribe({
      next: () => this.loadPatient(id),
    });
  }
}
```

**patient-detail.html**:
```html
@if (isLoading()) {
  <mat-spinner />
} @else if (patient(); as p) {
  <h1>{{ p.firstName }} {{ p.lastName }}</h1>

  <mat-card>
    <mat-card-content>
      <p><strong>Email:</strong> {{ p.email }}</p>
      <p><strong>Status:</strong> {{ p.status }}</p>
      <p><strong>Date of Birth:</strong> {{ p.dateOfBirth | date }}</p>
    </mat-card-content>
    <mat-card-actions>
      @if (p.status !== 'Suspended') {
        <button mat-raised-button color="warn" (click)="suspend()">Suspend</button>
      }
      <button mat-button (click)="router.navigate(['/patients'])">Back to List</button>
    </mat-card-actions>
  </mat-card>
}
```

### Create Patient Component

**features/patients/create-patient/create-patient.ts**:
```typescript
import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { PatientApi } from '../../../core/services/patient-api';

@Component({
  selector: 'app-create-patient',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatDatepickerModule,
    MatNativeDateModule,
  ],
  templateUrl: './create-patient.html',
  styleUrl: './create-patient.scss',
})
export class CreatePatient {
  private patientService = inject(PatientApi);
  private fb = inject(FormBuilder);
  router = inject(Router);

  isSubmitting = signal(false);

  form = this.fb.group({
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    dateOfBirth: ['', Validators.required],
  });

  submit() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.patientService.create(this.form.value).subscribe({
      next: () => this.router.navigate(['/patients']),
      error: () => this.isSubmitting.set(false),
    });
  }
}
```

**create-patient.html**:
```html
<h1>Create Patient</h1>

<form [formGroup]="form" (ngSubmit)="submit()">
  <mat-form-field>
    <mat-label>First Name</mat-label>
    <input matInput formControlName="firstName" required />
    @if (form.controls.firstName.invalid && form.controls.firstName.touched) {
      <mat-error>First name is required</mat-error>
    }
  </mat-form-field>

  <mat-form-field>
    <mat-label>Last Name</mat-label>
    <input matInput formControlName="lastName" required />
    @if (form.controls.lastName.invalid && form.controls.lastName.touched) {
      <mat-error>Last name is required</mat-error>
    }
  </mat-form-field>

  <mat-form-field>
    <mat-label>Email</mat-label>
    <input matInput type="email" formControlName="email" required />
    @if (form.controls.email.invalid && form.controls.email.touched) {
      <mat-error>Valid email is required</mat-error>
    }
  </mat-form-field>

  <mat-form-field>
    <mat-label>Date of Birth</mat-label>
    <input matInput [matDatepicker]="picker" formControlName="dateOfBirth" required />
    <mat-datepicker-toggle matSuffix [for]="picker"></mat-datepicker-toggle>
    <mat-datepicker #picker></mat-datepicker>
  </mat-form-field>

  <div class="actions">
    <button mat-raised-button color="primary" type="submit" [disabled]="isSubmitting()">
      Create
    </button>
    <button mat-button type="button" (click)="router.navigate(['/patients'])">
      Cancel
    </button>
  </div>
</form>
```

**create-patient.scss**:
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
- [ ] Suspend button only shows for active patients
- [ ] Suspend button updates patient status
- [ ] Back to List button navigates to patient list
- [ ] Loading spinners display during API calls
- [ ] Navigation between pages works without page refresh
- [ ] Browser back/forward buttons work correctly

---

## Navigation

- **Previous:** [01-angular-project-setup.md](./01-angular-project-setup.md)
- **Next:** [03-angular-consuming-apis.md](./03-angular-consuming-apis.md)
