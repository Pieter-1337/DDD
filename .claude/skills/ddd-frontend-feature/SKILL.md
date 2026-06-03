---
name: 'ddd-frontend-feature'
description: >
  Scaffold a new feature in the Angular SPA (Frontend/Angular/Scheduling.AngularApp):
  a lazy-loaded list and/or detail standalone component under src/app/features/,
  a typed API service under src/app/core/services/, model interfaces under
  src/app/core/models/, and route entries in app.routes.ts guarded by authGuard.
  Angular 21 standalone components, signals, inject() DI, zoneless change detection,
  Angular Material. Use when adding a new page or section.
paths: Frontend/Angular/**
---

# DDD Frontend Feature (Angular)

Scaffolds a new feature end-to-end in the Angular 21 SPA: model interfaces, an API service, lazy-loaded standalone list/detail components, and guarded routes. Mirror the live `patients` feature (`src/app/features/patients/`, `src/app/core/services/patient-api.ts`).

## Architecture

- **Standalone components** only (no NgModules). Each declares its own `imports` array and uses `changeDetection: ChangeDetectionStrategy.OnPush` (the app is zoneless — `provideZonelessChangeDetection()`).
- **State via signals** (`signal`, `computed`); **DI via `inject()`** (not constructor params), services as `private` fields.
- **Services don't subscribe** — they return `Observable`s; components subscribe.
- Path aliases (tsconfig): `@core/*` → `app/core/*`, `@features/*` → `app/features/*`, `@shared/*` → `app/shared/*`, `@env/*` → `environments/*`.
- Layout: shared/singleton code lives in `core/` (`services/`, `models/`, `guards/`, `interceptors/`, `constants/`, `layout/`); routed feature components live in `features/<feature>/<component>/`; cross-feature DTOs in `shared/models/`.
- Routes are lazy (`loadComponent: () => import(...).then(m => m.Component)`) in `src/app/app.routes.ts`, guarded with `canActivate: [authGuard]`.
- Auth is cookie/BFF — the SPA never sees tokens; `authInterceptor` adds `withCredentials` + `X-Requested-With` and redirects on 401. New services just call the API; the interceptor handles auth.

## Step 1 — Clarify scope
- Feature name (kebab-case for files/folders, e.g. `appointments`).
- List view, detail view, or both?
- Which backend endpoint(s) and which WebApi (`schedulingApiUrl` / `billingApiUrl` in `@env/environment`)?
- Any role-gated actions (Admin/Doctor/Nurse)?
- Need a create/edit form? If yes, run `ddd-frontend-form` after scaffolding.

## Step 2 — Model interfaces

`src/app/core/models/<feature>.model.ts`:

```typescript
import { SuccessOrFailureResponse } from '@shared/models/success-or-failure-response.model';

export interface <Feature> {
  id: string;
  name: string;
  status: string; // e.g. "Active" | "Suspended"
}

export interface Create<Feature>Request {
  name: string;
  status: string;
}

export interface Create<Feature>Response extends SuccessOrFailureResponse {
  <feature>Id: string;
}

export interface <Feature>FilterParams {
  status?: string;
}
```

`SuccessOrFailureResponse` (`@shared/models`) maps to the backend `SuccessOrFailureDto` (`{ success, message }`).

## Step 3 — API service

`src/app/core/services/<feature>-api.ts`:

```typescript
import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '@env/environment';
import { Observable } from 'rxjs';
import { SuccessOrFailureResponse } from '@shared/models/success-or-failure-response.model';
import { <Feature>, Create<Feature>Request, Create<Feature>Response, <Feature>FilterParams } from '@core/models/<feature>.model';

@Injectable({ providedIn: 'root' })
export class <Feature>Api {
  private http = inject(HttpClient);
  private baseUrl = `${environment.schedulingApiUrl}/api/<feature>s`;

  getAll(params?: <Feature>FilterParams): Observable<<Feature>[]> {
    let httpParams = new HttpParams();
    if (params?.status) httpParams = httpParams.set('status', params.status);
    return this.http.get<<Feature>[]>(this.baseUrl, { params: httpParams });
  }

  getById(id: string): Observable<<Feature>> {
    return this.http.get<<Feature>>(`${this.baseUrl}/${id}`);
  }

  create(request: Create<Feature>Request): Observable<Create<Feature>Response> {
    return this.http.post<Create<Feature>Response>(this.baseUrl, request);
  }

  suspend(id: string): Observable<SuccessOrFailureResponse> {
    return this.http.post<SuccessOrFailureResponse>(`${this.baseUrl}/${id}/suspend`, null);
  }

  delete(id: string): Observable<SuccessOrFailureResponse> {
    return this.http.delete<SuccessOrFailureResponse>(`${this.baseUrl}/${id}`);
  }
}
```

Use `billingApiUrl` instead of `schedulingApiUrl` for a Billing-context feature.

## Step 4 — List component

`src/app/features/<feature>/<feature>-list/<feature>-list.ts`:

```typescript
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { <Feature> } from '@core/models/<feature>.model';
import { <Feature>Api } from '@core/services/<feature>-api';

@Component({
  selector: 'app-<feature>-list',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './<feature>-list.html',
  styleUrl: './<feature>-list.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class <Feature>List implements OnInit {
  private service = inject(<Feature>Api);
  router = inject(Router);

  items = signal<<Feature>[]>([]);
  isLoading = signal<boolean>(true);
  displayedColumns = ['name', 'status', 'actions'];

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.service.getAll().subscribe({
      next: (items) => { this.items.set(items); this.isLoading.set(false); },
      error: () => this.isLoading.set(false),
    });
  }
}
```

`src/app/features/<feature>/<feature>-list/<feature>-list.html`:

```html
@if (isLoading()) {
  <div class="spinner-wrapper"><mat-spinner diameter="40" /></div>
} @else {
  <div class="list-header">
    <h1><Feature>s</h1>
    <button mat-flat-button color="primary" (click)="router.navigate(['/<feature>s/create'])">New</button>
  </div>
  <table mat-table [dataSource]="items()">
    <ng-container matColumnDef="name">
      <th mat-header-cell *matHeaderCellDef>Name</th>
      <td mat-cell *matCellDef="let row">{{ row.name }}</td>
    </ng-container>
    <ng-container matColumnDef="status">
      <th mat-header-cell *matHeaderCellDef>Status</th>
      <td mat-cell *matCellDef="let row">{{ row.status }}</td>
    </ng-container>
    <ng-container matColumnDef="actions">
      <th mat-header-cell *matHeaderCellDef></th>
      <td mat-cell *matCellDef="let row">
        <button mat-button (click)="router.navigate(['/<feature>s', row.id])">View</button>
      </td>
    </ng-container>
    <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
    <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
  </table>
}
```

(`@if`/`@for` are the built-in control-flow blocks; signals are read by calling them — `items()`.)

## Step 5 — Detail component (with role-gated actions)

`src/app/features/<feature>/<feature>-detail/<feature>-detail.ts`:

```typescript
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { <Feature> } from '@core/models/<feature>.model';
import { <Feature>Api } from '@core/services/<feature>-api';
import { AuthService } from '@core/services/auth';
import { NotificationService } from '@core/services/notification';
import { AppRoles } from '@core/constants/approles';

@Component({
  selector: 'app-<feature>-detail',
  standalone: true,
  imports: [MatCardModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './<feature>-detail.html',
  styleUrl: './<feature>-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class <Feature>Detail implements OnInit {
  private service = inject(<Feature>Api);
  private route = inject(ActivatedRoute);
  private auth = inject(AuthService);
  private notification = inject(NotificationService);
  router = inject(Router);

  item = signal<<Feature> | null>(null);
  isLoading = signal<boolean>(false);

  canDelete = computed(() => this.auth.hasRole(AppRoles.Admin));
  canSuspend = computed(() => this.auth.hasRole(AppRoles.Doctor) || this.auth.hasRole(AppRoles.Admin));

  ngOnInit(): void {
    this.load(this.route.snapshot.paramMap.get('id')!);
  }

  private load(id: string): void {
    this.isLoading.set(true);
    this.service.getById(id).subscribe({
      next: (item) => { this.item.set(item); this.isLoading.set(false); },
      error: () => this.isLoading.set(false),
    });
  }

  delete(): void {
    const id = this.item()!.id;
    this.service.delete(id).subscribe({
      next: (r) => {
        if (r.success) { this.notification.success(r.message); this.router.navigate(['/<feature>s']); }
        else this.notification.error(r.message);
      },
    });
  }
}
```

`src/app/features/<feature>/<feature>-detail/<feature>-detail.html`:

```html
@if (isLoading()) {
  <mat-spinner />
} @else if (item(); as f) {
  <div class="detail-header">
    <h1>{{ f.name }}</h1>
    @if (canDelete()) {
      <button mat-icon-button color="warn" (click)="delete()" aria-label="Delete"><mat-icon>delete</mat-icon></button>
    }
  </div>
  <mat-card>
    <mat-card-content>
      <p><strong>Status:</strong> {{ f.status }}</p>
    </mat-card-content>
    <mat-card-actions>
      <button mat-button (click)="router.navigate(['/<feature>s'])">Back to list</button>
    </mat-card-actions>
  </mat-card>
}
```

Role gating: `AuthService.hasRole(role)` + `computed()` signals drive `@if` blocks. Role constants are in `@core/constants/approles` (`AppRoles.Admin`, `.Doctor`). UI gating is convenience only — the API's `UserValidator` is the real enforcement.

## Step 6 — Register routes

Add to `src/app/app.routes.ts` (import `authGuard` from `@core/guards/auth.guard`):

```typescript
{
  path: '<feature>s',
  loadComponent: () => import('./features/<feature>/<feature>-list/<feature>-list').then(m => m.<Feature>List),
  canActivate: [authGuard],
},
{
  path: '<feature>s/create',
  loadComponent: () => import('./features/<feature>/create-<feature>/create-<feature>').then(m => m.Create<Feature>),
  canActivate: [authGuard],
},
{
  path: '<feature>s/:id',
  loadComponent: () => import('./features/<feature>/<feature>-detail/<feature>-detail').then(m => m.<Feature>Detail),
  canActivate: [authGuard],
},
```

Order matters — put the static `create` path **before** the `:id` param route. For role-restricted pages use the parameterized `roleGuard('Admin')` factory instead of `authGuard`.

## Step 7 — Navigation

Add a link in the navbar (`src/app/core/layout/`). Ask where if unsure.

## Step 8 — Verify

```
cd Frontend\Angular\Scheduling.AngularApp
npm run build
```

Fix template/type errors. If the feature needs a form, run `ddd-frontend-form`. Exercise the golden path with `npm start` (needs the dev SSL certs — see the app's setup docs).
