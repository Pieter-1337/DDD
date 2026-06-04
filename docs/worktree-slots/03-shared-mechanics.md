# 03 — Shared Mechanics: `BuildingBlocks.WorktreeSlots`

## Why a shared building block

The slot formula and the four base ports are needed in three different places:

- the **AppHost**, to assign endpoint ports and inject slot-derived config;
- **`IdentityServerConfig`**, to seed each slot's own redirect / CORS URLs;
- **host startup**, to resolve which slot this process is.

An earlier iteration duplicated the arithmetic and the base-port constants across these sites — one copy even carried a `// must stay in sync with AppHost.cs` comment, which is a smell: a comment is not a compiler. We consolidated the mechanics into a single project, **`BuildingBlocks.WorktreeSlots`**, so the formula and the base ports have exactly one home, unit-tested once.

```
BuildingBlocks.WorktreeSlots          (dependency-free)
        ▲                     ▲
        │                     │
   Aspire.AppHost        Identity.WebApi
   (orchestration)       (dev-only cookie isolation + client seeding)
```

## Dependency-free on purpose

The building block references **no web framework, no Duende, no configuration package** — only in-framework `System.Data.Common`. That purity is what lets *both* an Aspire host and an ASP.NET Core host reference it without inheriting unwanted packages. (The one thing that genuinely needs the web framework — cookie naming — stays in the Identity host; see [06](06-the-dev-release-split.md).)

## The API

```csharp
public static class WorktreeSlot
{
    public const int Min = 1;
    public const int Max = 5;

    public const int IdentityBasePort   = 7010;
    public const int SchedulingBasePort  = 7001;
    public const int BillingBasePort     = 7002;
    public const int SpaBasePort         = 7003;

    // base + 100 * (slot - 1)
    public static int Port(int basePort, int slot);

    // AppHost path: worktree-slot env var → .worktree-slot file → default 1
    public static int Resolve(string appHostDirectory);

    // Web-host path: parse one injected config value (null/blank → 1)
    public static int FromValue(string? rawValue);

    // Rewrite the Initial Catalog token: DDD → DDD_S{slot}
    public static string WithSlotDatabase(string connectionString, int slot);
}
```

## Two resolution entry points, one parser

There are two ways a process learns its slot, so there are two entry points — but they share one fail-fast parser:

| Consumer | Method | Source |
|----------|--------|--------|
| AppHost | `Resolve(dir)` | `worktree-slot` env var, then the `.worktree-slot` file |
| Web host (Identity) | `FromValue(config["worktree-slot"])` | a single config value the AppHost injected as an env var |

The web host can't use `Resolve` — it has no `.worktree-slot` file of its own; it receives the slot as an injected environment variable that shows up in `IConfiguration`. `FromValue` parses that single string with the **same range guard**, which also hardened the old inline `int.TryParse(...)` the Identity host used to do — that swallowed out-of-range values silently; `FromValue("99")` now throws.

## `WithSlotDatabase`

Rather than string-hacking the connection string, this parses it with `DbConnectionStringBuilder` (in-framework, case-insensitive) and rewrites only the `Initial Catalog` token:

```
Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=DDD;...
                                   →  Initial Catalog=DDD_S2
```

It throws if there is no `Initial Catalog` token — a missing catalog would silently connect to the wrong database, defeating the isolation that is the entire point.

→ Continue to [04 — AppHost Orchestration](04-apphost-orchestration.md).
