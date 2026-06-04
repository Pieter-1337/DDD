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

## 5. Message broker (RabbitMQ by default)

**RabbitMQ is the default broker.** Managed by .NET Aspire — running `Aspire.AppHost` starts a RabbitMQ container automatically. No manual setup needed.

The `docker-compose.yml` at the repo root is kept as a fallback for CI/CD or running without Aspire.

### Selecting broker & framework locally — two AppHost knobs

Under the AppHost, **two parameters are the single source of truth** for local dev, and each
is **fanned out to both services** so they cannot drift:

| Knob (AppHost config) | Default | Controls | Fanned to services as |
| --- | --- | --- | --- |
| `Parameters:messaging-broker` (or env `ASPIRE_MESSAGING_BROKER`) | `RabbitMq` | which container the AppHost provisions **and** each service's transport | `MessageBroker` |
| `Parameters:messaging-framework` (or env `ASPIRE_MESSAGING_FRAMEWORK`) | `MassTransit` | which messaging library each service runs | `MessagingFramework` |

Set a knob on the **AppHost only** — the AppHost injects the resolved value into both
`Scheduling.WebApi` and `Billing.WebApi` via `WithEnvironment(...)`, so flipping one value moves
the provisioned broker container *and* both services together. You no longer set `MessageBroker`
or `MessagingFramework` per service for a local AppHost run.

> **Other deployments are unchanged.** The env injection happens **only when the AppHost launches
> the services**. Production (and any non-AppHost run) starts each service from its own config
> files / user-secrets, so `MessageBroker` and `MessagingFramework` remain **per-service config**
> there — the AppHost is just a local-dev config *source* supplying an aligned value, not a second
> production mechanism (consistent with [ADR-0001](adr/0001-message-broker-selection.md) and
> [ADR-0003](adr/0003-native-wolverine-flow-and-framework-alignment.md)).

**Which combinations work, fail, or are not wired** is recorded in the **[supported framework ×
broker matrix](phase-5-event-driven/09-broker-framework-matrix.md)** — read it before switching.

```powershell
# Native Wolverine→Wolverine on RabbitMQ (broker stays at the RabbitMq default):
dotnet user-secrets set "Parameters:messaging-framework" Wolverine --project Aspire.AppHost
#   (or: $env:ASPIRE_MESSAGING_FRAMEWORK = "Wolverine" before `dotnet run`)

# Azure Service Bus emulator (MT→MT — keep the MassTransit default; the MT→W interop bridge is
# RabbitMQ-only). ONE knob now — the AppHost fans MessageBroker out to both services:
dotnet user-secrets set "Parameters:messaging-broker" AzureServiceBus --project Aspire.AppHost
```

The ASB emulator has **no management UI** (and is incompatible with the community Service Bus Explorer tools). Message-flow observability is the **Aspire dashboard's OpenTelemetry traces**.

To switch back to defaults, remove the AppHost secret(s) (or set `Parameters:messaging-broker` back to `RabbitMq` / `Parameters:messaging-framework` back to `MassTransit`).

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

## Working in a git worktree (e.g. agent worktrees)

Background agents and `claude --worktree` create isolated checkouts under `.claude/worktrees/`. Most of the setup above **carries over for free** because it lives outside the repo and is shared per-Windows-user: user secrets, the LocalDB databases, and the `C:\SharedKeys\DDD` Data Protection keys are all visible from any worktree without re-running steps 1, 3, or 4.

Two things do **not** carry over into a fresh worktree:

- **Angular `node_modules`** — not copied (it's large, and per-worktree copies are slow and cause Windows path-length grief). Before running or building the SPA from a worktree, install dependencies in that worktree:
  ```powershell
  npm ci --prefix <worktree-path>\Frontend\Angular\Scheduling.AngularApp
  ```
  On Windows, first enable long paths (see [Windows long paths](#windows-long-paths-required-for-the-angular-spa-in-worktrees) below) — otherwise this install silently drops the most deeply-nested files. Note: booting the slot via Aspire (`dotnet run --project Aspire.AppHost`) also runs `npm install` for you, so an explicit `npm ci` is only needed to prep the SPA *before* launching.
- **Local mkcert certificates** — seeded **only when Claude Code creates the worktree**. The repo-root `.worktreeinclude` lists `Frontend/Angular/Scheduling.AngularApp/certs/local-cert.pem` and `local-key.pem`, and the harness copies them into worktrees it creates. This is keyed to the harness's creation step, **not** the directory — a worktree made with a plain `git worktree add` (even one placed under `.claude/worktrees/`) does **not** get them. For those, regenerate the certs per `Frontend\Angular\Scheduling.AngularApp\certs\README.md`, or just create the worktree via Claude.

### Creating a worktree

`.worktreeinclude` (and the cert seeding above) only fires when **Claude Code** creates the worktree, so prefer that:

| You want | Use | Lifetime |
| --- | --- | --- |
| A worktree you boot and reuse | `claude --worktree <name>` → `.claude/worktrees/<name>` (keep it when prompted) | Persists |
| An agent to work in isolation | spawn with `isolation: worktree` (e.g. `/app-do-prd`) | Ephemeral — auto-cleaned when the agent finishes without changes |
| Full manual control | `git worktree add <path>` | You manage cleanup — and must seed the certs yourself |

### Windows long paths (required for the Angular SPA in worktrees)

A worktree path like `.claude\worktrees\<name>\` plus Angular's deeply-nested `node_modules\@angular\material\…` can exceed Windows' 260-char `MAX_PATH`. When it does, **npm silently fails to write the deepest files** — typically some `@angular/material` `.d.ts` declarations — and the SPA build then fails with `TS7016: Could not find a declaration file for module '@angular/material/toolbar'` (plus a cascading `NG1010`). The package versions look correct; the install is just incomplete.

Enable long-path support once so installs under deep worktree paths complete:

```powershell
git config --global core.longpaths true                       # git half (no admin)
# OS half — run in an ELEVATED PowerShell (Run as Administrator):
Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' -Name LongPathsEnabled -Value 1 -Type DWord
```

`LongPathsEnabled` only applies to newly-started processes, so reopen your terminal (and restart Aspire) afterwards. If a worktree was installed *before* enabling it, reinstall cleanly so the missing files get written:

```powershell
$spa = '<worktree-path>\Frontend\Angular\Scheduling.AngularApp'
Remove-Item "$spa\node_modules" -Recurse -Force
npm --prefix $spa ci
```

Verify by comparing the `@angular/material` file count against the main checkout. If you'd rather not enable long paths, create the worktree at a short path instead (e.g. `git worktree add C:\w\<name>`).

### Running more than one instance at once (worktree slots)

The AppHost derives every port from a single **slot** integer (1–5), so multiple checkouts can run live side by side without colliding. Slot 1 is the main checkout (unchanged byte-for-byte); slots 2–5 offset every port by `+100 × (slot − 1)` and get their own `DDD_S{N}` / `IdentityDb_S{N}` databases. Full design: **`docs/adr/0002-worktree-slots.md`**.

From inside a new worktree, claim a slot, then boot:

```powershell
.\scripts\worktree-init.ps1            # claims the lowest free slot 2-5, writes Aspire.AppHost/.worktree-slot
dotnet run --project Aspire.AppHost    # binds the slot's offset ports + creates/migrates its databases on boot
```

When you're done with the slot, release it (drops its databases and frees the slot; refuses on slot 1):

```powershell
.\scripts\worktree-destroy.ps1
```

`worktree-init.ps1` must be run from inside the worktree (not the main checkout — it refuses there) and serializes slot claims with a lockfile in the git common dir.

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
