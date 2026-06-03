---
name: 'ddd-ef-migration'
description: >
  Add an EF Core migration after changing an entity or its IEntityTypeConfiguration
  in a bounded context (Core/<Context>/<Context>.Infrastructure). Migrations target
  SQL Server, are generated per-context with dotnet ef (--project on the
  Infrastructure project, --startup-project on the matching WebApi), live in
  Persistence/Migrations, and are committed (including the ModelSnapshot). Use
  whenever a schema change is needed.
paths: Core/**, WebApplications/**
---

# DDD EF Migration

The source of truth is the C# model (entity + `IEntityTypeConfiguration<T>`). Migrations are generated, not hand-written, and target **SQL Server (LocalDB)**. Each bounded context has its own `DbContext` and its own `Persistence/Migrations/` folder; the connection string comes from user secrets.

## Conventions
- One `DbContext` per context: `SchedulingDbContext` (`Core/Scheduling/Scheduling.Infrastructure/Persistence/`), `BillingDbContext` (`Core/Billing/...`).
- The Infrastructure project holds the model/migrations; the **WebApi is the startup project** (it supplies the connection string + DI). Generate with `--project <Infrastructure>` and `--startup-project <WebApi>`.
- Migration output dir: `Persistence\Migrations` (relative to the Infrastructure project).
- Snapshot `<Context>DbContextModelSnapshot.cs` is committed alongside the migration.
- `dotnet ef` is a **global** tool (there is no `.config/dotnet-tools.json`). Install once: `dotnet tool install --global dotnet-ef`.
- Connection string: user secrets `ConnectionStrings:DefaultConnection` (LocalDB), shared via `UserSecretsId`.
- The DbContext has no `DbSet<T>` — `dotnet ef` discovers entities through `ApplyConfigurationsFromAssembly`, so a new entity just needs its `IEntityTypeConfiguration`.
- **Never edit a migration already applied to any environment.** Generate a new one instead.

## Step 1 — Clarify scope
- Which context's schema changed?
- What changed (new field, index, rename, type change, new entity)?
- A descriptive PascalCase migration name (`AddPatient`, `AddPhoneIndexToPatient`, `RenameXToY`).
- Does existing data need a backfill? If so the generated migration needs a hand-edited `migrationBuilder.Sql(...)`.

## Step 2 — Edit the model

Change the entity in `Core/<Context>/<Context>.Domain/...` and/or its config in `Core/<Context>/<Context>.Infrastructure/Persistence/Configurations/`. Run `dotnet build DDD.sln` first — `dotnet ef` builds the project, so a compile error blocks generation.

## Step 3 — Generate (run from repo root)

**Scheduling:**

```powershell
dotnet ef migrations add <Name> `
  --project Core\Scheduling\Scheduling.Infrastructure `
  --startup-project WebApplications\Scheduling.WebApi `
  --output-dir Persistence\Migrations
```

**Billing:**

```powershell
dotnet ef migrations add <Name> `
  --project Core\Billing\Billing.Infrastructure `
  --startup-project WebApplications\Billing.WebApi `
  --output-dir Persistence\Migrations
```

This creates, in `<Context>.Infrastructure/Persistence/Migrations/`:
- `<timestamp>_<Name>.cs` — `Up()` / `Down()`
- `<timestamp>_<Name>.Designer.cs` — generated, don't edit
- `<Context>DbContextModelSnapshot.cs` — updated snapshot; **commit it**

## Step 4 — Inspect and hand-edit if needed

Read the generated `Up()`. Two things to watch on SQL Server:
1. **Renames / type changes** — EF may emit drop+add (data loss) instead of `RENAME`. For a rename, replace with `migrationBuilder.RenameColumn(...)`.
2. **Backfill** — EF generates DDL only. A new `NOT NULL` column on a table with rows needs a `migrationBuilder.Sql("UPDATE ...")` between the `AddColumn` and any constraint step. Mirror it in `Down()`.

Owned value objects (`OwnsOne`) become extra columns on the same table; SmartEnum `HasConversion(s => s.Name, ...)` becomes an `nvarchar` column — confirm the `maxLength` matches the config.

## Step 5 — Apply locally

Scheduling/Billing don't auto-migrate at WebApi startup yet, so apply explicitly (stop the WebApi first if it holds the DB):

```powershell
dotnet ef database update `
  --project Core\Scheduling\Scheduling.Infrastructure `
  --startup-project WebApplications\Scheduling.WebApi
```

(Swap the Billing paths for Billing.) If you later add a startup `db.Database.Migrate()`, starting the WebApi will apply pending migrations automatically — mirror the Identity host's `MigrateAsync` pattern (see `ddd-ef-seed`).

## Step 6 — Verify

`dotnet build DDD.sln`. Integration tests (`ddd-backend-integration-test`) use SQLite + `EnsureCreated()` and won't exercise the migration itself, but they confirm the model still materializes. To exercise the real schema, run the WebApi against LocalDB.

## Reverting a not-yet-applied migration

If generated but never run anywhere:

```powershell
dotnet ef migrations remove `
  --project Core\Scheduling\Scheduling.Infrastructure `
  --startup-project WebApplications\Scheduling.WebApi
```

If applied locally, first roll back: `dotnet ef database update <PreviousMigrationName> --project ... --startup-project ...`, then remove.

## Multiple DbContexts in one project (Identity)

`WebApplications/Identity.WebApi` hosts three contexts (`IdentityDbContext`, Duende's `ConfigurationDbContext`, `PersistedGrantDbContext`) in one project, so every `dotnet ef` call there **must** add `--context <Name>` (and its own `--output-dir`). Those contexts use `MigrateAsync()` at startup via `IdentitySeedData` and share one database, which is why they use `Migrate()` (not `EnsureCreated()`) — per-context `__EFMigrationsHistory` keeps them independent. Bounded-context migrations (Scheduling/Billing) have a single context each and don't need `--context`.
