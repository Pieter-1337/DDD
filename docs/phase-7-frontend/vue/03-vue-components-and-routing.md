# Vue Components and Routing

> **Track:** Vue frontend track.

This document covers Vue's component model, Vue Router configuration, and the page implementations for the patient management UI. It uses single-file components (SFCs) with `<script setup>`, the Composition API, and the TanStack Query composables from doc 02. The form-heavy create page is summarised here and covered in depth in doc 04.

---

## Vue Component Model

A Vue **single-file component** (`.vue`) bundles template, logic, and styles in one file. With `<script setup>` (the modern default) the component's logic is plain top-level code — imports, `ref`s, and functions are automatically exposed to the template.

### Component Anatomy

```vue
<script setup lang="ts">
import { ref, computed } from 'vue';

const count = ref(0);                          // reactive state
const doubled = computed(() => count.value * 2); // derived state

function increment() {
  count.value++;
}
</script>

<template>
  <h1>{{ count }} (doubled: {{ doubled }})</h1>
  <button @click="increment">+1</button>
</template>

<!-- Optional scoped styles; we mostly use Tailwind utilities instead -->
<style scoped>
h1 { color: var(--p-primary-color); }
</style>
```

| Block | Purpose |
|-------|---------|
| `<script setup lang="ts">` | Component logic — runs once at setup; top-level bindings are template-visible |
| `<template>` | Markup with Vue directives (`v-if`, `v-for`, `@click`, `:prop`, `v-model`) |
| `<style scoped>` | Optional component-scoped CSS (we prefer Tailwind classes in the template) |

### Reactivity Primitives

| Primitive | Purpose | Example |
|-----------|---------|---------|
| `ref(value)` | Reactive single value; access via `.value` in script, unwrapped in template | `const n = ref(0)` |
| `reactive(obj)` | Reactive object (no `.value`) | `const state = reactive({ n: 0 })` |
| `computed(fn)` | Derived, cached reactive value | `const dbl = computed(() => n.value * 2)` |
| `watch` / `watchEffect` | Run side effects when dependencies change | `watch(id, load)` |

### Component Communication

```vue
<!-- Child.vue -->
<script setup lang="ts">
const props = defineProps<{ data: string }>();
const emit = defineEmits<{ action: [] }>();
</script>

<template>
  <button @click="emit('action')">{{ props.data }}</button>
</template>
```

```vue
<!-- Parent usage -->
<Child :data="myData" @action="handleAction" />
```

| Direction | Mechanism | Angular equivalent |
|-----------|-----------|--------------------|
| Parent → Child | `defineProps` + `:prop` | `@Input()` + `[prop]` |
| Child → Parent | `defineEmits` + `@event` | `@Output()` + `(event)` |

---

## Routing Setup

Vue Router provides client-side navigation with lazy-loaded route components (counterpart to Angular's `loadComponent`).

### Configure Routes

**File: `src/router/index.ts`**

```typescript
import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router';

const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/patients' },
  {
    path: '/patients',
    name: 'patient-list',
    component: () => import('@features/patients/PatientList.vue'), // lazy-loaded
  },
  {
    path: '/patients/create',
    name: 'create-patient',
    component: () => import('@features/patients/CreatePatient.vue'),
  },
  {
    path: '/patients/:id',
    name: 'patient-detail',
    component: () => import('@features/patients/PatientDetail.vue'),
    props: true, // pass :id as a prop
  },
];

const router = createRouter({
  history: createWebHistory(),
  routes,
});

export default router;
```

> **Route ordering:** `/patients/create` is declared **before** `/patients/:id` so the literal `create` path isn't captured by the `:id` parameter. Vue Router matches in declaration order for same-depth routes.

The router was registered in `main.ts` (`app.use(router)`) in doc 01.

### Route Configuration Options

| Option | Purpose | Example |
|--------|---------|---------|
| `path` | URL pattern | `'/patients/:id'` |
| `redirect` | Redirect target | `'/patients'` |
| `component` | Lazy import for code-splitting | `() => import('...')` |
| `props: true` | Pass route params as component props | reads `:id` as a prop |
| `name` | Named route for `router.push({ name })` | `'patient-detail'` |

### Root Component with Navbar

**File: `src/App.vue`**

```vue
<script setup lang="ts">
import Toast from 'primevue/toast';
import Menubar from 'primevue/menubar';
import { useRouter } from 'vue-router';

const router = useRouter();
const items = [
  { label: 'Patients', icon: 'pi pi-users', command: () => router.push('/patients') },
];
</script>

<template>
  <Toast />
  <Menubar :model="items" class="mb-4">
    <template #start>
      <span class="font-bold text-primary px-2">🏥 Patient Management</span>
    </template>
  </Menubar>

  <main class="px-6">
    <router-view />
  </main>
</template>
```

`<router-view />` is where the matched route component renders (the counterpart to Angular's `<router-outlet>`).

---

## Page Implementations

Create the three feature components under `src/features/patients/`.

### Patient List Component

The landing page. It reads all patients via the `usePatients` query composable, displays them in a PrimeVue `DataTable`, and offers a status `Select` to filter. Because the query key is reactive to the status `ref`, changing the filter refetches automatically — no manual reload. A "Create patient" button navigates to the create form.

**File: `src/features/patients/PatientList.vue`**

```vue
<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import DataTable from 'primevue/datatable';
import Column from 'primevue/column';
import Button from 'primevue/button';
import Select from 'primevue/select';
import Tag from 'primevue/tag';
import ProgressSpinner from 'primevue/progressspinner';
import { usePatients } from '@core/composables/use-patients';

const router = useRouter();

const selectedStatus = ref<string>('');
const statusOptions = [
  { label: 'All', value: '' },
  { label: 'Active', value: 'Active' },
  { label: 'Suspended', value: 'Suspended' },
  { label: 'Deleted', value: 'Deleted' },
];

// Reactive query — refetches whenever selectedStatus changes.
const { data: patients, isPending } = usePatients(selectedStatus);

function severityFor(status: string) {
  return status === 'Active' ? 'success' : status === 'Suspended' ? 'warn' : 'danger';
}
</script>

<template>
  <h1 class="text-2xl font-bold mb-4">Patients</h1>

  <div class="flex items-center justify-between mb-4">
    <Select
      v-model="selectedStatus"
      :options="statusOptions"
      option-label="label"
      option-value="value"
      placeholder="Status"
      class="w-48"
    />
    <Button label="Create patient" icon="pi pi-plus" @click="router.push('/patients/create')" />
  </div>

  <div v-if="isPending" class="flex justify-center py-12">
    <ProgressSpinner />
  </div>

  <DataTable v-else :value="patients ?? []" striped-rows>
    <Column field="firstName" header="First Name" />
    <Column field="lastName" header="Last Name" />
    <Column field="email" header="Email" />
    <Column header="Status">
      <template #body="{ data }">
        <Tag :value="data.status" :severity="severityFor(data.status)" />
      </template>
    </Column>
    <Column header="Actions">
      <template #body="{ data }">
        <Button label="View" text @click="router.push(`/patients/${data.id}`)" />
      </template>
    </Column>
  </DataTable>
</template>
```

Key points:
- `usePatients(selectedStatus)` — passing the `ref` makes the query reactive; the dropdown's `v-model` change triggers a refetch
- `DataTable` + `Column` is the PrimeVue counterpart to Angular Material's `mat-table` + `matColumnDef`
- A `Column` with a `#body` slot renders custom cell content (status `Tag`, action `Button`)

### Patient Detail Component

A read-only detail view for one patient. It reads the `:id` route prop, fetches the patient with `usePatient`, and shows the data in a PrimeVue `Card`. A suspend/activate toggle and a delete button use the mutation composables from doc 02; on success they show a toast and (for delete) navigate back to the list. Because the mutations invalidate the cache, the detail view refreshes itself after suspend/activate.

**File: `src/features/patients/PatientDetail.vue`**

```vue
<script setup lang="ts">
import { computed } from 'vue';
import { useRouter } from 'vue-router';
import Card from 'primevue/card';
import Button from 'primevue/button';
import ProgressSpinner from 'primevue/progressspinner';
import {
  usePatient,
  useSuspendPatient,
  useActivatePatient,
  useDeletePatient,
} from '@core/composables/use-patients';
import { useNotifications } from '@core/composables/use-notifications';

const props = defineProps<{ id: string }>();
const router = useRouter();
const notify = useNotifications();

const { data: patient, isPending } = usePatient(() => props.id);

const isSuspended = computed(() => patient.value?.status === 'Suspended');
const isDeleted = computed(() => patient.value?.status === 'Deleted');

const suspend = useSuspendPatient();
const activate = useActivatePatient();
const remove = useDeletePatient();

function toggleStatus() {
  const mutation = isSuspended.value ? activate : suspend;
  mutation.mutate(props.id, {
    onSuccess: (res) => (res.success ? notify.success(res.message) : notify.error(res.message)),
    onError: (err) => notify.error(err.message),
  });
}

function deletePatient() {
  remove.mutate(props.id, {
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
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString();
}
</script>

<template>
  <div v-if="isPending" class="flex justify-center py-12">
    <ProgressSpinner />
  </div>

  <template v-else-if="patient">
    <div class="flex items-center gap-2 mb-4">
      <h1 class="text-2xl font-bold">{{ patient.firstName }} {{ patient.lastName }}</h1>
      <Button
        v-if="!isDeleted"
        icon="pi pi-trash"
        severity="danger"
        text
        rounded
        aria-label="Delete patient"
        :loading="remove.isPending"
        @click="deletePatient"
      />
    </div>

    <Card>
      <template #content>
        <p><strong>Email:</strong> {{ patient.email }}</p>
        <p><strong>Status:</strong> {{ patient.status }}</p>
        <p><strong>Date of birth:</strong> {{ formatDate(patient.dateOfBirth) }}</p>
      </template>
      <template #footer>
        <div class="flex gap-2">
          <Button
            v-if="!isDeleted"
            :label="isSuspended ? 'Activate' : 'Suspend'"
            severity="warn"
            :loading="suspend.isPending || activate.isPending"
            @click="toggleStatus"
          />
          <Button label="Back to list" text @click="router.push('/patients')" />
        </div>
      </template>
    </Card>
  </template>
</template>
```

Key points:
- `defineProps<{ id: string }>()` receives the route param (because the route set `props: true`)
- `usePatient(() => props.id)` passes a getter so the query stays reactive if the id changes
- Suspend/activate/delete use the mutation composables; their `isPending` refs drive PrimeVue's `:loading` button state
- Cache invalidation (doc 02) means the view auto-refreshes after a successful suspend/activate

### Create Patient Component (Overview)

A form for creating a new patient using PrimeVue inputs, **VeeValidate + Zod** for validation, and the `useCreatePatient` mutation. The full implementation — schema, field binding, error display — is covered in [doc 04](./04-vue-forms-and-validation.md). The skeleton:

```vue
<script setup lang="ts">
import { useRouter } from 'vue-router';
import { useCreatePatient } from '@core/composables/use-patients';
import { useNotifications } from '@core/composables/use-notifications';
// + VeeValidate + Zod imports — see doc 04

const router = useRouter();
const notify = useNotifications();
const create = useCreatePatient();
// form setup, validation schema, and submit handler — see doc 04
</script>
```

The submit handler calls `create.mutate(request, { onSuccess, onError })`, shows a toast, and navigates back to `/patients` on success.

---

## Vue vs Angular Component Comparison

| Concept | Angular | Vue 3 |
|---------|---------|-------|
| **Component file** | `.ts` + `.html` + `.scss` | single `.vue` file (template + script + style) |
| **Logic block** | `@Component` class | `<script setup>` |
| **Template syntax** | `@if`, `@for`, `{{ }}`, `[prop]`, `(event)` | `v-if`, `v-for`, `{{ }}`, `:prop`, `@event` |
| **Routing definition** | `Routes` array + `loadComponent` | `routes` array + `() => import()` |
| **Route parameters** | `ActivatedRoute.snapshot.paramMap` | route `props: true` → `defineProps` |
| **Data table** | `mat-table` + `matColumnDef` | `DataTable` + `Column` |
| **Loading indicator** | `<mat-spinner />` | `<ProgressSpinner />` |
| **Status badge** | styled `<span>` | `<Tag :severity>` |
| **Navigation** | `Router.navigate([...])` | `router.push(...)` |
| **Reactivity** | Signals (`signal()`) | `ref()` / `computed()` |
| **DI** | `inject()` | import composables directly |
| **Server state** | manual `subscribe()` + reload | TanStack Query composables |
| **Outlet** | `<router-outlet>` | `<router-view>` |

### Template Syntax Cheat-Sheet

| Feature | Angular | Vue |
|---------|---------|-----|
| Interpolation | `{{ value }}` | `{{ value }}` |
| Conditional | `@if (cond) { }` | `v-if="cond"` |
| Loop | `@for (x of xs; track x.id)` | `v-for="x in xs" :key="x.id"` |
| Event binding | `(click)="fn()"` | `@click="fn()"` |
| Property binding | `[value]="prop"` | `:value="prop"` |
| Two-way binding | `[(ngModel)]="prop"` | `v-model="prop"` |

---

## Verification Checklist

- [ ] Routes configured in `router/index.ts` with lazy `() => import()` components
- [ ] `/patients/create` declared before `/patients/:id`
- [ ] `App.vue` renders a navbar, `<Toast />`, and `<router-view />`
- [ ] Patient List displays patients in a PrimeVue `DataTable`
- [ ] Status `Select` filter reloads data (reactive query key)
- [ ] "Create patient" button navigates to the create form
- [ ] Patient Detail loads by route prop (`/patients/:id`, `props: true`)
- [ ] Delete button (trash icon) appears next to the patient name when not deleted
- [ ] Delete soft-deletes the patient, shows a toast, and navigates to the list
- [ ] Suspend/Activate button toggles based on patient status and shows a toast
- [ ] Loading spinners show during fetches; mutation buttons show `:loading`
- [ ] Navigation works without full page reloads; browser back/forward works

---

## Navigation

- **Previous:** [02-vue-consuming-apis.md](./02-vue-consuming-apis.md)
- **Next:** [04-vue-forms-and-validation.md](./04-vue-forms-and-validation.md)
- **Up:** [Frontend overview](../00-frontend-overview.md)
