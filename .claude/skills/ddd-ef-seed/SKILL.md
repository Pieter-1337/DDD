---
name: 'ddd-ef-seed'
description: >
  Seed reference or demo data for a bounded context. Two modes: EF Core HasData()
  in an IEntityTypeConfiguration (static reference data baked into a migration), or
  a runtime IHostedService seeder that runs at startup and writes through
  IUnitOfWork / IRepository (dynamic or demo data, must be idempotent). The live
  pattern is IdentitySeedData in Identity.WebApi. Use after ddd-backend-module when
  an aggregate needs baseline rows.
paths: Core/**, WebApplications/**
---

# DDD EF Seed

Two-mode skill. Pick the mode that fits the data:

| Mode | When | Where it runs |
|---|---|---|
| `HasData()` | Reference data: lookups, fixed sets, stable IDs | Baked into a generated migration — once per environment |
| Runtime seeder | Demo/dev fixtures, computed values, anything that may grow | `IHostedService` at startup — runs every boot, must be idempotent |

`IdentitySeedData` (`WebApplications/Identity.WebApi/SeedData/IdentitySeedData.cs`) is the live runtime seeder (roles + Admin/Doctor/Nurse users + IdentityServer config). New bounded-context seeders follow the same idempotent `IHostedService` shape but write domain aggregates through `IUnitOfWork`.

## Mode A — `HasData()` for static reference data

Use when rows are fixed at design time, keyed by stable literal IDs, never changed at runtime.

In the entity's `IEntityTypeConfiguration` (`Core/<Context>/<Context>.Infrastructure/Persistence/Configurations/<Aggregate>Configuration.cs`):

```csharp
public void Configure(EntityTypeBuilder<<Aggregate>> builder)
{
    builder.ToTable("<Aggregate>s");
    builder.HasKey(x => x.Id);
    builder.Ignore(x => x.DomainEvents);
    builder.Property(x => x.Name).IsRequired().HasMaxLength(200);

    builder.HasData(
        new { Id = new Guid("11111111-1111-1111-1111-111111111111"), Name = "Standard" },
        new { Id = new Guid("22222222-2222-2222-2222-222222222222"), Name = "Premium" });
}
```

Then run `ddd-ef-migration` (`dotnet ef migrations add Seed<Aggregate> ...`). The `Up()` will contain `migrationBuilder.InsertData(...)`, `Down()` the matching `DeleteData`. Editing `HasData` later diffs against the prior seed and generates insert/update/delete automatically.

**Limits of HasData:** IDs and values must be literals (no `Guid.NewGuid()`, no `DateTime.UtcNow`); can't depend on non-seeded rows; for entities with private setters use an anonymous object with the shadow/property names (as above) — EF maps it positionally to columns. Awkward for hundreds of rows.

> HasData seeds run only via **migrations**, so they do NOT appear in integration tests (which use SQLite + `EnsureCreated()`). If tests need the data, seed it in-test via `Uow` or use Mode B.

## Mode B — runtime seeder (IHostedService)

Use for demo/dev data or anything HasData can't express. Idempotency is your job — it runs on every startup.

`WebApplications/<Context>.WebApi/SeedData/<Context>SeedData.cs`:

```csharp
using BuildingBlocks.Application.Interfaces;
using <Context>.Domain.<Aggregates>;

namespace <Context>.WebApi.SeedData;

public class <Context>SeedData : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostEnvironment _environment;

    public <Context>SeedData(IServiceProvider serviceProvider, IHostEnvironment environment)
    {
        _serviceProvider = serviceProvider;
        _environment = environment;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            return;

        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = uow.RepositoryFor<<Aggregate>>();

        // Idempotent guard — bail if anything already exists (or check a specific key).
        if (await repo.ExistsAsync(_ => true, cancellationToken))
            return;

        repo.Add(<Aggregate>.Create("Demo One"));
        repo.Add(<Aggregate>.Create("Demo Two"));
        await uow.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

Register it in `WebApplications/<Context>.WebApi/Program.cs`:

```csharp
builder.Services.AddHostedService<<Context>SeedData>();
```

Notes:
- Seed **through `IUnitOfWork`/`IRepository`**, mirroring the rest of the app — not raw `DbContext`. `Create(...)` keeps invariants and raises domain events (which dispatch on `SaveChangesAsync`).
- The Identity seeder additionally calls `db.Database.MigrateAsync()` before seeding because it owns its schema lifecycle. A bounded-context seeder should run **after** the schema exists — apply migrations first (`ddd-ef-migration`), or add a `MigrateAsync()` call at the top of `StartAsync` guarded by `IsRelational()` if you want the host to own it.
- **Idempotency is critical** — guard with `ExistsAsync(...)` (a broad `_ => true`, or a specific natural key like `IdentitySeedData`'s per-email `FindByEmailAsync` check). Never rely on migrations to prevent re-seeding.

## Step 1 — Pick the mode
- Fixed reference data with literal IDs → Mode A.
- Demo/dev data, computed values, or data tests need → Mode B.

## Step 2 — Implement
Follow the section for the chosen mode.

## Step 3 — Mode A only: generate the migration
Run `ddd-ef-migration` (`dotnet ef migrations add Seed<Aggregate> --project Core\<Context>\<Context>.Infrastructure --startup-project WebApplications\<Context>.WebApi --output-dir Persistence\Migrations`). Inspect the `InsertData` calls, commit.

## Step 4 — Verify
Start the WebApi (Mode A: rows arrive when the migration applies; Mode B: the hosted service runs on boot in Development) and confirm via the aggregate's GET endpoint. For integration tests that need baseline data, seed inside the test with `Uow` — HasData seeds don't apply under SQLite/`EnsureCreated()`.
