# 04 — AppHost Orchestration

The Aspire AppHost is where the slot integer turns into a running, isolated stack. The AppHost is a **local-dev-only orchestrator — it is never deployed**, so all of its slot logic is inherently out of the release path.

## Resolve the slot before `CreateBuilder`

```csharp
var projectDir = FindProjectDirectory(AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
var slot = WorktreeSlot.Resolve(projectDir);

if (slot > 1)
    OffsetDashboardPorts(slot);   // ASPIRE_DASHBOARD_* env vars

var builder = DistributedApplication.CreateBuilder(args);
```

The slot is read **at build time** because `WithHttpsEndpoint(port: …)` needs a literal `int` while the resource graph is being constructed. An Aspire `AddParameter` would only materialise at *run* time — too late to feed port arithmetic (the same constraint that governs the broker choice in ADR-0001).

The dashboard/OTLP/resource-service ports are offset by setting `ASPIRE_DASHBOARD_*` and `ASPNETCORE_URLS` env vars *before* `CreateBuilder`, because environment variables beat `launchSettings.json` (which is git-tracked and can't be per-worktree).

## Derive endpoint ports from the shared formula

```csharp
var identityApi = builder.AddProject<Projects.Identity_WebApi>("identity-webapi")
    .WithHttpsEndpoint(port: WorktreeSlot.Port(WorktreeSlot.IdentityBasePort, slot), name: "identity-https");

var schedulingApi = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-webapi")
    .WithHttpsEndpoint(port: WorktreeSlot.Port(WorktreeSlot.SchedulingBasePort, slot), name: "scheduling-https")
    ...
```

Note the named constants (`WorktreeSlot.IdentityBasePort`) rather than bare `7010` literals — the AppHost and `IdentityServerConfig` now read the same base-port constants from the shared building block, so they cannot drift.

## Inject slot-derived config for slots ≥ 2 (and nothing for slot 1)

```csharp
if (slot > 1)
{
    var authority   = $"https://localhost:{WorktreeSlot.Port(WorktreeSlot.IdentityBasePort, slot)}";
    var cookieName  = $"DDD.Auth.S{slot}";
    var spaOrigin   = $"https://localhost:{WorktreeSlot.Port(WorktreeSlot.SpaBasePort, slot)}";

    identityApi.WithEnvironment("worktree-slot", slot.ToString());

    schedulingApi
        .WithEnvironment("Auth__Authority", authority)
        .WithEnvironment("Auth__CookieName", cookieName)
        .WithEnvironment("Cors__AllowedOrigins__0", spaOrigin)
        .WithEnvironment("Cors__AllowedOrigins__1", authority);
    // …same for billingApi…

    var defaultSlotted  = WorktreeSlot.WithSlotDatabase(defaultConnectionBase, slot);
    var identitySlotted = WorktreeSlot.WithSlotDatabase(identityDbBase, slot);
    schedulingApi.WithEnvironment("ConnectionStrings__DefaultConnection", defaultSlotted);
    billingApi.WithEnvironment("ConnectionStrings__DefaultConnection", defaultSlotted);
    identityApi.WithEnvironment("ConnectionStrings__IdentityDb", identitySlotted);
}
```

Two things make this safe:

- **The whole block is gated `if (slot > 1)`** — slot 1 injects nothing, so the `appsettings.json` literals govern and behaviour is byte-for-byte identical to before the slot model.
- **Injections use env-var form** (`__` separator), which beats `appsettings`/user-secrets in .NET's config precedence. So the Scheduling/Billing hosts need *no slot code at all* — they just read config, and the AppHost shadows the right values. The only host with in-process slot code is Identity (cookies; see [05](05-auth-isolation.md)).

## Per-slot databases: booting the slot is the DB init

There is no EF step in the init script. Each service runs a `Development`-guarded `MigrateAsync` hosted service at startup, so the slot's databases materialise and migrate on first boot:

```
boot slot 2  →  MigrateAsync against DDD_S2 / IdentityDb_S2  →  fresh, migrated, seeded
```

The `Development` guard keeps dev/staging/prod migrating via the deployment pipeline, never at startup.

## Broker isolation

RabbitMQ keeps its durable `.WithDataVolume()` only on slot 1; slots 2–5 run ephemeral (scratch worktrees need no broker durability). Container *names* already get a random run-suffix from Aspire/DCP, so the only deterministic collision risk was the volume — and that's removed for slots ≥ 2.

→ Continue to [05 — Auth Isolation](05-auth-isolation.md).
