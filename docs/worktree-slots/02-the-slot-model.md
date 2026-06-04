# 02 — The Slot Model: One Integer Derives Everything

## The idea

Each worktree declares a single integer — its **slot** — in the range 1–5. Every per-instance value is *derived* from that integer instead of being configured by hand:

```
value(base) = base + 100 * (slot - 1)
```

So a base port of 7001 becomes:

```
slot 1 → 7001 + 100*0 = 7001     (main checkout — unchanged)
slot 2 → 7001 + 100*1 = 7101
slot 3 → 7001 + 100*2 = 7201
slot 4 → 7001 + 100*3 = 7301
slot 5 → 7001 + 100*4 = 7401
```

The four browser-facing bases:

```
            base    slot 1   slot 2   slot 3
Identity    7010    7010     7110     7210
Scheduling  7001    7001     7101     7201
Billing     7002    7002     7102     7202
Angular SPA 7003    7003     7103     7203
```

Databases and cookies follow the same per-slot scheme: `DDD` → `DDD_S2`, `IdentityDb` → `IdentityDb_S2`, `DDD.Auth` → `DDD.Auth.S2`, and so on.

## The load-bearing rule: slot 1 is "today"

**Slot 1 is the main checkout and reproduces today's behaviour byte-for-byte.** The offset for slot 1 is `100 * (1 - 1) = 0`, so every derived value equals its base. No env vars are injected, no volume changes, no cookie renaming. This is not a happy accident — it is a guarantee the whole design is built to preserve, because it means:

- A normal `dotnet run` / F5 in the main checkout behaves exactly as before the slot model existed.
- The **release path** (dev/staging/prod, which are always slot 1) is unaffected.
- Regression tests assert the slot-1 outputs explicitly (e.g. `Clients_Slot1_UsesCanonicalPorts`).

## Where the slot value comes from

The integer lives in a gitignored file, `Aspire.AppHost/.worktree-slot`, containing just the number. It's gitignored *because* its value must differ per worktree — committing it would force the same value everywhere (collisions return) or churn history. The tooling that reads and manages it is committed; only the value is ignored.

Resolution order (highest wins):

```
1. worktree-slot environment variable        (CI / launch scripts)
2. first line of Aspire.AppHost/.worktree-slot file
3. default: 1
```

A non-empty value outside 1–5 **fails fast** — a misconfigured slot stops the boot rather than silently running on the wrong ports.

## Why a cap of 5

The cap (main + 4 worktrees) is purely pragmatic — a RAM ceiling, since each live slot is an API trio plus a broker (and on the Azure Service Bus path, two containers per slot). It is not an architectural limit: ports are just a formula and the auth layer is slot-aware (see [05](05-auth-isolation.md)). Raising it later is bumping a constant.

→ Continue to [03 — Shared Mechanics](03-shared-mechanics.md).
