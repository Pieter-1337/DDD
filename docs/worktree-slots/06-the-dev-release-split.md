# 06 — The Dev / Release Split

The slot model is a **local-development** concern. None of it should change how the system behaves when deployed. This doc explains exactly where the dev/release boundary sits and how it's enforced.

## What runs where

```
                          dev (worktree, slot ≥ 2)    release (always slot 1)
AppHost orchestration     yes                          n/a — AppHost never deploys
per-slot DB MigrateAsync  yes (Development-gated)       no — pipeline migrates
cookie isolation          yes (Development-gated)       no — returns on slot 1
slot-aware client seed    yes                            seeds canonical URLs (slot 1)
Scheduling/Billing code   none (config-driven)          none
```

Three categories, three different reasons each is safe in release:

1. **AppHost, scripts, `environment.ts`** — dev-only *by construction*. The AppHost is never deployed; the scripts and the Angular dev `environment.ts` are dev-only files. Nothing to gate.
2. **Scheduling & Billing hosts** — *zero* slot code. They read `Auth:Authority`, `Cors:AllowedOrigins`, `Auth:CookieName`, and their connection strings from config; the AppHost injects slot-derived values as env vars in dev. In release nothing is injected, so the `appsettings.json` values govern. No code path to gate.
3. **Identity host** — the *only* deployable binary with in-process slot code (cookie names must be set inside the process). This one needs an explicit gate.

## The gate

```csharp
if (builder.Environment.IsDevelopment())
    builder.AddWorktreeSlotCookieIsolation();
```

Two independent guards make this inert in release:

- **`IsDevelopment()`** — release environments (Staging/Production) never enter the method.
- **`if (slot <= 1) return;`** inside the method — even in dev, slot 1 is a no-op.

Release is always slot 1 (no `worktree-slot` is ever injected outside the AppHost), so both guards agree: the framework default cookie names are used, unchanged. We chose a **runtime gate** over `#if DEBUG` conditional compilation deliberately — it's the idiomatic .NET pattern (it mirrors the existing `Development`-gated `MigrateAsync`), it's testable, and `DEBUG` ≠ `Development` in general, which would surprise.

## Why the cookie wiring is NOT in `BuildingBlocks.WorktreeSlots`

It would seem tidier to push *all* slot logic into the shared building block. We deliberately didn't, and the reason is dependency hygiene.

The slot logic has two kinds:

```
pure mechanics                    cookie wiring
──────────────                    ─────────────
Port, base ports, Resolve,        ConfigureApplicationCookie  (ASP.NET Core)
FromValue, WithSlotDatabase       AddAntiforgery              (ASP.NET Core)
                                  CheckSessionCookieName      (Duende)
no external dependencies          needs web framework + Duende
3 consumers                       1 consumer (Identity host only)
```

Moving the cookie wiring into the shared block would force a project that is referenced by the Aspire AppHost — and is intentionally dependency-free — to take a **web-framework + Duende** dependency, all for a *single* consumer. The other hosts don't own these cookies; there's no second reuser.

So the boundary is: **the building block stays pure and supplies only the slot number; the Identity-specific cookie wiring stays in the Identity host as one dev-gated call.** "Consolidate as much as possible" stops exactly at the dependency line — and that line is what keeps the consolidation clarifying rather than muddying.

→ Continue to [07 — Lifecycle Scripts](07-lifecycle-scripts.md).
