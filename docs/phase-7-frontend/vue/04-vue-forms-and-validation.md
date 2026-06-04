# Vue — Forms and Validation

> **Track:** Vue frontend track. For the Angular equivalent see [angular/04-angular-forms-and-validation.md](../angular/04-angular-forms-and-validation.md).

This document covers form handling and validation in the Vue track using **VeeValidate** with a **Zod** schema, integrated with PrimeVue input components and the `useCreatePatient` TanStack Query mutation. A single Zod schema gives type-safe, declarative validation that mirrors the backend FluentValidation rules.

---

## Forms Overview

Where the Angular track uses Reactive Forms (`FormBuilder` + `Validators`), the Vue track uses:

| Piece | Role | Angular counterpart |
|-------|------|---------------------|
| **Zod** | Declarative schema describing valid shape + rules | `Validators.required`, `Validators.email`, custom `ValidatorFn` |
| **VeeValidate** | Binds the schema to fields, tracks touched/dirty/errors | `FormGroup` + `FormControl` state |
| **`@vee-validate/zod`** | `toTypedSchema()` adapter bridging Zod → VeeValidate | n/a |
| **PrimeVue inputs** | `InputText`, `DatePicker`, `Select` | Angular Material form fields |
| **TanStack Query mutation** | Submits the validated payload | `PatientApi.create().subscribe()` |

**Why this stack?**
- **Schema-first** — one Zod object defines both the runtime validation and the inferred TypeScript type
- **Type safety** — `z.infer<typeof schema>` gives the form value type for free
- **Mirrors the backend** — the Zod rules read almost identically to the server's FluentValidation rules
- **Composable** — VeeValidate's `useForm` / `useField` fit naturally into `<script setup>`

---

## Install

```bash
npm install vee-validate @vee-validate/zod zod
```

| Package | Purpose |
|---------|---------|
| `vee-validate` | Form state + validation engine for Vue |
| `@vee-validate/zod` | `toTypedSchema()` adapter for Zod schemas |
| `zod` | Schema declaration + TypeScript inference |

---

## The Validation Schema

Define a Zod schema that mirrors the backend's create-patient rules (see the overview doc for the authoritative rules: first/last name required, valid email, date of birth required).

**File:** `src/features/patients/create-patient.schema.ts`

```typescript
import { z } from 'zod';

export const createPatientSchema = z.object({
  firstName: z.string().min(1, 'First name is required'),
  lastName: z.string().min(1, 'Last name is required'),
  email: z.string().min(1, 'Email is required').email('Enter a valid email'),
  dateOfBirth: z.date({ required_error: 'Date of birth is required' }),
  status: z.string().default('Active'),
});

// Inferred form-value type — no separate interface needed.
export type CreatePatientForm = z.infer<typeof createPatientSchema>;
```

> **Note:** `dateOfBirth` is a `Date` here because PrimeVue's `DatePicker` binds a `Date` object. We format it to `yyyy-MM-dd` when mapping to the API request (which expects a string).

---

## Create Patient Form

The complete create form: VeeValidate `useForm` with the Zod schema, `useField` per input bound to PrimeVue components, error messages, a submit handler that maps to the typed request and fires the mutation, and a submit button disabled while the mutation is in flight.

**File:** `src/features/patients/CreatePatient.vue`

```vue
<script setup lang="ts">
import { useRouter } from 'vue-router';
import { useForm } from 'vee-validate';
import { toTypedSchema } from '@vee-validate/zod';
import InputText from 'primevue/inputtext';
import DatePicker from 'primevue/datepicker';
import Button from 'primevue/button';
import Message from 'primevue/message';
import { createPatientSchema } from './create-patient.schema';
import { useCreatePatient } from '@core/composables/use-patients';
import { useNotifications } from '@core/composables/use-notifications';
import type { CreatePatientRequest } from '@core/models/patient';

const router = useRouter();
const notify = useNotifications();
const create = useCreatePatient();

// VeeValidate form bound to the Zod schema.
const { handleSubmit, errors, defineField } = useForm({
  validationSchema: toTypedSchema(createPatientSchema),
  initialValues: { status: 'Active' },
});

// defineField gives a [model, props] pair per field for v-model binding.
const [firstName] = defineField('firstName');
const [lastName] = defineField('lastName');
const [email] = defineField('email');
const [dateOfBirth] = defineField('dateOfBirth');

// handleSubmit only runs the callback when the schema validates.
const submit = handleSubmit((values) => {
  const request: CreatePatientRequest = {
    firstName: values.firstName,
    lastName: values.lastName,
    email: values.email,
    dateOfBirth: values.dateOfBirth.toISOString().split('T')[0], // yyyy-MM-dd
    status: values.status,
  };

  create.mutate(request, {
    onSuccess: (res) => {
      if (res.success) {
        notify.success(res.message);
        router.push('/patients');
      } else {
        notify.error(res.message);
      }
    },
    onError: (err) => notify.error(err.message),
  });
});
</script>

<template>
  <h1 class="text-2xl font-bold mb-4">Create Patient</h1>

  <form class="flex flex-col gap-4 max-w-md" @submit.prevent="submit">
    <div class="flex flex-col gap-1">
      <label for="firstName">First Name</label>
      <InputText id="firstName" v-model="firstName" :invalid="!!errors.firstName" />
      <Message v-if="errors.firstName" severity="error" size="small" variant="simple">
        {{ errors.firstName }}
      </Message>
    </div>

    <div class="flex flex-col gap-1">
      <label for="lastName">Last Name</label>
      <InputText id="lastName" v-model="lastName" :invalid="!!errors.lastName" />
      <Message v-if="errors.lastName" severity="error" size="small" variant="simple">
        {{ errors.lastName }}
      </Message>
    </div>

    <div class="flex flex-col gap-1">
      <label for="email">Email</label>
      <InputText id="email" v-model="email" type="email" :invalid="!!errors.email" />
      <Message v-if="errors.email" severity="error" size="small" variant="simple">
        {{ errors.email }}
      </Message>
    </div>

    <div class="flex flex-col gap-1">
      <label for="dateOfBirth">Date of Birth</label>
      <DatePicker id="dateOfBirth" v-model="dateOfBirth" date-format="yy-mm-dd" :invalid="!!errors.dateOfBirth" />
      <Message v-if="errors.dateOfBirth" severity="error" size="small" variant="simple">
        {{ errors.dateOfBirth }}
      </Message>
    </div>

    <div class="flex gap-2">
      <Button label="Create" type="submit" :loading="create.isPending" />
      <Button label="Cancel" text type="button" @click="router.push('/patients')" />
    </div>
  </form>
</template>
```

### Key Points

- **`toTypedSchema(createPatientSchema)`** — adapts the Zod schema so VeeValidate validates against it and infers field types
- **`defineField`** — returns a `v-model`-compatible model per field; bind it directly to PrimeVue inputs
- **`handleSubmit`** — runs its callback only when the schema passes; otherwise it populates `errors` and skips submission (the counterpart to Angular's `if (form.invalid) markAllAsTouched()`)
- **`errors.<field>`** — reactive per-field message string, shown via PrimeVue `Message`
- **`:invalid`** — PrimeVue's invalid-state styling, driven by the presence of an error
- **`create.isPending`** — TanStack Query's reactive mutation-pending flag, bound to the button's `:loading` (no `.value` in templates — Vue auto-unwraps it) to prevent double submission
- **Typed request** — map the form values to `CreatePatientRequest`, formatting the date to `yyyy-MM-dd`

---

## Custom Validation Rules

Zod expresses domain-specific rules with `.refine()` — the counterpart to Angular's custom `ValidatorFn`. These mirror server-side business rules.

> **Future enhancement:** like the Angular track, these stricter date-of-birth rules are documented as ready-to-use examples, not yet wired into the form.

```typescript
import { z } from 'zod';

function ageOnOrBefore(date: Date): number {
  const today = new Date();
  let age = today.getFullYear() - date.getFullYear();
  const m = today.getMonth() - date.getMonth();
  if (m < 0 || (m === 0 && today.getDate() < date.getDate())) age--;
  return age;
}

export const createPatientSchema = z.object({
  firstName: z.string().min(1, 'First name is required'),
  lastName: z.string().min(1, 'Last name is required'),
  email: z.string().min(1, 'Email is required').email('Enter a valid email'),
  dateOfBirth: z
    .date({ required_error: 'Date of birth is required' })
    .refine((d) => d < new Date(), 'Date of birth must be in the past')
    .refine((d) => ageOnOrBefore(d) >= 18, 'Patient must be at least 18 years old'),
  status: z.string().default('Active'),
});
```

Each `.refine()` attaches its own message, which surfaces through `errors.dateOfBirth` exactly like the built-in rules — no template change needed.

---

## Client-Side vs Server-Side Validation

Just like the Angular and Blazor tracks, validation lives at both layers but serves different purposes.

| Layer | Purpose | Technology | When It Runs |
|-------|---------|------------|--------------|
| **Client (Vue)** | UX — immediate feedback | VeeValidate + Zod schema | Before submission |
| **Server (API)** | Security — authoritative | FluentValidation (pipeline behavior) | During request processing |

### Principles

1. **Client validation is for UX only** — fast feedback, fewer wasted round-trips
2. **Server validation is authoritative** — never trust the client for data integrity or security
3. **Keep them in sync** — the Zod schema should mirror the FluentValidation rules
4. **Server errors win** — surface server validation failures prominently (here, via the error toast on a non-`success` response)

The create mutation already handles the server's verdict: a `SuccessOrFailureResponse` with `success: false` triggers `notify.error(res.message)` rather than navigating away.

---

## Vue vs Angular Forms Comparison

| Concept | Angular | Vue |
|---------|---------|-----|
| **Form container** | `<form [formGroup]="form">` | `<form @submit.prevent="submit">` |
| **Validation library** | Built-in `Validators` + custom `ValidatorFn` | Zod schema via VeeValidate |
| **Form model** | `FormGroup` from `FormBuilder` | `useForm` + `defineField` (typed from Zod) |
| **Data binding** | `formControlName="firstName"` | `v-model="firstName"` (from `defineField`) |
| **Error display** | `<mat-error>` + `hasError()` | `<Message>` + `errors.field` |
| **Submit handler** | `(ngSubmit)="submit()"` | `handleSubmit(cb)` |
| **Guard invalid submit** | `if (form.invalid) markAllAsTouched()` | `handleSubmit` skips the callback automatically |
| **Disable on submit** | `[disabled]="isSubmitting()"` | `:loading="create.isPending"` |
| **Date picker** | `<input matInput [matDatepicker]>` | `<DatePicker v-model>` |
| **Custom rule** | custom `ValidatorFn` | Zod `.refine()` |
| **Server errors** | `NotificationService` snackbar | `useToast()` error toast |

### Key Differences

- **Angular** defines validation imperatively (validator functions per control); **Vue** defines it declaratively (one Zod schema) and infers the form type from it
- **Angular** converts `FormGroup` values via `getRawValue()`; **Vue** receives already-validated, typed `values` in the `handleSubmit` callback
- Both keep client validation (UX) separate from server FluentValidation (security)

---

## Verification Checklist

- [ ] `vee-validate`, `@vee-validate/zod`, `zod` installed
- [ ] `createPatientSchema` (Zod) defines required first/last name, valid email, required date of birth
- [ ] Form uses `useForm({ validationSchema: toTypedSchema(createPatientSchema) })`
- [ ] Each field bound via `defineField` + `v-model` to a PrimeVue input
- [ ] `errors.<field>` rendered through PrimeVue `Message` and `:invalid` styling
- [ ] `handleSubmit` only submits when the schema validates
- [ ] Submit maps values to `CreatePatientRequest` with `yyyy-MM-dd` date formatting
- [ ] Submit button shows `:loading` from the mutation's `isPending`
- [ ] Success shows a toast and navigates to `/patients`; `success: false` shows an error toast
- [ ] Cancel navigates back without submitting

### Testing Your Form

1. Submit empty — verify required messages appear and submission is blocked
2. Enter an invalid email (e.g. `notanemail`) — verify the email message
3. Verify the submit button is disabled/loading while the request is in flight
4. Submit data that fails server validation (e.g. duplicate email) — verify the error toast
5. Submit valid data — verify navigation and the success toast
6. Click Cancel — verify navigation back without submission

---

## Navigation

- **Previous:** [03-vue-components-and-routing.md](./03-vue-components-and-routing.md)
- **Back to overview:** [../00-frontend-overview.md](../00-frontend-overview.md)
