# 07 — Lifecycle Scripts & the Orchestrator Semaphore

A slot has a lifecycle: claim it when a worktree starts, release it when the worktree is torn down. Two PowerShell scripts bracket that lifecycle, and the autonomous orchestrator layers a semaphore on top.

## `worktree-init.ps1` — claim the lowest free slot

Run once inside a new worktree. It:

1. Refuses to run in the **main checkout** (implicit slot 1) — the main checkout must never carry a `.worktree-slot` file, or it would silently rebind to another slot's ports/DBs.
2. Scans every worktree's `.worktree-slot` file to find taken slots.
3. Claims the **lowest free** slot in {2,3,4,5} and writes it to `Aspire.AppHost/.worktree-slot`.

The scan-and-claim is wrapped in a **lockfile** in the git *common* directory — the one filesystem location shared across all worktrees — so two concurrent inits can never grab the same slot:

```
.git/worktree-slot.lock   (FileMode.CreateNew + DeleteOnClose)
   acquired → scan taken slots → claim lowest free → write file → release
```

`DeleteOnClose` means a crashed process never leaves a stale lock behind.

## `worktree-destroy.ps1` — release the slot

Run inside a worktree being torn down. It:

1. Reads the slot, **refusing slot 1** (it must never drop `DDD` / `IdentityDb`).
2. Drops `DDD_S{N}` and `IdentityDb_S{N}`.
3. Deletes the `.worktree-slot` file, freeing the slot for the next `init`.

The drop sets each database to `SINGLE_USER WITH ROLLBACK IMMEDIATE` **before** dropping it:

```sql
IF DB_ID('DDD_S2') IS NOT NULL
BEGIN
    ALTER DATABASE [DDD_S2] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [DDD_S2];
END
```

Without this, `DROP DATABASE` fails with *"database is currently in use"* (msg 3702) because LocalDB keeps connections **pooled** even after the Aspire apps stop. `ROLLBACK IMMEDIATE` evicts those pooled connections first.

## The slot semaphore in `/app-do-prd`

The autonomous orchestrator schedules slice workers in isolated worktrees against a dependency DAG. Because a live worktree consumes a slot — and a worktree's `bin/` is exclusive while its app runs (DLL locks make one live boot per worktree a natural invariant) — the orchestrator gains a **slot semaphore of size 4** (slots 2–5; slot 1 is the human's main checkout):

```
a slice launches only when:
    all its DAG blockers are merged
    AND fewer than 4 worker worktrees are in-flight
```

A DAG-ready but slot-starved slice waits and logs, and is reconsidered when `worktree-destroy` frees a slot on merge or failure. The in-flight count is the fast gate; `worktree-init.ps1` is the authoritative allocator and the hard guard that also accounts for any worktree a human spun up mid-run.

**Consequence accepted:** a test-only slice that never boots Aspire still occupies one of the 4 slots. Given the project's low concurrent-live-verification need, one-slot-per-worktree simplicity beats per-live-boot bookkeeping.

## Where to go next

- The *why* behind these decisions: [ADR-0002](../adr/0002-worktree-slots.md).
- The shared mechanics these scripts and the AppHost lean on: [03 — Shared Mechanics](03-shared-mechanics.md).
