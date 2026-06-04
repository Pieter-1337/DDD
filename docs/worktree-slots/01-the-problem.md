# 01 — The Problem: One Checkout Couldn't Run Twice

## The goal

We want to run the *whole* system live from more than one git worktree at the same time — the main checkout plus one or more parallel worktrees (a human experimenting in one, an autonomous agent building a slice in another). "Live" means the full Aspire stack: Identity, Scheduling, Billing, the Angular SPA, RabbitMQ, and the databases.

The trouble is that a second worktree is a *byte-for-byte copy of the same configuration*. Boot it next to the first and everything that is pinned to a fixed name or number collides.

## The four collisions

```
        main checkout (slot 1)            second worktree
        ----------------------            ---------------
ports   Identity   https://:7010   <-->   https://:7010   ❌ port in use
        Scheduling https://:7001   <-->   https://:7001   ❌
        Billing    https://:7002   <-->   https://:7002   ❌
        Angular    https://:7003   <-->   https://:7003   ❌
        Aspire dashboard / OTLP     <-->   same ports      ❌

db      DDD, IdentityDb             <-->   DDD, IdentityDb  ❌ same LocalDB databases

broker  RabbitMQ data volume        <-->   same volume      ❌ deterministic name

auth    DDD.Auth cookie             <-->   DDD.Auth cookie  ❌ host-scoped, one profile
```

1. **Ports.** Both worktrees host their APIs and the Aspire dashboard on the same well-known localhost ports. The second one to start fails to bind.
2. **Databases.** All projects share a single `UserSecretsId`, so both worktrees point EF Core at the same `DDD` and `IdentityDb` on `(localdb)\MSSQLLocalDB`. Data from one leaks into the other.
3. **Broker.** Aspire/DCP gives RabbitMQ's Docker *container* a random run-suffix, so the containers don't clash — but its **data volume** name is deterministic, so two instances fight over the same volume.
4. **Auth cookies.** Browser cookies are scoped by **host** (`localhost`), not by port. Even on different ports, two instances in one browser profile read and overwrite each other's `DDD.Auth` cookie — logging into one logs you out of the other.

## Why "just change the config" isn't the answer

You *could* hand-edit a second worktree's ports, connection strings, authority URL, and CORS origins. But those are four sets of interdependent values that must stay mutually consistent — change a port and you must change the matching redirect URI, the CORS allow-list, and the authority the SPA points at, or auth silently breaks. Doing that by hand per worktree is error-prone, and the edits dirty the git tree (risking accidental commits of machine-specific values).

We want **one knob**, not forty. The next doc introduces it: the slot.

→ Continue to [02 — The Slot Model](02-the-slot-model.md).
