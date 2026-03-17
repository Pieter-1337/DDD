# Angular — State Management

> **Track:** Angular frontend track.

---

## Overview

State management in Angular determines how components share and react to data changes. This document covers practical approaches for managing state in a DDD-aligned Angular application, focusing on signals introduced in Angular 17+ for reactive state handling.

For this learning project, built-in Angular patterns (signals and services) are sufficient. Complex state libraries like NgRx are unnecessary for simple CRUD operations.

---

## State in Angular

Angular provides multiple approaches for managing state:

- **Component state**: Local signals or properties for component-specific data
- **Service-based state**: Injectable services with signals for cross-component sharing
- **External state libraries**: NgRx, Akita, or NgRx Signal Store for complex scenarios (not needed for this project)

The key principle: keep state as local as possible, promote to services only when sharing is necessary.

---

## State Management Approaches

| Approach | Scope | Use Case |
|----------|-------|----------|
| Component signals | Single component | Form values, loading state, UI toggles |
| Injectable services with signals | Feature/app-wide | Shared data, cross-component communication |
| NgRx / NgRx Signal Store | App-wide (complex) | Complex state, undo/redo, time-travel debugging |
| localStorage/sessionStorage | Browser persistence | Remember user preferences, tokens |

**Recommendation for this project**: Use component signals for local state, services with signals for shared state, and MatSnackBar for notifications.

---

## Component State with Signals

Angular 17+ introduced signals as the primary reactive primitive. Signals automatically track dependencies and trigger re-renders when updated.

### Basic Signal Usage

```typescript
import { Component, signal, computed } from '@angular/core';
import { Patient } from '../../models/patient.model';

@Component({
  selector: 'app-patient-list',
  standalone: true,
  templateUrl: './patient-list.html',
})
export class PatientList {
  // Writable signals
  patients = signal<Patient[]>([]);
  isLoading = signal(true);
  selectedStatus = signal<string>('');

  // Computed signals - automatically update when dependencies change
  activePatients = computed(() =>
    this.patients().filter(p => p.status === 'Active')
  );

  filteredPatients = computed(() => {
    const status = this.selectedStatus();
    if (!status) return this.patients();
    return this.patients().filter(p => p.status === status);
  });

  filteredCount = computed(() => this.filteredPatients().length);

  ngOnInit() {
    this.loadPatients();
  }

  loadPatients() {
    this.isLoading.set(true);
    this.patientService.getAll().subscribe({
      next: (data) => {
        this.patients.set(data);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  filterByStatus(status: string) {
    this.selectedStatus.set(status); // Computed values auto-update
  }
}
```

**patient-list.html**:
```html
<div>
  @if (isLoading()) {
    <p>Loading...</p>
  } @else {
    <p>Found {{ filteredCount() }} patients</p>
    @for (patient of filteredPatients(); track patient.id) {
      <div>{{ patient.firstName }} {{ patient.lastName }}</div>
    }
  }
</div>
```

### Signal Update Patterns

```typescript
// Set - replace entire value
patients.set([...newPatients]);

// Update - modify based on current value
patients.update(current => [...current, newPatient]);

// Update nested property
selectedPatient.update(p => ({ ...p, status: 'Active' }));

// Read current value
const currentPatients = patients(); // Note: signals are functions
```

---

## Service-Based State with Signals

For state shared across multiple components, use injectable services with signals.

### Notification Store with Signals

**File**: `src/app/core/services/notification-store.ts`

```typescript
import { Injectable, signal } from '@angular/core';

export interface Notification {
  message: string;
  type: 'success' | 'error' | 'warning' | 'info';
  timestamp?: Date;
}

@Injectable({ providedIn: 'root' })
export class NotificationStore {
  // Private writable signal
  private _notifications = signal<Notification[]>([]);

  // Public readonly signal - consumers can read but not write
  readonly notifications = this._notifications.asReadonly();

  showSuccess(message: string) {
    this._notifications.update(n => [
      ...n,
      { message, type: 'success', timestamp: new Date() }
    ]);
    this.autoDismiss();
  }

  showError(message: string) {
    this._notifications.update(n => [
      ...n,
      { message, type: 'error', timestamp: new Date() }
    ]);
  }

  showWarning(message: string) {
    this._notifications.update(n => [
      ...n,
      { message, type: 'warning', timestamp: new Date() }
    ]);
  }

  showInfo(message: string) {
    this._notifications.update(n => [
      ...n,
      { message, type: 'info', timestamp: new Date() }
    ]);
  }

  dismiss(index: number) {
    this._notifications.update(n => n.filter((_, i) => i !== index));
  }

  clear() {
    this._notifications.set([]);
  }

  private autoDismiss() {
    setTimeout(() => {
      if (this._notifications().length > 0) {
        this._notifications.update(n => n.slice(1));
      }
    }, 5000);
  }
}
```

---

## Using MatSnackBar for Notifications (Recommended)

For simpler notification requirements, Angular Material's `MatSnackBar` provides a ready-made solution without custom state management.

### MatSnackBar Setup

**File**: `src/app/features/patients/create-patient/create-patient.ts`

```typescript
import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { PatientApi } from '../../../core/services/patient-api';

@Component({
  selector: 'app-create-patient',
  standalone: true,
  templateUrl: './create-patient.html',
})
export class CreatePatient {
  private fb = inject(FormBuilder);
  private patientService = inject(PatientApi);
  private router = inject(Router);
  private snackBar = inject(MatSnackBar);

  isSubmitting = signal(false);

  form: FormGroup = this.fb.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    dateOfBirth: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
  });

  onSubmit() {
    if (this.form.invalid) return;

    this.isSubmitting.set(true);

    this.patientService.create(this.form.value).subscribe({
      next: (response) => {
        this.snackBar.open('Patient created successfully', 'Close', {
          duration: 3000,
          horizontalPosition: 'end',
          verticalPosition: 'top',
          panelClass: ['success-snackbar'],
        });
        this.router.navigate(['/patients']);
      },
      error: (err) => {
        this.isSubmitting.set(false);
        const errorMessage = err.error?.title || 'Failed to create patient';
        this.snackBar.open(`Error: ${errorMessage}`, 'Close', {
          duration: 5000,
          horizontalPosition: 'end',
          verticalPosition: 'top',
          panelClass: ['error-snackbar'],
        });
      },
    });
  }
}
```

**create-patient.html**:
```html
<form [formGroup]="form" (ngSubmit)="onSubmit()">
  <!-- Form fields -->
  <button type="submit" [disabled]="form.invalid || isSubmitting()">
    Create Patient
  </button>
</form>
```

### MatSnackBar Styling

**File**: `src/styles.scss`

```scss
// Custom snackbar styles
.success-snackbar {
  --mdc-snackbar-container-color: #4caf50;
  --mat-snack-bar-button-color: #ffffff;
}

.error-snackbar {
  --mdc-snackbar-container-color: #f44336;
  --mat-snack-bar-button-color: #ffffff;
}

.warning-snackbar {
  --mdc-snackbar-container-color: #ff9800;
  --mat-snack-bar-button-color: #ffffff;
}

.info-snackbar {
  --mdc-snackbar-container-color: #2196f3;
  --mat-snack-bar-button-color: #ffffff;
}
```

---

## Custom Notification Display Component (Alternative)

If you prefer a custom notification service with full control over display logic.

### Notification Display Component

**File**: `src/app/shared/components/notification-display/notification-display.ts`

```typescript
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NotificationStore } from '../../core/services/notification-store';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './notification-display.html',
  styleUrl: './notification-display.scss',
})
export class NotificationDisplay {
  notificationService = inject(NotificationStore);
}
```

**notification-display.html**:
```html
<div class="notification-container">
  @for (notification of notificationService.notifications(); track $index) {
    <div
      class="notification"
      [class.success]="notification.type === 'success'"
      [class.error]="notification.type === 'error'"
      [class.warning]="notification.type === 'warning'"
      [class.info]="notification.type === 'info'"
    >
      <span class="notification-message">{{ notification.message }}</span>
      <button
        class="notification-close"
        (click)="notificationService.dismiss($index)"
        aria-label="Close notification"
      >
        ×
      </button>
    </div>
  }
</div>
```

**notification-display.scss**:
```scss
.notification-container {
  position: fixed;
  top: 20px;
  right: 20px;
  z-index: 9999;
  display: flex;
  flex-direction: column;
  gap: 10px;
  max-width: 400px;
}

.notification {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  border-radius: 4px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
  background-color: white;
  border-left: 4px solid;
  animation: slideIn 0.3s ease-out;
}

@keyframes slideIn {
  from {
    transform: translateX(400px);
    opacity: 0;
  }
  to {
    transform: translateX(0);
    opacity: 1;
  }
}

.notification.success {
  border-left-color: #4caf50;
  background-color: #f1f8f4;
}

.notification.error {
  border-left-color: #f44336;
  background-color: #fef5f5;
}

.notification.warning {
  border-left-color: #ff9800;
  background-color: #fff8f0;
}

.notification.info {
  border-left-color: #2196f3;
  background-color: #f0f7ff;
}

.notification-message {
  flex: 1;
  margin-right: 12px;
}

.notification-close {
  background: none;
  border: none;
  font-size: 24px;
  line-height: 1;
  cursor: pointer;
  color: #666;
  padding: 0;
  width: 24px;
  height: 24px;
}

.notification-close:hover {
  color: #000;
}
```

### Add to App Component

**File**: `src/app/app.ts`

```typescript
import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NotificationDisplay } from './shared/components/notification-display/notification-display';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, NotificationDisplay],
  templateUrl: './app.html',
})
export class App {}
```

**app.html**:
```html
<app-notifications></app-notifications>
<router-outlet></router-outlet>
```

---

## Blazor vs Angular State Comparison

Understanding the mental model differences between Blazor and Angular state management:

| Concept | Blazor | Angular |
|---------|--------|---------|
| Reactivity | Manual `StateHasChanged()` | Signals (automatic dependency tracking) |
| Component state | `@code` block fields | `signal()` / class properties |
| Shared state | Scoped services + `event Action` | Services with signals |
| Notifications | Custom service + `event Action` | `MatSnackBar` or custom service with signals |
| Lifecycle hook | `OnInitializedAsync` | `ngOnInit` |
| Cleanup | `IDisposable` | `ngOnDestroy` / `DestroyRef` |
| Two-way binding | `@bind` | `[(ngModel)]` or `FormControl` |
| Derived values | Manual computed properties | `computed()` signals |
| Change detection | Implicit (after event handlers) | Automatic via signals / Zone.js |

**Key difference**: Angular signals eliminate manual change detection calls. When a signal updates, Angular automatically re-renders components that depend on it.

---

## When to Use What

Choosing the right state management approach:

| Scenario | Recommendation | Rationale |
|----------|---------------|-----------|
| Form input values | `FormControl` / `FormGroup` | Built-in validation and dirty tracking |
| Loading states | Component `signal<boolean>()` | Local to component lifecycle |
| API response data | Component `signal<T[]>()` | Component-specific data |
| Cross-page notifications | `MatSnackBar` (simple) or `NotificationStore` (custom) | MatSnackBar is simpler, custom service for complex needs |
| Shared data between routes | Service with `signal<T>()` | Centralized state survives route changes |
| User preferences | Service + `localStorage` | Persist across sessions |
| Complex state with history | NgRx Signal Store | Not needed for this project |

**Golden Rule**: Start with component signals. Promote to service-based state only when multiple components need the same data.

---

## Practical Example: Patient List with Filtering

Combining local and computed signals for a filtered patient list:

```typescript
import { Component, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PatientApi } from '../../../core/services/patient-api';
import { Patient } from '../../../models/patient.model';

@Component({
  selector: 'app-patient-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './patient-list.html',
})
export class PatientList {
  private patientService = inject(PatientApi);

  // Component state
  patients = signal<Patient[]>([]);
  isLoading = signal(true);
  searchTerm = signal('');
  statusFilter = signal('');

  // Derived state - automatically updates when dependencies change
  filteredPatients = computed(() => {
    let result = this.patients();

    const search = this.searchTerm().toLowerCase();
    if (search) {
      result = result.filter(p =>
        p.firstName.toLowerCase().includes(search) ||
        p.lastName.toLowerCase().includes(search)
      );
    }

    const status = this.statusFilter();
    if (status) {
      result = result.filter(p => p.status === status);
    }

    return result;
  });

  ngOnInit() {
    this.loadPatients();
  }

  loadPatients() {
    this.isLoading.set(true);
    this.patientService.getAll().subscribe({
      next: (data) => {
        this.patients.set(data);
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Failed to load patients', err);
        this.isLoading.set(false);
      }
    });
  }

  onSearchChange(term: string) {
    this.searchTerm.set(term);
    // filteredPatients computed signal automatically updates
  }

  onStatusChange(status: string) {
    this.statusFilter.set(status);
    // filteredPatients computed signal automatically updates
  }
}
```

**patient-list.html**:
```html
<div class="patient-list">
  <div class="filters">
    <input
      type="text"
      placeholder="Search by name..."
      [(ngModel)]="searchTerm"
      (ngModelChange)="onSearchChange($event)"
    />
    <select [(ngModel)]="statusFilter" (ngModelChange)="onStatusChange($event)">
      <option value="">All Statuses</option>
      <option value="Active">Active</option>
      <option value="Inactive">Inactive</option>
    </select>
  </div>

  @if (isLoading()) {
    <p>Loading patients...</p>
  } @else if (filteredPatients().length === 0) {
    <p>No patients found</p>
  } @else {
    <p>Showing {{ filteredPatients().length }} of {{ patients().length }} patients</p>
    @for (patient of filteredPatients(); track patient.id) {
      <div class="patient-card">
        <h3>{{ patient.firstName }} {{ patient.lastName }}</h3>
        <p>DOB: {{ patient.dateOfBirth | date }}</p>
        <p>Status: {{ patient.status }}</p>
      </div>
    }
  }
</div>
```

**Key points**:
- `patients`, `isLoading`, `searchTerm`, and `statusFilter` are writable signals
- `filteredPatients` is a computed signal that automatically recalculates when any dependency changes
- No manual change detection or update logic needed
- Template automatically re-renders when signals change

---

## Verification Checklist

Ensure your state management implementation follows best practices:

- [ ] Component signals used for local state (`patients`, `isLoading`, `selectedStatus`)
- [ ] Computed signals work for derived values (`filteredPatients`, `activePatients`)
- [ ] MatSnackBar or `NotificationStore` shows success/error messages
- [ ] Notifications appear after patient creation/update/deletion
- [ ] Error messages display on API failures with meaningful content
- [ ] State updates trigger re-renders automatically via signals
- [ ] No `StateHasChanged()` equivalent needed (handled by signals)
- [ ] Signals are readonly where appropriate (`asReadonly()` on service state)
- [ ] Loading states prevent duplicate API calls
- [ ] Form submission disables submit button during processing

---

## Navigation

- Previous: [03-angular-components-and-routing.md](./03-angular-components-and-routing.md)
- Next: [05-angular-forms-and-validation.md](./05-angular-forms-and-validation.md)
