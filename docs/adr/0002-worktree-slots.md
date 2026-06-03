# Concurrent worktrees run as derived "slots" off a single integer, owned entirely by the Aspire AppHost

Running the system live from more than one git worktree at once was impossible: every checkout pinned the same browser-facing ports (Identity 7010, Scheduling 7001, Billing 7002, Angular 7003), the same Aspire dashboard/OTLP ports, the same LocalDB databases (shared `UserSecretsId`), and the same RabbitMQ data volume. We introduce a **slot model**: each worktree declares a single integer (1–5), and the AppHost *derives* every per-instance value from it via `value(base) = base + 100 * (slot - 1)`. Slot 1 is the main checkout and reproduces today's behaviour byte-for-byte (default when no slot is set); slots 2–5 are worktrees. The slot mechanism lives **only** in the Aspire AppHost — a local-dev-only orchestrator that is never deployed — so it cannot affect dev/staging/prod.

## Why a single derived integer, owned by the AppHost

The alternative is maintaining a per-slot config block by hand (ports, connection strings, authority URLs, CORS origins) in each worktree — four sets of interdependent values that must stay internally consistent or auth/DB silently breaks. Instead the only per-worktree artifact is **one integer** in a gitignored `Aspire.AppHost/.worktree-slot` file; everything else is computed in `AppHost.cs` and propagated to the child processes via Aspire `WithEnvironment(...)`. The integer file is gitignored (not committed) precisely *because* its value must differ per worktree — committing it would either force the same value everywhere (collisions return) or churn git history with per-worktree slot commits. The tooling (`worktree-init.ps1`, `worktree-destroy.ps1`, `AppHost.cs`, `.worktree-slot.example`) is committed; only the value is ignored.

## Why read the slot at build time, not via `AddParameter`

`WithHttpsEndpoint(port: …)` needs a literal `int` while the resource graph is being built. An Aspire `AddParameter("worktree-slot")` yields a parameter *resource* whose value isn't materialised until run, so it cannot feed port arithmetic — the same constraint that already forces the `messaging-broker` choice to be read via `builder.Configuration[...]` at build time (ADR-0001). The slot is read identically: `int.TryParse(builder.Configuration["worktree-slot"], …)`, default 1, with a fail-fast guard rejecting anything outside 1–5.

## Why slot 1 is always "today" for every resource

The propagation rule is **"slot 1 = exactly current behaviour; slots 2–5 = no colliding persistent resource."** Concretely:
- Resource ports derived; slot 1 → unchanged 7010/7001/7002/7003.
- Connection strings: the AppHost (which shares the `UserSecretsId`) reads the base strings and rewrites only the `Initial Catalog` token to `DDD_S{N}` / `IdentityDb_S{N}` for slot ≠ 1; slot 1 injects nothing and uses the existing `DDD` / `IdentityDb`. (Scheduling and Billing share one `DDD` database today; the slot work suffixes that single DB and deliberately does **not** split bounded-context databases.)
- `Auth:Authority`, CORS origins, and the auth cookie name are **overridden** by injected env vars for slots ≠ 1, never *removed* from `appsettings.json` — so standalone `dotnet run` and the deployed config story are unchanged (env beats appsettings/user-secrets in .NET's config order).
- RabbitMQ keeps `.WithDataVolume()` only on slot 1; slots 2–5 run ephemeral (scratch worktrees need no broker durability). The ASB emulator is already volume-less, so no change — ASB durable-persistence is explicitly out of scope.
- Aspire dashboard/OTLP/resource-service ports are offset by setting `ASPIRE_DASHBOARD_*` env vars at the very top of `AppHost.cs` *before* `CreateBuilder` (env beats `launchSettings.json`, which is git-tracked and cannot be per-worktree). **Residual unknown:** whether the dashboard UI port itself (`applicationUrl`) can be moved purely from `Program.cs` or needs a thin launch wrapper — to be confirmed by a live slot-2 boot.

## Why per-slot databases are created by dev-only `MigrateAsync`, not an init-script EF step

Identity already runs a `Development`-guarded `MigrateAsync` hosted service at startup. We mirror it in Scheduling and Billing so that **booting the slot is the entire DB init** — the slot's databases materialise and migrate on first run, uniformly across all three services, with no EF tooling or connection-string logic duplicated in a PowerShell script. The `Development` guard keeps dev/staging/prod migrating via the deployment pipeline, never at startup — consistent with the local-dev-only boundary. `worktree-destroy.ps1` drops `DDD_S{N}` + `IdentityDb_S{N}` (slot-1-protected) so LocalDB doesn't accumulate orphans.

## Why auth callbacks are slot-aware seed data, not a static 5-slot list or a runtime registration script

IdentityServer clients are EF-backed (`AddConfigurationStore`) and seeded from `IdentityServerConfig.Clients` into each slot's **own** `IdentityDb_S{N}` at startup (insert-when-empty). Because each slot is a fully independent triangle — its own Identity process, its own config store — slot N's Identity only ever needs slot N's redirect/post-logout/CORS URLs. So `IdentityServerConfig.Clients` becomes **slot-aware**: it reads the slot number and generates *that slot's* URLs, and each fresh per-slot Identity self-registers only its own. The "register on create / remove on teardown" lifecycle falls out of the per-slot DB lifecycle for free (fresh DB seeded on boot → dropped on `worktree-destroy`) — no script mutating Duende's `ClientRedirectUris` table, no schema coupling, no teardown-cleanup reliability risk.

Rejected: a single shared Identity (slot 1 only) serving all slots' callbacks via a static list or runtime DB mutation. It would make every worktree depend on the main-checkout Identity being up — destroying the self-containment that makes an agent worktree a *complete, disposable* stack, which is the whole point of "parallel live verification of worktrees."

## Why the slot cap (5) is now pragmatic, not architectural

With slot-aware seed data, the auth layer no longer caps slot count — and ports are just a formula. The cap of **5 (main + 4 worktrees)** is therefore purely a RAM ceiling (each live slot is an API trio + a broker; on the ASB path, emulator + companion SQL = 2 containers *per slot*) plus the orchestrator semaphore below. Raising it later is bumping a constant (+ having the RAM), not a callback-registration exercise.

## Why slots become a semaphore in the autonomous orchestrator

`/app-do-prd` schedules slice workers in isolated worktrees against a dependency DAG. A live worktree consumes a slot, and a worktree's `bin/` is exclusive while the app runs (DLL locks make one live boot per worktree a natural invariant). So the orchestrator gains a **slot semaphore of size 4** (slots 2–5; slot 1 is the human's main checkout) layered on top of the DAG: a slice launches only when its blockers are merged **and** fewer than 4 worker worktrees are in-flight. A DAG-ready but slot-starved slice waits and logs, and is reconsidered when `worktree-destroy` releases a slot on merge/failure. The orchestrator's in-flight count is the fast gate; `worktree-init.ps1` is the authoritative allocator (atomic claim guarded by a lockfile in the git common dir, the one filesystem location shared across all worktrees) and the hard guard that also accounts for any worktree a human spun up mid-run.

Consequence accepted: under this "slot = worktree" model, a test-only slice that never boots Aspire still occupies one of the 4 slots. Given the project's realistically-low concurrent-live-verification need, the simplicity of one-slot-per-worktree is worth the occasional wasted slot over per-live-boot bookkeeping.

## Why user secrets stay shared, and worktrees never write them

All worktrees share one `UserSecretsId` (it lives in the tracked `.csproj`). We deliberately keep it shared rather than swapping it per worktree: a per-worktree GUID edit permanently dirties the tree and risks accidental commits, and a fresh GUID yields an empty store that needs re-seeding and then drifts from the main one. The rule instead is **user secrets are the shared, static layer** (SQL password, messaging password, auth client secrets, base connection strings) and **a worktree never writes to it.** All per-slot variance enters *above* secrets in .NET's config precedence (environment variables > user secrets): the AppHost injects slot-derived values via `WithEnvironment(...)`, which shadow the shared secrets in the child processes without touching the store. A slot needing a per-instance override of something that lives in secrets today (e.g. one worktree pointing `messaging` at a real ASB namespace while another runs the emulator) puts that override in its gitignored cfg/env, not the store.

Rejected fallback (only if truly isolated stores are ever needed): an env-conditional `UserSecretsId` MSBuild property (`<UserSecretsId Condition="'$(DDD_SECRETS_ID)' != ''">$(DDD_SECRETS_ID)</UserSecretsId>`) set by `worktree-init`, accepting the seeding/drift cost knowingly. Not the plan.

## Status

accepted (design) — sliced into implementation issues via `/matt-to-issues` under PRD #7. The dashboard-UI-port and Docker container-name-collision unknowns are carried as acceptance checks on the relevant slices.
