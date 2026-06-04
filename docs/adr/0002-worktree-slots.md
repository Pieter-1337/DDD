# Concurrent worktrees run as derived "slots", with shared mechanics in a BuildingBlock and a dev-only path that is inert in release

Running the system live from more than one git worktree at once was impossible: every checkout pinned the same browser-facing ports (Identity 7010, Scheduling 7001, Billing 7002, Angular 7003), the same Aspire dashboard/OTLP ports, the same LocalDB databases (shared `UserSecretsId`), and the same RabbitMQ data volume. We introduce a **slot model**: each worktree declares a single integer (1–5), and every per-instance value *derives* from it via `value(base) = base + 100 * (slot - 1)`. Slot 1 is the main checkout and reproduces today's behaviour byte-for-byte (the default when no slot is set); slots 2–5 are worktrees.

The mechanics of the slot model — the formula, the base ports, slot resolution, and the connection-string rewrite — live in a single dependency-free building block, **`BuildingBlocks.WorktreeSlots`**. Two consumers reference it: the **Aspire AppHost** (orchestration — never deployed) and the **Identity host** (a dev-only cookie-isolation path). The slot model touches deployable code in exactly one place — the Identity host — and that code is gated to `Development` *and* no-ops on slot 1, so the dev/staging/prod release path is byte-for-byte today's behaviour.

## Why the mechanics live in a shared `BuildingBlocks.WorktreeSlots`

The formula `base + 100*(slot - 1)` and the four base ports are needed in three places: the AppHost (to assign endpoint ports and inject slot-derived config), `IdentityServerConfig` (to seed each slot's own redirect/CORS URLs), and slot resolution at host startup. Duplicating the arithmetic invited drift — an earlier iteration literally carried a `// must stay in sync with AppHost.cs` comment above a second copy of the constants. Centralising it in one building block makes the formula and the base ports a single source of truth, unit-tested once (`WorktreeSlotTests`).

The building block is deliberately **dependency-free** — it references no web framework, no Duende, no configuration package (only in-framework `System.Data.Common` for the connection-string rewrite). That is what lets *both* an Aspire host and an ASP.NET Core host reference it without dragging extra packages into either. It exposes: `Port(base, slot)`; the base-port constants; `Resolve(appHostDirectory)` (env var → `.worktree-slot` file → default 1, for the AppHost); `FromValue(rawValue)` (parse a single injected config value, for web hosts); and `WithSlotDatabase(connectionString, slot)` (rewrite the `Initial Catalog` token).

## Why a single derived integer

The alternative is maintaining a per-slot config block by hand (ports, connection strings, authority URLs, CORS origins) in each worktree — four sets of interdependent values that must stay internally consistent or auth/DB silently breaks. Instead the only per-worktree artifact is **one integer** in a gitignored `Aspire.AppHost/.worktree-slot` file; everything else is computed and propagated to the child processes via Aspire `WithEnvironment(...)`. The integer file is gitignored (not committed) precisely *because* its value must differ per worktree — committing it would either force the same value everywhere (collisions return) or churn git history with per-worktree slot commits. The tooling (`worktree-init.ps1`, `worktree-destroy.ps1`, `AppHost.cs`, `.worktree-slot.example`) is committed; only the value is ignored.

## Why read the slot at build time, not via `AddParameter`

`WithHttpsEndpoint(port: …)` needs a literal `int` while the resource graph is being built. An Aspire `AddParameter("worktree-slot")` yields a parameter *resource* whose value isn't materialised until run, so it cannot feed port arithmetic — the same constraint that already forces the `messaging-broker` choice to be read via `builder.Configuration[...]` at build time (ADR-0001). The slot is resolved identically at build time: `WorktreeSlot.Resolve(projectDir)`, default 1, with a fail-fast guard rejecting anything outside 1–5.

## Why slot 1 is always "today" for every resource

The propagation rule is **"slot 1 = exactly current behaviour; slots 2–5 = no colliding persistent resource."** Concretely:
- Resource ports derived; slot 1 → unchanged 7010/7001/7002/7003.
- Connection strings: the AppHost (which shares the `UserSecretsId`) reads the base strings and rewrites only the `Initial Catalog` token to `DDD_S{N}` / `IdentityDb_S{N}` for slot ≠ 1; slot 1 injects nothing and uses the existing `DDD` / `IdentityDb`. (Scheduling and Billing share one `DDD` database today; the slot work suffixes that single DB and deliberately does **not** split bounded-context databases.)
- `Auth:Authority`, CORS origins, and the auth cookie name are **overridden** by injected env vars for slots ≠ 1, never *removed* from `appsettings.json` — so standalone `dotnet run` and the deployed config story are unchanged (env beats appsettings/user-secrets in .NET's config order).
- RabbitMQ keeps `.WithDataVolume()` only on slot 1; slots 2–5 run ephemeral (scratch worktrees need no broker durability). The ASB emulator is already volume-less, so no change.
- Aspire dashboard/OTLP/resource-service ports are offset by setting `ASPIRE_DASHBOARD_*` env vars at the very top of `AppHost.cs` *before* `CreateBuilder` (env beats `launchSettings.json`, which is git-tracked and cannot be per-worktree).

## Why per-slot databases are created by dev-only `MigrateAsync`, not an init-script EF step

Identity already runs a `Development`-guarded `MigrateAsync` hosted service at startup. We mirror it in Scheduling and Billing so that **booting the slot is the entire DB init** — the slot's databases materialise and migrate on first run, uniformly across all three services, with no EF tooling or connection-string logic duplicated in a PowerShell script. The `Development` guard keeps dev/staging/prod migrating via the deployment pipeline, never at startup — consistent with the local-dev-only boundary. `worktree-destroy.ps1` drops `DDD_S{N}` + `IdentityDb_S{N}` (slot-1-protected, `SINGLE_USER WITH ROLLBACK IMMEDIATE` first so pooled connections don't block the drop) so LocalDB doesn't accumulate orphans.

## Why auth callbacks are slot-aware seed data, not a static 5-slot list or a runtime registration script

IdentityServer clients are EF-backed (`AddConfigurationStore`) and seeded from `IdentityServerConfig.Clients` into each slot's **own** `IdentityDb_S{N}` at startup (insert-when-empty). Because each slot is a fully independent triangle — its own Identity process, its own config store — slot N's Identity only ever needs slot N's redirect/post-logout/CORS URLs. So `IdentityServerConfig.Clients(slot)` is **slot-aware**: it derives that slot's URLs from the shared `WorktreeSlot.Port`, and each fresh per-slot Identity self-registers only its own. The "register on create / remove on teardown" lifecycle falls out of the per-slot DB lifecycle for free (fresh DB seeded on boot → dropped on `worktree-destroy`) — no script mutating Duende's `ClientRedirectUris` table, no schema coupling, no teardown-cleanup reliability risk.

Rejected: a single shared Identity (slot 1 only) serving all slots' callbacks via a static list or runtime DB mutation. It would make every worktree depend on the main-checkout Identity being up — destroying the self-containment that makes an agent worktree a *complete, disposable* stack, which is the whole point of "parallel live verification of worktrees."

## Why the Identity host keeps a dev-only cookie-isolation path — and why it cannot move to the BuildingBlock

Most of the system needs no in-process slot code: the Scheduling and Billing hosts are purely config-driven (the AppHost injects their slot-derived CORS origins, authority, cookie name, and connection strings as env vars). The Identity host is the exception, for one reason: **cookies are scoped by host (`localhost`), not by port** (RFC 6265). Two IdentityServer instances on different slot ports therefore share cookies in a single browser profile unless their cookie *names* differ. The cookie name is set inside the Identity process at DI-registration time — the AppHost cannot reach in and rename it — so this is the one piece of slot logic that must live in a deployable host.

It is contained as follows:
- A single dev-only extension, `AddWorktreeSlotCookieIsolation()`, suffixes every host-scoped cookie with `.S{slot}`: the ASP.NET Core Identity application/external cookies, the antiforgery cookie, and Duende's check-session cookie (`Configure<IdentityServerOptions>`).
- It is called only under `if (builder.Environment.IsDevelopment())`, and returns immediately on slot 1. So in release (always slot 1, no `worktree-slot` injected) it is inert and the framework default cookie names are unchanged.

This cookie wiring **deliberately does not live in `BuildingBlocks.WorktreeSlots`**, even though that would maximise "consolidation". It touches the ASP.NET Core framework cookies and Duende's `IdentityServerOptions` — so moving it would force a dependency-free, AppHost-referenced building block to take a web-framework + Duende dependency, for a *single* consumer (only the Identity host owns these cookies). Dependency hygiene wins over total consolidation: the building block stays pure and supplies only the slot number (`FromValue`); the Identity-specific wiring stays in the Identity host as one dev-gated call.

## Why the slot cap (5) is now pragmatic, not architectural

With slot-aware seed data, the auth layer no longer caps slot count — and ports are just a formula. The cap of **5 (main + 4 worktrees)** is therefore purely a RAM ceiling (each live slot is an API trio + a broker; on the ASB path, emulator + companion SQL = 2 containers *per slot*) plus the orchestrator semaphore below. Raising it later is bumping a constant (`WorktreeSlot.Max`) and having the RAM, not a callback-registration exercise.

## Why slots become a semaphore in the autonomous orchestrator

`/app-do-prd` schedules slice workers in isolated worktrees against a dependency DAG. A live worktree consumes a slot, and a worktree's `bin/` is exclusive while the app runs (DLL locks make one live boot per worktree a natural invariant). So the orchestrator gains a **slot semaphore of size 4** (slots 2–5; slot 1 is the human's main checkout) layered on top of the DAG: a slice launches only when its blockers are merged **and** fewer than 4 worker worktrees are in-flight. A DAG-ready but slot-starved slice waits and logs, and is reconsidered when `worktree-destroy` releases a slot on merge/failure. The orchestrator's in-flight count is the fast gate; `worktree-init.ps1` is the authoritative allocator (atomic claim guarded by a lockfile in the git common dir, the one filesystem location shared across all worktrees) and the hard guard that also accounts for any worktree a human spun up mid-run.

Consequence accepted: under this "slot = worktree" model, a test-only slice that never boots Aspire still occupies one of the 4 slots. Given the project's realistically-low concurrent-live-verification need, the simplicity of one-slot-per-worktree is worth the occasional wasted slot over per-live-boot bookkeeping.

## Why user secrets stay shared, and worktrees never write them

All worktrees share one `UserSecretsId` (it lives in the tracked `.csproj`). We deliberately keep it shared rather than swapping it per worktree: a per-worktree GUID edit permanently dirties the tree and risks accidental commits, and a fresh GUID yields an empty store that needs re-seeding and then drifts from the main one. The rule instead is **user secrets are the shared, static layer** (SQL password, messaging password, auth client secrets, base connection strings) and **a worktree never writes to it.** All per-slot variance enters *above* secrets in .NET's config precedence (environment variables > user secrets): the AppHost injects slot-derived values via `WithEnvironment(...)`, which shadow the shared secrets in the child processes without touching the store.

## Status

accepted. Implemented under PRD #7; live multi-slot verification completed under #18 (two stacks side by side, isolated ports/DBs/cookies/bus). A step-by-step implementation walkthrough lives in `docs/worktree-slots/`.
