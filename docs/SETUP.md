# Fresh-Machine Setup

End-to-end checklist for getting the solution running on a new Windows dev machine.

User secrets and LocalDB databases are **per-Windows-user** and never checked in — so every machine starts empty. Follow these steps in order.

---

## Prerequisites

- .NET 9 SDK
- SQL Server LocalDB (ships with SQL Server Express / Visual Studio)
- Node.js (for the Angular frontend)
- `dotnet-ef` global tool:
  ```powershell
  dotnet tool install --global dotnet-ef
  ```

---

## 1. Connection strings (user secrets)

> **Shared secret store.** `Aspire.AppHost`, `Scheduling.WebApi`, `Billing.WebApi`, and `Identity.WebApi` all declare the same `<UserSecretsId>` in their `.csproj` files, so they read from one secrets file at `%APPDATA%\Microsoft\UserSecrets\12d3119a-ea1f-43ad-b1f3-6c5072eb7dcd\secrets.json`. Each key only needs to be set **once** — picking any of the four `--project` values works.

Two databases are used:

| Database     | Used by                                | Secret key                          |
| ------------ | -------------------------------------- | ----------------------------------- |
| `DDD`        | Scheduling.WebApi, Billing.WebApi      | `ConnectionStrings:DefaultConnection` |
| `IdentityDb` | Identity.WebApi (separate bounded context) | `ConnectionStrings:IdentityDb`      |

Set them:

```powershell
$ddd = "Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=DDD;Integrated Security=true;TrustServerCertificate=True"
$idp = "Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=IdentityDb;Integrated Security=true;TrustServerCertificate=True"

dotnet user-secrets set "ConnectionStrings:DefaultConnection" $ddd --project WebApplications\Scheduling.WebApi
dotnet user-secrets set "ConnectionStrings:IdentityDb"        $idp --project WebApplications\Scheduling.WebApi
```

**Do not add `MultipleActiveResultSets=true`** — see [phase-2-ef-core/03-database-migrations.md](phase-2-ef-core/03-database-migrations.md) for why.

---

## 2. Start LocalDB

```powershell
sqllocaldb start MSSQLLocalDB
```

---

## 3. Apply migrations

Each DbContext gets its own `__EFMigrationsHistory` table — safe to share one database (the `DDD` case) or use one per context (the `IdentityDb` case).

```powershell
# DDD database — Scheduling + Billing bounded contexts
dotnet ef database update --project Core\Scheduling\Scheduling.Infrastructure --startup-project WebApplications\Scheduling.WebApi
dotnet ef database update --project Core\Billing\Billing.Infrastructure       --startup-project WebApplications\Billing.WebApi

# IdentityDb — three contexts share one DB, so each needs --context
dotnet ef database update --context IdentityDbContext        --project WebApplications\Identity.WebApi --startup-project WebApplications\Identity.WebApi
dotnet ef database update --context ConfigurationDbContext   --project WebApplications\Identity.WebApi --startup-project WebApplications\Identity.WebApi
dotnet ef database update --context PersistedGrantDbContext  --project WebApplications\Identity.WebApi --startup-project WebApplications\Identity.WebApi
```

> **Why three commands for Identity.WebApi?** Three DbContexts (`IdentityDbContext`, `ConfigurationDbContext`, `PersistedGrantDbContext`) live in the project. `dotnet ef` requires `--context` whenever more than one is present.

> **Why migrations, not `EnsureCreatedAsync`?** When several DbContexts share one database, `EnsureCreatedAsync` only builds schema for the first context that runs — subsequent calls see "DB exists" and exit without creating their tables. `MigrateAsync` keeps a per-context history and is idempotent. `IdentitySeedData` calls `MigrateAsync` on startup, so the explicit `database update` above is only required when you want the schema in place **before** the app runs (e.g. CI, smoke tests).

---

## 4. Shared Data Protection keys folder

Scheduling.WebApi and Billing.WebApi share cookie encryption keys via the filesystem path configured in `appsettings.json` (`Auth:SharedKeysPath`). Default: `C:\SharedKeys\DDD`.

```powershell
New-Item -ItemType Directory -Force C:\SharedKeys\DDD
```

---

## 5. RabbitMQ

Managed by .NET Aspire — running `Aspire.AppHost` starts a RabbitMQ container automatically. No manual setup needed.

The `docker-compose.yml` at the repo root is kept as a fallback for CI/CD or running without Aspire.

---

## 6. Install Angular dependencies

```powershell
npm ci --prefix Frontend\Angular\Scheduling.AngularApp
```

`npm ci` (not `npm install`) reproduces exactly what's in `package-lock.json`. Skip this and Aspire will fail to launch the SPA with errors like `ERR_MODULE_NOT_FOUND: Cannot find module '...\node_modules\cliui\index.mjs'`.

---

## 7. Run

```powershell
dotnet run --project Aspire.AppHost
```

Aspire opens its dashboard and launches Identity, Scheduling, Billing, the Angular SPA, and RabbitMQ.

On first startup, `IdentitySeedData` runs and seeds the test users:

| Email             | Password    | Role   |
| ----------------- | ----------- | ------ |
| `admin@test.com`  | `Admin123!` | Admin  |
| `doctor@test.com` | `Doctor123!`| Doctor |
| `nurse@test.com`  | `Nurse123!` | Nurse  |

It also seeds Duende clients, API scopes, and identity resources into `IdentityDb`.

---

## Troubleshooting

**"Invalid object name 'IdentityResources'" (or any Duende table) on startup**
Migrations weren't applied. Re-run step 3 for the Identity contexts.

**"Connection string 'DefaultConnection' not found." (or 'IdentityDb')**
The shared user-secrets file is missing the key. Re-run step 1. Tip: `dotnet user-secrets list --project WebApplications\Scheduling.WebApi` shows all current values.

**"Login failed for user" or "Cannot open database"**
LocalDB instance isn't running. Run `sqllocaldb start MSSQLLocalDB`.

**Cookie decryption fails after restart, or APIs can't share auth**
The `C:\SharedKeys\DDD` folder is missing or not writable. Re-run step 4.

**`ERR_MODULE_NOT_FOUND: Cannot find module '...\node_modules\cliui\index.mjs'` when Aspire starts the Angular app**
The Angular `node_modules` is missing or partially installed. Re-run step 6 (`npm ci --prefix Frontend\Angular\Scheduling.AngularApp`).
