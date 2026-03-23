# Angular — Forms and Validation

> **Track:** Angular frontend track. For the Blazor equivalent, see the Blazor documentation.

This document covers reactive forms in Angular, including validation, error handling, and integration with Angular Material form components. Forms are a critical part of any frontend application, and Angular's reactive forms provide explicit control and excellent testability.

---

## Angular Forms Overview

Angular provides two approaches to handling forms:

1. **Template-driven forms** - Logic primarily in the template, similar to traditional HTML forms
2. **Reactive forms** - Logic in the component class, with explicit control over form state

**We use Reactive Forms** in this project for the following reasons:
- **Explicit control** - Form state is managed in the component class
- **Testability** - Easier to unit test form logic without rendering components
- **Type safety** - Better TypeScript integration
- **Scalability** - Better suited for complex forms with dynamic fields

### Key Concepts

| Concept | Description |
|---------|-------------|
| **FormControl** | Tracks the value and validation state of an individual form field |
| **FormGroup** | Tracks the value and validation state of a group of FormControl instances |
| **FormBuilder** | Provides syntactic sugar for creating FormControl, FormGroup, and FormArray instances |
| **Validators** | Built-in and custom validation functions |
| **Angular Material** | Provides Material Design form field components with built-in error handling |

---

## Reactive Forms Setup

To use reactive forms in a standalone component, import the necessary modules:

```typescript
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
```

For Angular Material form fields:

```typescript
import { MatFormField, MatLabel, MatError } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
```

---

## Create Patient Form Example

Here's the complete create patient form using reactive forms, Angular Material, and NotificationService for user feedback.

**File**: `features/patients/create-patient/create-patient.ts`

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
import { MatButtonModule } from '@angular/material/button';

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

**create-patient.html**:
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

### Key Points

- **FormBuilder** — Use `nonNullable.group()` to create a strongly-typed form that doesn't allow null values
- **Validators** — Chain multiple validators in an array: `[Validators.required, Validators.email]`
- **Error Messages** — A single `<mat-error>` per field is sufficient; Angular Material only shows one at a time
- **Typed Request** — Map `getRawValue()` to a typed `CreatePatientRequest` before sending; format the date as `yyyy-MM-dd`
- **Signal State** — `isSubmitting` is a signal because the component uses `ChangeDetectionStrategy.OnPush`
- **NotificationService** — Centralises snackbar config so components just call `success()` or `error()`
- **Public Router** — `router` has no `private` modifier because the template calls `router.navigate()` inline for the cancel button

---

## Custom Validators

Angular provides built-in validators (`required`, `email`, `minLength`, etc.), but you can also create custom validators for domain-specific rules.

> **Future enhancement:** The custom validators below are not yet wired into the create-patient form. They are documented here as ready-to-use examples for when stricter date-of-birth rules are needed.

### Past Date Validator Example

**File**: `shared/validators/date.validators.ts`

```typescript
import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * Validator that requires the control value to be a date in the past.
 */
export function pastDateValidator(control: AbstractControl): ValidationErrors | null {
  const value = control.value;
  if (!value) return null; // Don't validate empty values (use Validators.required for that)

  const date = new Date(value);
  const now = new Date();
  now.setHours(0, 0, 0, 0); // Compare dates only, not times

  if (date >= now) {
    return {
      pastDate: {
        message: 'Date of birth must be in the past',
        actualValue: date
      }
    };
  }

  return null;
}

/**
 * Validator that requires the control value to be at least a certain age.
 * @param minAge Minimum age in years
 */
export function minAgeValidator(minAge: number): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = control.value;
    if (!value) return null;

    const birthDate = new Date(value);
    const today = new Date();
    let age = today.getFullYear() - birthDate.getFullYear();
    const monthDiff = today.getMonth() - birthDate.getMonth();

    if (monthDiff < 0 || (monthDiff === 0 && today.getDate() < birthDate.getDate())) {
      age--;
    }

    if (age < minAge) {
      return {
        minAge: {
          message: `Must be at least ${minAge} years old`,
          requiredAge: minAge,
          actualAge: age
        }
      };
    }

    return null;
  };
}
```

### Using Custom Validators

To wire these into the form, add them to the `dateOfBirth` control:

```typescript
import { pastDateValidator, minAgeValidator } from '@shared/validators/date.validators';

form = this.fb.nonNullable.group({
  dateOfBirth: [null as Date | null, [
    Validators.required,
    pastDateValidator,
    minAgeValidator(18) // Patient must be at least 18 years old
  ]],
});
```

### Displaying Custom Validator Errors

```html
<mat-form-field>
  <mat-label>Date of Birth</mat-label>
  <input matInput [matDatepicker]="picker" formControlName="dateOfBirth" />
  <mat-datepicker-toggle matIconSuffix [for]="picker"></mat-datepicker-toggle>
  <mat-datepicker #picker></mat-datepicker>

  @if (form.controls.dateOfBirth.hasError('required')) {
    <mat-error>Date of birth is required</mat-error>
  }
  @if (form.controls.dateOfBirth.hasError('pastDate')) {
    <mat-error>{{ form.controls.dateOfBirth.errors?.['pastDate'].message }}</mat-error>
  }
  @if (form.controls.dateOfBirth.hasError('minAge')) {
    <mat-error>{{ form.controls.dateOfBirth.errors?.['minAge'].message }}</mat-error>
  }
</mat-form-field>
```

---

## Client-Side vs Server-Side Validation

Just like in Blazor, validation should exist at both the client and server layers, but they serve different purposes.

| Layer | Purpose | Technology | When It Runs |
|-------|---------|------------|--------------|
| **Client (Angular)** | UX — immediate feedback to the user | Reactive Forms Validators | Before form submission |
| **Server (API)** | Security — authoritative validation | FluentValidation (pipeline behavior) | During API request processing |

### Important Principles

1. **Client validation is for UX only** - It provides immediate feedback and prevents unnecessary API calls
2. **Server validation is authoritative** - Never trust client-side validation for security or data integrity
3. **Keep them in sync** - Client and server validation rules should mirror each other where possible
4. **Server errors override client** - Display server validation errors prominently

### Why Both Are Necessary

- **Client validation can be bypassed** - A malicious user can modify client-side code or send requests directly to the API
- **Server validation catches edge cases** - Database constraints, race conditions, and business rules that depend on server state
- **Client validation improves UX** - Users get immediate feedback without waiting for a round-trip to the server

This is the exact same principle as in Blazor: **client for UX, server for security**.

---

## Blazor vs Angular Forms Comparison

Both frameworks provide robust form handling with validation, but the approaches differ.

| Concept | Blazor | Angular |
|---------|--------|---------|
| **Form component** | `<EditForm Model="@model">` | `<form [formGroup]="form">` |
| **Validation library** | FluentValidation (Blazored.FluentValidation) | Built-in Validators + custom functions |
| **Form model** | POCO class (e.g., `CreatePatientCommand`) | `FormGroup` created with `FormBuilder` |
| **Data binding** | `@bind-Value="model.FirstName"` | `formControlName="firstName"` |
| **Error display** | `<FluentValidationMessage For="() => model.FirstName" />` | `<mat-error>` with `hasError('required')` |
| **Submit handler** | `OnValidSubmit="HandleSubmit"` | `(ngSubmit)="submit()"` |
| **Validation trigger** | Automatic via EditContext | Automatic via FormControl state |
| **Disable on submit** | `Disabled="@isSubmitting"` | `[disabled]="isSubmitting()"` |
| **Date picker** | `<FluentDatePicker @bind-Value="model.DateOfBirth" />` | `<input matInput [matDatepicker]="picker">` |
| **Server errors** | `FluentValidationSummary` | `NotificationService` error snackbar |

### Similarities

- Both enforce validation before allowing submission
- Both separate client validation (UX) from server validation (security)
- Both provide visual feedback for invalid fields
- Both handle server validation errors returned from the API

### Key Differences

- **Blazor** uses a POCO model that directly maps to the command/DTO sent to the API
- **Angular** uses `FormGroup` which is then converted to a typed request model via `getRawValue()`
- **Blazor** validation is defined in C# validator classes (server-side first, optionally reused client-side)
- **Angular** validation is defined as TypeScript functions (client-side first, separate from server FluentValidation)

---

## Verification Checklist

After implementing forms and validation, verify the following:

- [ ] **Reactive form created** — Form is created using `FormBuilder` with `nonNullable.group()`
- [ ] **Required validators** — All required fields have `Validators.required`
- [ ] **Email validation** — Email field has `Validators.email`
- [ ] **Error messages** — `mat-error` displays appropriate validation messages
- [ ] **Submit button state** — Submit button is disabled while submitting (`isSubmitting()` signal)
- [ ] **Success notification** — Successful creation shows a snackbar via `NotificationService`
- [ ] **Error notification** — Failed creation shows an error snackbar via `NotificationService`
- [ ] **Navigation on success** — User is navigated to patient list after successful creation
- [ ] **Cancel button** — Cancel button navigates back to patient list without submitting
- [ ] **Typed request** — Form values are mapped to `CreatePatientRequest` with correct date formatting

### Testing Your Form

1. **Test required field validation** — Leave fields empty and try to submit
2. **Test email format validation** — Enter invalid email formats (e.g., "notanemail")
3. **Test submit button state** — Verify it's disabled while the request is in flight
4. **Test server validation** — Submit with data that will fail server validation (e.g., duplicate email)
5. **Test successful submission** — Create a valid patient and verify navigation and notification
6. **Test cancel** — Click cancel and verify navigation back without submission

---

## Navigation

- **Previous**: [03-angular-components-and-routing.md](./03-angular-components-and-routing.md)
- **Back to overview**: [../00-frontend-overview.md](../00-frontend-overview.md)
