# Autonomous Workflow

Product Requirement Document implementation, end-to-end. Hand the orchestrator a PRD and it spawns one agent per ready slice — auto-routed by work type, isolated in its own worktree + PR, respecting the `## Blocked by` dependency graph, in parallel where the DAG allows.

## When to use this flow

- A PRD has already been broken into vertical slices on the tracker (via `/matt-to-issues`)
- You trust the implementer not to need pacing on each slice
- You're comfortable doing the human review _after_ each PR lands rather than mid-implementation
- You want to walk away while the chain unwinds

If any of those aren't true, use [workflow-manual.md](./workflow-manual.md) per slice.

## The chain

```
Prerequisite: PRD + child issues on the tracker, each with "## Blocked by"
              and a clear scope. (See workflow-manual.md → matt-to-issues.)

  /app-do-prd <PRD-ref>
        │
        ├─ fetch PRD + child issues
        ├─ build DAG from each child's "## Blocked by"
        ├─ schedule loop:
        │     for each issue whose blockers are all merged:
        │         decide agent type (auto-route by work type)
        │         decide isolation (worktree / current checkout)
        │         spawn agent → /app-do-work <issue> (linear chain)
        │     wait for PR-merged notifications
        │     re-evaluate ready set, repeat
        └─ stop when all child issues are merged, or a failure halts the chain

  human review (PR-by-PR, between batches)
```

The orchestrator never edits code itself — per-slice work happens inside the worker spawned for each ready slice.

## Surface

```
/app-do-prd <target> [--worktrees=true] [--parallel=true] [--reviewer=true]
                     [--agent=auto] [--on-failure=continue-siblings]
                     [--auto-merge=false]
```

- `target` — a PRD issue (parent of slice issues, each linking back via `## Parent`)
- `--worktrees` / `--parallel` — isolation + concurrency for spawned workers
- `--reviewer` — cascades to every worker; each `/app-do-work` runs a reviewer sub-agent pass before commit
- `--agent` — override per-issue agent routing
- `--on-failure` — `continue-siblings` (default) vs `halt`
- `--auto-merge` — when true, the orchestrator merges PRs itself once readiness conditions are met (CI green + threads resolved + reviewer findings addressed); see [Stacking and merge cadence](#stacking-and-merge-cadence)

Full parameter semantics live in [`app-do-prd/SKILL.md`](../../.claude/skills/app-do-prd/SKILL.md).

## Per-issue states

A slice is not "done" when the PR opens — it's done when the PR merges. The orchestrator tracks each issue through:

- **pending** — at least one blocker is still unmerged
- **in-flight** — a worker is running, or the PR is open but unmerged
- **failed** — autonomous recovery exhausted; needs human rescue
- **merged** — done

The orchestrator does not auto-merge. PRs go through human review like any other change — see [Stacking and merge cadence](#stacking-and-merge-cadence).

## Agent auto-routing

Each ready slice gets routed to a **specialist** primary agent based on the paths it touches — `backend-engineer`, `frontend-engineer`, or `documenter`. There is no generic fallback. Mixed BE+FE issues default to `backend-engineer` (FE typically consumes BE), which self-spawns `frontend-engineer` sub-agents in-band when the work crosses domains.

When the primary needs cross-skill help, it recruits a specialist sub-agent using the same rules — **recursively**, in-band, never falling back to a generic agent. Sub-agents themselves can recruit further sub-sub-agents. The primary stays in charge of the slice end-to-end and integrates sub-agent reports.

If a slice's body has no path signal that matches any rule, the orchestrator halts and surfaces it — that's a signal the issue is under-specified and needs human re-scoping, not a guess.

Concrete rules and `--agent` override behaviour live in [`app-do-prd/SKILL.md` §3](../../.claude/skills/app-do-prd/SKILL.md).

## Per-issue quality gate

Each worker runs a `reviewer` sub-agent pass on its diff before committing (default on; `--reviewer=false` opts out). The reviewer returns prioritised findings; the worker addresses high-priority ones, re-validates, and includes the rest in the PR's "Reviewer notes" section so they don't get lost during human review.

Roughly +30 % tokens per slice; catches issues pre-PR and saves a review round-trip.

## Iterating on an open PR

A slice's PR is its body of work until merge. When a PR isn't immediately mergeable, the worker gets **one** attempt to resolve the blockers — *in addition to* the initial implementation that opened the PR. So a slice in autonomous mode sees at most two worker spawns: the original implementer + the iterator. After that single iterate attempt, if the PR still isn't mergeable, the slice waits for human attention (via [Failure mode](#failure-mode)). Not a retry loop.

Triggers for the one iterate attempt:

- **CI flipped red on the PR's head SHA.** Orchestrator re-spawns the same worker; `/app-do-work` detects the open PR and enters iterate mode (diagnose, push a fix to the same branch). After the push, CI re-runs. If green, the slice continues toward merge (or waits for human merge if `--auto-merge=false`). If still red, the slice goes to failure mode — human takes over.
- **`--auto-merge=true` and the readiness check has unresolved blockers.** Unresolved review threads, unaddressed reviewer-pass findings, or merge conflicts each trigger the same one-attempt iterate. The worker addresses what it can (pushes fix commits for comments; updates the PR body to address reviewer notes; rebases to clear conflicts). After the attempt, the orchestrator re-checks readiness. If everything is now clean, it merges. If not, the slice goes to failure mode.
- **Manual re-launch by a human.** Outside of `--auto-merge` mode, human review comments don't trigger an automatic iterate — they're conversational and the orchestrator doesn't speak for the reviewer's intent. To get the worker to address comments, re-invoke `/app-do-work <issue>` explicitly; the skill detects the open PR and enters iterate mode. No automatic budget applies — each manual re-launch is one human-initiated attempt.

Note: workers can push commits that *address* review comments but cannot *resolve* the review threads themselves — that resolution stays the reviewer's call. After the worker's one automatic attempt, if comments remain unresolved, the slice waits.

Iterate-mode pseudocode lives in [`app-do-work/SKILL.md` §8](../../.claude/skills/app-do-work/SKILL.md).

## Stacking and merge cadence

Who closes the merge — human or orchestrator — depends on the `--auto-merge` flag.

### `--auto-merge=false` (default): wait for human merge

When a slice's PR opens, the orchestrator pauses any downstream slice that has it in `## Blocked by`. After the human reviews and merges, the orchestrator picks the freshly-unblocked slice and launches it from updated master.

Each PR is independently reviewable. Slower but safer; matches the team's existing merge history. Applies whether `parallel=true` or not — slices with no remaining unmerged blockers run in parallel, slices blocked by an open PR wait.

### `--auto-merge=true`: orchestrator merges

The orchestrator merges PRs itself once it has confirmed all readiness conditions. It does NOT use GitHub's declarative auto-merge — it runs `gh pr merge` actively, so the criteria it applies are its own (not just whatever branch protection enforces).

Readiness conditions per PR:
- CI status is green
- Every review thread is resolved (or none exist)
- No reviewer-pass findings sit unaddressed in the PR's "Reviewer notes" section
- GitHub reports the PR mergeable (no conflicts, branch protection satisfied)

Edge cases (each triggers a single iterate attempt per the [Iterating on an open PR](#iterating-on-an-open-pr) policy; if not resolved by that one attempt, the slice goes to failure mode and the human takes over):

- **No human has reviewed and no comments are left** → conditions trivially pass once CI is green; orchestrator merges. Most autonomous path; what `--auto-merge=true` is for.
- **Human left review comments and hasn't resolved them** → one iterate attempt: worker pushes fix commits addressing the comments. If after that the threads are still unresolved (resolution stays the reviewer's call), slice waits for human.
- **CI flips red** → one iterate attempt to bring it back to green. If still red, slice → failure mode.
- **Reviewer pass found issues the worker chose not to address** (logged in the PR's "Reviewer notes" section) → one iterate attempt to address them. After the attempt, if findings remain in the PR body, slice waits — a human can resolve by fixing them or explicitly approving the PR (interpreted as "ship the notes").

### Stacked PRs (orthogonal alternative to both flag values)

Some teams branch each PR from the previous one rather than from master, so the chain can keep moving while reviews queue. Stacked PRs are powerful but the team's current review tooling doesn't make them friction-free. Documented for completeness; not the default. Independent of the `--auto-merge` flag — could in principle combine with either mode.

## Failure mode

Human intervention is the last resort, not the first. The orchestrator exhausts its own recovery options before halting.

A *failure* is distinct from a one-attempt iterate-mode blocker (which §5 handles). The categories the orchestrator treats as failure, and the autonomous recovery sequence that follows — one diagnostic re-spawn, then continue-siblings (or halt) — live in [`app-do-prd/SKILL.md` §7](../../.claude/skills/app-do-prd/SKILL.md).

**The philosophy:** prefer throughput. The chain keeps moving through localised failures rather than blocking the whole PRD on one stuck slice. `--on-failure=halt` is the conservative override for teams that don't yet trust autopilot for the PRD's domain.

The *bad result* case (agent reported done, PR is green, but the feature doesn't actually work) is the one thing the orchestrator can't detect autonomously — the [Per-issue quality gate](#per-issue-quality-gate) catches most of these pre-PR; the rest get caught at human PR review and trigger the [Manual rescue path](#manual-rescue-path) below.

## Manual rescue path

When a slice fails after autonomous recovery — see [Failure mode](#failure-mode) for what counts — the orchestrator halts and surfaces:

- the failing PR URL (if a PR was opened), and/or
- the worktree path + branch (the harness preserves it on failure), and/or
- the agent's last summary (containing whatever it managed to report before bailing)

From there:

1. **Inspect the failing state** — `git status` / `git diff` inside the worktree, the PR's CI logs, and the agent's last summary together usually identify whether the agent ran out of skill, scope was wrong, or environment / CI drifted.
2. **Take over the slice manually** via the [workflow-manual.md](./workflow-manual.md) chain when the agent couldn't make it across the line but the issue's scope is fine.
3. **Regenerate the issue** via `/matt-to-issues` from the PRD when the issue file itself was the root cause (under-specified, wrong slicing), then re-launch the orchestrator.
4. **Re-launch the orchestrator on the remaining open child issues** when the failure is localised — come back to the failed slice separately rather than blocking the whole PRD on it.

## Relationship to the other workflows

There is one plan-side chain (always manual — humans hold the pen on architecture) and three implement-side modes that vary by autonomy:

```
PLAN SIDE                            IMPLEMENT SIDE  (pick one mode per slice)
──────────────────────────           ───────────────────────────────────────
matt-grill-with-docs                 ┌─ workflow-manual.md       step-by-step
  │                                  │                           human-paced
  ▼                                  │
matt-to-prd ──► PRD issue            ├─ workflow-automatic.md    one-shot
  │                                  │                           per issue
  ▼                                  │
matt-to-issues ──► slice issues ─►───┴─ workflow-autonomous.md   Product Requirement
                                                                 Document implementation
                       handover via the tracker issue
```

**Implement-side modes** in order of decreasing human pacing:

| Mode | Doc | Surface | When |
|---|---|---|---|
| Manual | [workflow-manual.md](./workflow-manual.md) | `/implement` → `/validate` → `/verify` → `/simplify` → `/commit` | Scope unclear, risk high, or you want to control pacing phase-by-phase |
| Automatic | [workflow-automatic.md](./workflow-automatic.md) | `/app-do-work <issue>` (one issue, linear chain) | Scope captured in an issue and you trust the implementer not to need pacing |
| Autonomous | [workflow-autonomous.md](./workflow-autonomous.md) | `/app-do-prd <PRD>` (orchestrator across slices, worktrees, parallel) | You want to walk away while a whole PRD unwinds |

Typical pattern: think with the plan-side chain, ship with whichever implement-side mode fits the slice's risk profile. Escalate _toward_ manual when a slice rejects autopilot or scope changes mid-flight; de-escalate _toward_ autonomous as the PRD's remaining slices become well-defined and low-risk.
