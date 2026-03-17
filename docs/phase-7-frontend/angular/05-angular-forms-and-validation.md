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
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
```

For Angular Material form fields:

```typescript
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
```

---

## Create Patient Form Example

Here's a complete example of a create patient form using reactive forms and Angular Material.

**File**: `features/patients/create-patient/create-patient.ts`

```typescript
import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { HttpErrorResponse } from '@angular/common/http';
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
  private fb = inject(FormBuilder);
  private patientService = inject(PatientApi);
  private snackBar = inject(MatSnackBar);
  private router = inject(Router);

  isSubmitting = false;
  serverErrors: string[] = [];

  form = this.fb.nonNullable.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email]],
    dateOfBirth: ['', [Validators.required, pastDateValidator]],
  });

  onSubmit(): void {
    if (this.form.invalid) return;

    this.isSubmitting = true;
    this.serverErrors = [];

    this.patientService.create(this.form.getRawValue()).subscribe({
      next: (response) => {
        if (response.success) {
          this.snackBar.open('Patient created successfully', 'Close', {
            duration: 3000,
          });
          this.router.navigate(['/patients']);
        } else {
          this.serverErrors = response.errors ?? ['An unknown error occurred'];
          this.isSubmitting = false;
        }
      },
      error: (err: HttpErrorResponse) => {
        this.handleError(err);
        this.isSubmitting = false;
      },
    });
  }

  onCancel(): void {
    this.router.navigate(['/patients']);
  }

  private handleError(err: HttpErrorResponse): void {
    if (err.status === 400 && err.error?.errors) {
      // FluentValidation errors from backend
      this.serverErrors = Object.values(err.error.errors).flat() as string[];
    } else {
      this.serverErrors = [err.message || 'Failed to create patient'];
    }
  }
}
```

**create-patient.html**:
```html
<div class="create-patient-container">
  <h1>Create Patient</h1>

  <form [formGroup]="form" (ngSubmit)="onSubmit()">
    <mat-form-field>
      <mat-label>First Name</mat-label>
      <input matInput formControlName="firstName" />
      @if (form.controls.firstName.hasError('required')) {
        <mat-error>First name is required</mat-error>
      }
      @if (form.controls.firstName.hasError('maxlength')) {
        <mat-error>Maximum 100 characters</mat-error>
      }
    </mat-form-field>

    <mat-form-field>
      <mat-label>Last Name</mat-label>
      <input matInput formControlName="lastName" />
      @if (form.controls.lastName.hasError('required')) {
        <mat-error>Last name is required</mat-error>
      }
      @if (form.controls.lastName.hasError('maxlength')) {
        <mat-error>Maximum 100 characters</mat-error>
      }
    </mat-form-field>

    <mat-form-field>
      <mat-label>Email</mat-label>
      <input matInput formControlName="email" type="email" />
      @if (form.controls.email.hasError('required')) {
        <mat-error>Email is required</mat-error>
      }
      @if (form.controls.email.hasError('email')) {
        <mat-error>Invalid email format</mat-error>
      }
    </mat-form-field>

    <mat-form-field>
      <mat-label>Date of Birth</mat-label>
      <input matInput [matDatepicker]="picker" formControlName="dateOfBirth" />
      <mat-datepicker-toggle matIconSuffix [for]="picker" />
      <mat-datepicker #picker />
      @if (form.controls.dateOfBirth.hasError('required')) {
        <mat-error>Date of birth is required</mat-error>
      }
      @if (form.controls.dateOfBirth.hasError('pastDate')) {
        <mat-error>{{ form.controls.dateOfBirth.errors?.['pastDate'].message }}</mat-error>
      }
    </mat-form-field>

    <div class="actions">
      <button mat-raised-button color="primary" type="submit"
              [disabled]="form.invalid || isSubmitting">
        {{ isSubmitting ? 'Creating...' : 'Create Patient' }}
      </button>
      <button mat-button type="button" (click)="onCancel()">
        Cancel
      </button>
    </div>

    @if (serverErrors.length > 0) {
      <div class="server-errors">
        @for (error of serverErrors; track error) {
          <p class="error">{{ error }}</p>
        }
      </div>
    }
  </form>
</div>
```

**create-patient.scss**:
```scss
.create-patient-container {
  max-width: 600px;
  margin: 2rem auto;
  padding: 2rem;
}

form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

mat-form-field {
  width: 100%;
}

.actions {
  display: flex;
  gap: 1rem;
  margin-top: 1rem;
}

.server-errors {
  margin-top: 1rem;
  padding: 1rem;
  background-color: #ffebee;
  border-radius: 4px;
}

.error {
  color: #c62828;
  margin: 0.25rem 0;
}
```

### Key Points

- **FormBuilder** - Use `nonNullable.group()` to create a strongly-typed form that doesn't allow null values
- **Validators** - Chain multiple validators in an array: `[Validators.required, Validators.maxLength(100)]`
- **Error Messages** - Use `@if` control flow to conditionally show error messages based on validation state
- **Disabled State** - Disable submit button when form is invalid or submitting
- **Server Errors** - Display server-side validation errors separately from client-side errors

---

## Custom Validators

Angular provides built-in validators (`required`, `email`, `minLength`, etc.), but you can also create custom validators for domain-specific rules.

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

```typescript
import { pastDateValidator, minAgeValidator } from '../../../shared/validators/date.validators';

form = this.fb.nonNullable.group({
  dateOfBirth: ['', [
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
  <mat-datepicker-toggle matIconSuffix [for]="picker" />
  <mat-datepicker #picker />

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

## Handling Server Validation Errors

The backend API (using FluentValidation) returns structured validation errors. Here's how to extract and display them in Angular.

### FluentValidation Error Response Format

When FluentValidation fails, ASP.NET Core returns a 400 Bad Request with this structure:

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "FirstName": ["First name is required"],
    "Email": ["Email must be a valid email address", "Email is already in use"]
  }
}
```

### Parsing and Displaying Server Errors

```typescript
private handleError(err: HttpErrorResponse): void {
  this.serverErrors = [];

  if (err.status === 400 && err.error?.errors) {
    // FluentValidation errors from backend
    // errors is an object with field names as keys and string arrays as values
    const errorObj = err.error.errors;

    for (const fieldErrors of Object.values(errorObj)) {
      if (Array.isArray(fieldErrors)) {
        this.serverErrors.push(...fieldErrors);
      }
    }
  } else if (err.status === 409) {
    // Conflict - e.g., duplicate email
    this.serverErrors = [err.error?.detail || 'A conflict occurred'];
  } else if (err.status === 500) {
    // Server error
    this.serverErrors = ['An unexpected server error occurred. Please try again later.'];
  } else {
    // Generic error
    this.serverErrors = [err.message || 'An unexpected error occurred'];
  }
}
```

### Displaying Server Errors in the Template

```html
@if (serverErrors.length > 0) {
  <div class="server-errors">
    <h3>Please correct the following errors:</h3>
    <ul>
      @for (error of serverErrors; track error) {
        <li>{{ error }}</li>
      }
    </ul>
  </div>
}
```

### Mapping Server Errors to Form Fields (Advanced)

If you want to map server errors to specific form fields (similar to Blazor's `FluentValidationMessage`), you can use `setErrors`:

```typescript
private handleError(err: HttpErrorResponse): void {
  if (err.status === 400 && err.error?.errors) {
    const errorObj = err.error.errors;

    for (const [fieldName, fieldErrors] of Object.entries(errorObj)) {
      const control = this.form.get(fieldName.toLowerCase());

      if (control && Array.isArray(fieldErrors)) {
        control.setErrors({
          serverError: { message: fieldErrors[0] }
        });
      }
    }
  } else {
    // Show generic errors in a summary section
    this.serverErrors = ['An unexpected error occurred'];
  }
}
```

Then in the template:

```html
@if (form.controls.email.hasError('serverError')) {
  <mat-error>{{ form.controls.email.errors?.['serverError'].message }}</mat-error>
}
```

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
| **Submit handler** | `OnValidSubmit="HandleSubmit"` | `(ngSubmit)="onSubmit()"` |
| **Validation trigger** | Automatic via EditContext | Automatic via FormControl state |
| **Disable on submit** | `Disabled="@isSubmitting"` | `[disabled]="form.invalid \|\| isSubmitting"` |
| **Date picker** | `<FluentDatePicker @bind-Value="model.DateOfBirth" />` | `<input matInput [matDatepicker]="picker">` |
| **Server errors** | `FluentValidationSummary` | Custom error display with `@for` loop |

### Similarities

- Both enforce validation before allowing submission
- Both separate client validation (UX) from server validation (security)
- Both provide visual feedback for invalid fields
- Both handle server validation errors returned from the API

### Key Differences

- **Blazor** uses a POCO model that directly maps to the command/DTO sent to the API
- **Angular** uses `FormGroup` which is then converted to the API payload via `getRawValue()`
- **Blazor** validation is defined in C# validator classes (server-side first, optionally reused client-side)
- **Angular** validation is defined as TypeScript functions (client-side first, separate from server FluentValidation)

---

## Verification Checklist

After implementing forms and validation, verify the following:

- [ ] **Reactive form created** - Form is created using `FormBuilder` with `nonNullable.group()`
- [ ] **Required validators** - All required fields have `Validators.required`
- [ ] **Email validation** - Email field has `Validators.email`
- [ ] **Custom validators** - Date of birth validated with `pastDateValidator` (and optionally `minAgeValidator`)
- [ ] **Error messages** - `mat-error` displays appropriate validation messages for each error type
- [ ] **Submit button state** - Submit button is disabled when form is invalid or while submitting
- [ ] **Loading indicator** - Submit button text changes to "Creating..." during submission
- [ ] **Server validation errors** - Server validation errors are extracted and displayed to the user
- [ ] **Success notification** - Successful creation shows a snackbar notification
- [ ] **Navigation on success** - User is navigated to patient list after successful creation
- [ ] **Cancel button** - Cancel button navigates back to patient list without submitting

### Testing Your Form

1. **Test required field validation** - Leave fields empty and try to submit
2. **Test email format validation** - Enter invalid email formats (e.g., "notanemail")
3. **Test maxLength validation** - Enter more than 100 characters in name fields
4. **Test date validation** - Try to select a future date for date of birth
5. **Test submit button state** - Verify it's disabled when form is invalid
6. **Test server validation** - Submit with data that will fail server validation (e.g., duplicate email)
7. **Test successful submission** - Create a valid patient and verify navigation and notification

---

## Navigation

- **Previous**: [04-angular-state-management.md](./04-angular-state-management.md)
- **Back to overview**: [../00-frontend-overview.md](../00-frontend-overview.md)
