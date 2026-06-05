# Vue — Consuming APIs

> **Track:** Vue frontend track.

## Overview

This document covers how the Vue app consumes the backend APIs. Where the Angular track manually subscribes to RxJS observables and reloads data by hand, the Vue track uses **TanStack Query** to manage server state — caching, loading/error flags, background refetching, and cache invalidation after mutations all come for free.

The layering is:

1. **Models** — TypeScript interfaces matching the backend DTOs (identical contract to the Angular track)
2. **API layer** (`core/api/`) — thin `fetch` wrappers, one function per endpoint, returning typed promises
3. **Query composables** (`core/composables/`) — `useQuery` / `useMutation` hooks built on the API layer
4. **Components** — consume the composables; they never call `fetch` directly

---

## How Vue Calls APIs

Vue has no built-in HTTP client (unlike Angular's `HttpClient`). We use the native `fetch` API wrapped in small typed functions. TanStack Query then wraps those functions to add caching and reactivity.

- **API functions return `Promise<T>`** — plain async functions, easy to test
- **TanStack Query** turns a promise-returning function into reactive `data` / `isPending` / `isError` refs
- **CORS** is configured on the backend for the Vue origin (`https://localhost:7004`, see doc 01)
- **Environment** base URLs come from `import.meta.env`

> **Why not Axios?** `fetch` is sufficient and dependency-free. If you prefer Axios for interceptors, it drops in behind the same API-layer functions without changing the composables or components.

---

## Shared Response Model

The backend uses a common `SuccessOrFailureDto` base class for command responses. Define its TypeScript equivalent in `shared/` so it can be reused across bounded contexts.

**File:** `src/shared/models/success-or-failure-response.ts`

```typescript
/**
 * Base response for commands that return success/failure.
 * Maps to BuildingBlocks.Application.Dtos.SuccessOrFailureDto on the backend.
 */
export interface SuccessOrFailureResponse {
  success: boolean;
  message: string;
}
```

---

## Patient Model

Define interfaces that match the backend DTOs. This is the **same contract** the Angular track uses — only the surrounding framework differs.

**File:** `src/core/models/patient.ts`

```typescript
import type { SuccessOrFailureResponse } from '@shared/models/success-or-failure-response';

/**
 * Lifecycle status of a patient. A `const` object plus a derived type, so the
 * names work as values (`PatientStatus.Suspended`) and as a type
 * (`status: PatientStatus`). The values are the wire strings, so API responses
 * assign without a cast.
 */
export const PatientStatus = {
  Active: 'Active',
  Suspended: 'Suspended',
  Deleted: 'Deleted',
} as const;

export type PatientStatus = (typeof PatientStatus)[keyof typeof PatientStatus];

/** Patient entity returned from the API */
export interface Patient {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  dateOfBirth: string;  // ISO 8601 date string
  status: PatientStatus;
}

/** Request model for creating a new patient */
export interface CreatePatientRequest {
  firstName: string;
  lastName: string;
  email: string;
  dateOfBirth: string;  // yyyy-MM-dd format
  status: PatientStatus;
}

/** Response from the CreatePatient command */
export interface CreatePatientResponse extends SuccessOrFailureResponse {
  patientId: string;
}

/** Query parameters for filtering patients */
export interface PatientFilterParams {
  status?: string;
}
```

**Key points:**
- Match property names exactly to the backend DTOs (case-sensitive)
- Use `string` for dates — convert to/from `Date` in components as needed
- Use `import type` for type-only imports (Vite/TypeScript best practice)
- `PatientStatus` mirrors the backend SmartEnum and is a single source of truth for the entity and create request, letting consumers (e.g. `severityFor`) reason exhaustively. It's a `const` object + derived type (declaration merging) rather than a bare union, so the members double as values — `PatientStatus.Suspended` instead of the magic string `'Suspended'`. Prefer this over a TS `enum`: the values *are* the wire strings (so API responses assign without a cast) and nothing extra is emitted at runtime. Because it's now also a value, files that reference it must use a value import (`import { type Patient, PatientStatus }`), not `import type`, under `verbatimModuleSyntax`. It remains a compile-time assertion about the response shape, not runtime validation. Note `PatientFilterParams.status` stays a loose `string` on purpose — the filter dropdown adds an `''` ("All") option that isn't a real status.

---

## API Layer

Thin, framework-agnostic functions. No Vue, no TanStack Query here — just `fetch` and types. This keeps them trivially testable and reusable.

**File:** `src/core/api/patient-api.ts`

```typescript
import type {
  Patient,
  CreatePatientRequest,
  CreatePatientResponse,
  PatientFilterParams,
} from '@core/models/patient';
import type { SuccessOrFailureResponse } from '@shared/models/success-or-failure-response';

const baseUrl = `${import.meta.env.VITE_SCHEDULING_API_URL}/api/patients`;

/** Throw on non-2xx so TanStack Query routes it to the error state. */
async function json<T>(response: Response): Promise<T> {
  if (!response.ok) {
    throw new Error(`Request failed: ${response.status} ${response.statusText}`);
  }
  return response.json() as Promise<T>;
}

export const patientApi = {
  getAll(params?: PatientFilterParams): Promise<Patient[]> {
    const query = params?.status ? `?status=${encodeURIComponent(params.status)}` : '';
    return fetch(`${baseUrl}${query}`).then(json<Patient[]>);
  },

  getById(id: string): Promise<Patient> {
    return fetch(`${baseUrl}/${id}`).then(json<Patient>);
  },

  create(request: CreatePatientRequest): Promise<CreatePatientResponse> {
    return fetch(baseUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    }).then(json<CreatePatientResponse>);
  },

  suspend(id: string): Promise<SuccessOrFailureResponse> {
    return fetch(`${baseUrl}/${id}/suspend`, { method: 'POST' }).then(json<SuccessOrFailureResponse>);
  },

  activate(id: string): Promise<SuccessOrFailureResponse> {
    return fetch(`${baseUrl}/${id}/activate`, { method: 'POST' }).then(json<SuccessOrFailureResponse>);
  },

  delete(id: string): Promise<SuccessOrFailureResponse> {
    return fetch(`${baseUrl}/${id}`, { method: 'DELETE' }).then(json<SuccessOrFailureResponse>);
  },
};
```

**Design decisions:**
- One exported object with one method per endpoint — mirrors the Angular `PatientApi` service
- Functions throw on non-2xx so TanStack Query's error handling kicks in
- No subscriptions, no observables — just promises

---

## TanStack Query Composables

This is where the Vue track diverges most from Angular. A **composable** is a function that uses Vue's reactivity and Query hooks. Components call these instead of touching the API layer directly.

### Query keys

TanStack Query caches by **query key**. Centralise the keys so invalidation stays consistent. Note the `lists` **prefix** key — invalidating it matches *every* filtered list variant (`'all'`, `'Active'`, `'Suspended'`, …), not just one:

```typescript
const patientKeys = {
  all: ['patients'] as const,
  lists: ['patients', 'list'] as const,                              // prefix: matches all list variants
  list: (status?: string) => ['patients', 'list', status ?? 'all'] as const,
  detail: (id: string) => ['patients', 'detail', id] as const,
};
```

> **Prefix matching:** `invalidateQueries({ queryKey: ['patients', 'list'] })` invalidates every query whose key *starts with* `['patients', 'list']`. That's why mutations invalidate `patientKeys.lists` (the prefix) rather than `patientKeys.list('all')` — otherwise suspending a patient while the list is filtered to "Active" wouldn't refresh it.

### Read composables (`useQuery`)

**File:** `src/core/composables/use-patients.ts`

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import { patientApi } from '@core/api/patient-api';
import type { CreatePatientRequest } from '@core/models/patient';

const patientKeys = {
  all: ['patients'] as const,
  lists: ['patients', 'list'] as const,
  list: (status?: string) => ['patients', 'list', status ?? 'all'] as const,
  detail: (id: string) => ['patients', 'detail', id] as const,
};

/**
 * List patients, optionally filtered by status.
 * `status` may be a ref/getter — when it changes, the query refetches automatically.
 */
export function usePatients(status: MaybeRefOrGetter<string | undefined>) {
  // A COMPUTED key is the reactive primitive vue-query tracks. When `status`
  // changes the computed re-evaluates, vue-query sees a new key, and refetches.
  const queryKey = computed(() => patientKeys.list(toValue(status) || undefined));
  return useQuery({
    queryKey,
    queryFn: () => patientApi.getAll({ status: toValue(status) || undefined }),
  });
}

/** Fetch a single patient by id. */
export function usePatient(id: MaybeRefOrGetter<string>) {
  const queryKey = computed(() => patientKeys.detail(toValue(id)));
  return useQuery({
    queryKey,
    queryFn: () => patientApi.getById(toValue(id)),
  });
}
```

> **Reactive keys (important):** vue-query makes a query reactive by tracking **refs / computeds** in the key — it unwraps them automatically. It does **not** call plain getter functions placed in the key array (they'd be serialised to `null`, leaving the key constant and the query stuck). So wrap the key in `computed(...)`. When the status filter changes, the computed key changes and vue-query refetches — replacing the Angular doc's manual `loadPatients()` re-fire on `(selectionChange)`.

### Write composables (`useMutation` + invalidation)

```typescript
/** Create a patient, then invalidate the list so it refetches. */
export function useCreatePatient() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreatePatientRequest) => patientApi.create(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: patientKeys.all }),
  });
}

/** Suspend a patient, then invalidate both the detail and list caches. */
export function useSuspendPatient() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => patientApi.suspend(id),
    onSuccess: (_result, id) => {
      queryClient.invalidateQueries({ queryKey: patientKeys.detail(id) });
      queryClient.invalidateQueries({ queryKey: patientKeys.lists }); // prefix → all filter variants
    },
  });
}

export function useActivatePatient() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => patientApi.activate(id),
    onSuccess: (_result, id) => {
      queryClient.invalidateQueries({ queryKey: patientKeys.detail(id) });
      queryClient.invalidateQueries({ queryKey: patientKeys.lists });
    },
  });
}

export function useDeletePatient() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => patientApi.delete(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: patientKeys.all }),
  });
}
```

**What invalidation buys you:** after a successful suspend/activate/delete/create, the relevant cached queries are marked stale and refetch automatically. There is no manual `loadPatients()` call — the list and detail views update themselves.

---

## Using a Query in a Component

The composable returns reactive refs you can bind directly in the template.

```vue
<script setup lang="ts">
import { ref } from 'vue';
import { usePatients } from '@core/composables/use-patients';

const status = ref<string>('');               // bound to the filter dropdown
const { data: patients, isPending, isError, error } = usePatients(status);
</script>

<template>
  <div v-if="isPending">Loading patients…</div>
  <div v-else-if="isError" class="text-red-600">{{ error?.message }}</div>
  <div v-else-if="!patients?.length">No patients found.</div>
  <ul v-else>
    <li v-for="p in patients" :key="p.id">{{ p.firstName }} {{ p.lastName }}</li>
  </ul>
</template>
```

| State ref | Meaning |
|-----------|---------|
| `data` | The resolved query result (typed as `Patient[]`), or `undefined` while pending |
| `isPending` | `true` during the initial fetch — drives the loading spinner |
| `isError` / `error` | Error state and the thrown `Error` |
| `refetch()` | Imperatively re-run the query (rarely needed — invalidation handles it) |

---

## Using a Mutation in a Component

```vue
<script setup lang="ts">
import { useSuspendPatient } from '@core/composables/use-patients';
import { useNotifications } from '@core/composables/use-notifications';

const suspend = useSuspendPatient();
const notify = useNotifications();

function onSuspend(id: string) {
  suspend.mutate(id, {
    onSuccess: (res) => res.success ? notify.success(res.message) : notify.error(res.message),
    onError: (err) => notify.error(err.message),
  });
}
</script>

<template>
  <Button label="Suspend" :loading="suspend.isPending" @click="onSuspend(patient.id)" />
</template>
```

Bind `suspend.isPending` directly in the template (no `.value` — vue-query's result object is reactive and Vue auto-unwraps it in templates) to disable/spin the button while the request is in flight. This replaces the Angular doc's manual `isSubmitting` signal for mutations.

---

## Notifications with PrimeVue Toast

PrimeVue's `useToast()` is the counterpart to Angular's `MatSnackBar` wrapper. Wrap it in a composable so components call `success()` / `error()` without repeating config.

### Mount the Toast container once

**File: `src/App.vue`** — add `<Toast />` near the root so toasts render app-wide:

```vue
<script setup lang="ts">
import Toast from 'primevue/toast';
</script>

<template>
  <Toast />
  <router-view />
</template>
```

> `ToastService` was registered in `main.ts` in doc 01 — that's the plugin; `<Toast />` is the visual container.

### Notification composable

**File:** `src/core/composables/use-notifications.ts`

```typescript
import { useToast } from 'primevue/usetoast';

export function useNotifications() {
  const toast = useToast();
  return {
    success(message: string) {
      toast.add({ severity: 'success', summary: 'Success', detail: message, life: 3000 });
    },
    error(message: string) {
      toast.add({ severity: 'error', summary: 'Error', detail: message, life: 5000 });
    },
  };
}
```

> **Production note:** For this learning project we call `useNotifications()` in each mutation callback. In production you would centralise error toasts in the `QueryClient`'s global `onError` (via `MutationCache` / `QueryCache`), keeping components free of error-handling boilerplate — the equivalent of Angular's global `HttpInterceptor`.

---

## CORS Recap

Vue runs on `https://localhost:7004`; the APIs run on `7001`/`7002`. The backend must allow the Vue origin via CORS (configured **additively** alongside Angular's origin in doc 01, Step 8). Verify:

1. Start the backend (via Aspire) and the Vue dev server
2. Open the browser console (F12)
3. Trigger an API call
4. Confirm: no CORS errors, network tab shows `200`, response carries `Access-Control-Allow-Origin: https://localhost:7004`

---

## Vue Query vs Angular HttpClient

| Aspect | Angular HttpClient | Vue + TanStack Query |
|--------|--------------------|----------------------|
| **HTTP client** | Built-in `HttpClient` | Native `fetch` (or Axios) |
| **Return type** | `Observable<T>` | `Promise<T>` (wrapped into reactive refs by Query) |
| **Reactivity** | Manual `subscribe()` → `signal.set()` | `data` / `isPending` refs auto-update |
| **Caching** | None (manual) | Built-in, keyed by `queryKey` |
| **Refetch after write** | Manual `loadPatients()` | `invalidateQueries` → automatic refetch |
| **Loading state** | Manual `isLoading` signal | `isPending` ref |
| **Error state** | `catchError` / `error` callback | `isError` / `error` ref |
| **Dedup / background refetch** | Not built-in | Built-in (staleTime, window focus, etc.) |
| **Cancellation** | `takeUntilDestroyed()` | Automatic on unmount / key change |

**Key insight:** Angular's `Observable` is a general-purpose stream you wire up manually for each call. TanStack Query is a purpose-built **server-state cache** — it removes the loading/error/refetch boilerplate the Angular doc writes by hand, at the cost of learning the query-key + invalidation model.

---

## Verification Checklist

- [ ] `SuccessOrFailureResponse` and `Patient` models defined matching backend DTOs
- [ ] `patient-api.ts` exposes `getAll`, `getById`, `create`, `suspend`, `activate`, `delete`
- [ ] API functions throw on non-2xx responses
- [ ] `usePatients` / `usePatient` read composables built on `useQuery`
- [ ] List query key is reactive to the status filter (refetches on change)
- [ ] `useCreatePatient` / `useSuspendPatient` / `useActivatePatient` / `useDeletePatient` mutations built on `useMutation`
- [ ] Each mutation invalidates the relevant query keys in `onSuccess`
- [ ] `<Toast />` mounted in `App.vue`; `use-notifications.ts` wraps `useToast()`
- [ ] Mutations show success/error toasts
- [ ] API requests succeed without CORS errors in the browser console

---

## Navigation

- **Previous:** [01-vue-project-setup.md](./01-vue-project-setup.md)
- **Next:** [03-vue-components-and-routing.md](./03-vue-components-and-routing.md)
- **Up:** [Frontend overview](../00-frontend-overview.md)
