---
name: 'ddd-frontend-form'
description: >
  Add a create or edit form to a feature in the Angular SPA
  (Frontend/Angular/Scheduling.AngularApp). Uses Angular reactive forms
  (FormBuilder.nonNullable.group + Validators), Angular Material form fields, an
  isSubmitting signal, submission through the feature's API service, and the
  NotificationService for success/error feedback. Use after ddd-frontend-feature,
  or standalone for any form need.
paths: Frontend/Angular/**
---

# DDD Frontend Form (Angular)

Adds a reactive-forms component to a feature. Mirror the live `create-patient` component (`src/app/features/patients/create-patient/`). Standalone component, `ChangeDetectionStrategy.OnPush`, signals for transient UI state, Material form fields.

## Conventions
- **Reactive forms**: build with `inject(FormBuilder).nonNullable.group({...})` and `Validators.*`. Never template-driven (`ngModel`) for feature forms.
- Material modules in `imports`: `ReactiveFormsModule`, `MatFormFieldModule` (or `MatFormField`/`MatLabel`/`MatError`), `MatInputModule`, plus `MatButtonModule`; add `MatSelectModule`, `MatDatepickerModule` + `MatNativeDateModule`, `MatCheckboxModule` as fields require.
- `isSubmitting = signal(false)` disables the submit button while the request is in flight.
- On submit: guard `if (this.form.invalid) { this.form.markAllAsTouched(); return; }`, read with `getRawValue()`, transform as needed, call the service, handle the `SuccessOrFailureResponse`.
- Feedback via `NotificationService` (`@core/services/notification`): `.success(msg)` / `.error(msg)`. Navigate on success with the injected `Router`.
- DI via `inject()`; service/`FormBuilder`/`NotificationService` are `private`, `Router` public (used in template).
- Forms call the existing API service (`@core/services/<feature>-api`) — don't put HTTP in the component.

## Step 1 — Clarify scope

A form is a configuration — settle each item before writing. Source of answers, in order: (1) an existing plan/spec you're implementing — trust it, summarise it back; (2) the user's message; (3) ask only the gaps.

- **Placement**: which feature folder; standalone page vs section vs dialog; component file name (`create-<feature>`, `edit-<feature>`).
- **Mode**: create (POST) or edit (PUT/PATCH)? For edit, where do initial values come from (loaded via the API service in `ngOnInit`)?
- **Endpoint**: which API service method; request/response shape.
- **Fields** (per field): control name (matches the request contract), Material control type, label, required?, options/enum for selects, min/max for numbers, date format.
- **Validation**: client rules (required, email, min length, pattern…); server rules to expect (uniqueness, conflict) and how to surface them.
- **Buttons**: submit label + pending label; cancel behaviour (navigate back / none).
- **Post-submit**: navigate where; notify; for edit, reload.

## Step 2 — Create-form component

`src/app/features/<feature>/create-<feature>/create-<feature>.ts`:

```typescript
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { <Feature>Api } from '@core/services/<feature>-api';
import { Create<Feature>Request, Create<Feature>Response } from '@core/models/<feature>.model';
import { NotificationService } from '@core/services/notification';

@Component({
  selector: 'app-create-<feature>',
  standalone: true,
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  templateUrl: './create-<feature>.html',
  styleUrl: './create-<feature>.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Create<Feature> {
  private service = inject(<Feature>Api);
  private fb = inject(FormBuilder);
  private notification = inject(NotificationService);
  router = inject(Router);

  isSubmitting = signal(false);

  form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    status: ['Active', Validators.required],
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const request: Create<Feature>Request = this.form.getRawValue();

    this.isSubmitting.set(true);
    this.service.create(request).subscribe({
      next: (response: Create<Feature>Response) => {
        if (response.success) {
          this.notification.success(response.message);
          this.router.navigate(['/<feature>s']);
        } else {
          this.notification.error(response.message);
          this.isSubmitting.set(false);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.notification.error('Something went wrong.');
        this.isSubmitting.set(false);
      },
    });
  }
}
```

`src/app/features/<feature>/create-<feature>/create-<feature>.html`:

```html
<h1>Create <Feature></h1>

<form [formGroup]="form" (ngSubmit)="submit()">
  <mat-form-field>
    <mat-label>Name</mat-label>
    <input matInput formControlName="name" required />
    <mat-error>Name is required</mat-error>
  </mat-form-field>

  <mat-form-field>
    <mat-label>Status</mat-label>
    <mat-select formControlName="status">
      <mat-option value="Active">Active</mat-option>
      <mat-option value="Suspended">Suspended</mat-option>
    </mat-select>
  </mat-form-field>

  <div class="actions">
    <button mat-flat-button color="primary" type="submit" [disabled]="isSubmitting()">Create</button>
    <button mat-button type="button" (click)="router.navigate(['/<feature>s'])">Cancel</button>
  </div>
</form>
```

## Field cookbook

- **Date** (import `MatDatepickerModule`, `MatNativeDateModule`):
  ```html
  <mat-form-field>
    <mat-label>Date of birth</mat-label>
    <input matInput [matDatepicker]="picker" formControlName="dateOfBirth" required />
    <mat-datepicker-toggle matIconSuffix [for]="picker" />
    <mat-datepicker #picker />
  </mat-form-field>
  ```
  Control type is `Date`; declare it as `dateOfBirth: [null as Date | null, Validators.required]` and convert to the API's `yyyy-MM-dd` in `submit()`: `const dob = raw.dateOfBirth!; ...dateOfBirth: dob.toISOString().split('T')[0]`.
- **Email**: `email: ['', [Validators.required, Validators.email]]`.
- **Number**: `<input matInput type="number" formControlName="qty" />`, control `qty: [0, [Validators.min(1)]]`.
- **Checkbox** (`MatCheckboxModule`): `<mat-checkbox formControlName="active">Active</mat-checkbox>`, control `active: [false]`.

When the form shape and the API request shape diverge (date formatting, omitting computed fields), build the request object explicitly in `submit()` instead of passing `getRawValue()` straight through.

## Step 3 — Edit-form variant

Inject `ActivatedRoute`, load current values in `ngOnInit`, and `patchValue` the form:

```typescript
ngOnInit(): void {
  const id = this.route.snapshot.paramMap.get('id')!;
  this.service.getById(id).subscribe((item) => this.form.patchValue(item));
}
// submit() calls this.service.update(id, request) and navigates to the detail page on success.
```

(Add an `update` method to the API service if it doesn't exist — see `ddd-frontend-feature` Step 3.)

## Step 4 — Register the route

In `src/app/app.routes.ts`, lazy-load the form, guarded — and put a static `create` path **before** any `:id` route (see `ddd-frontend-feature` Step 6):

```typescript
{
  path: '<feature>s/create',
  loadComponent: () => import('./features/<feature>/create-<feature>/create-<feature>').then(m => m.Create<Feature>),
  canActivate: [authGuard],
},
```

## Step 5 — Verify

```
cd Frontend\Angular\Scheduling.AngularApp
npm run build
```

Check: control names match the request contract; validators present; submit guards on `form.invalid`; `isSubmitting` toggled in all branches; success navigates + notifies. Then exercise the golden path and one validation-error path with `npm start`.
