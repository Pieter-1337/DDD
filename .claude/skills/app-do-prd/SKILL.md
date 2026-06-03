---
name: app-do-prd
description: Orchestrate a PRD across its slice issues end-to-end. Parses the dependency DAG from each child issue's `## Blocked by` field, schedules ready slices, spawns one worktree-isolated worker per slice (each running `/app-do-work`), handles iterate-on-CI-red, and continues independent siblings through localised failures. Use when the user wants to autonomously execute a whole PRD rather than one slice at a time.
argument-hint: '<PRD-ref> [--worktrees=true] [--parallel=true] [--reviewer=true] [--agent=auto] [--on-failure=continue-siblings] — `<PRD-ref>` is a tracker issue ref like `#33` or a full GitHub issue URL.'
disable-model-invocation: true
---

# Orchestrate PRD

Run a whole PRD autonomously: parse its child slice issues, build the dependency DAG, spawn one worker per ready slice (each running `/app-do-work`) in an isolated git worktree, react to CI / merge / failure events, and keep the chain flowing until every slice is merged or autonomously unrecoverable.

This skill is the orchestrator layer. Per-slice work happens inside `/app-do-work` (see `.claude/skills/app-do-work/SKILL.md`). Conceptual picture lives in `docs/agents/workflow-autonomous.md`.

## Workflow

### 1. Resolve the PRD

Fetch the parent issue via `gh issue view <PRD-ref> --repo <repo> --json title,body,labels,state` (repo lookup per `docs/agents/tracker.md`).

Abort if:
- The issue doesn't exist or isn't open.
- No other open issues reference it via `## Parent: #<N>` (without children, there's nothing to orchestrate).
- The labels don't include `enhancement` or whatever the project uses to mark implementable scope (warn, don't abort).

### 2. Fetch child issues and build the DAG

Use `gh issue list --repo <repo> --search 'in:body "#<PRD-number>"' --state open --json number,title,body,labels,state` to find candidates, then filter to those whose body contains `## Parent` referencing the PRD.

For each child, extract from the body:
- The `## Blocked by` section — parse issue refs (`#N` or full URLs) into a list.
- The `## What to build` section (passed verbatim to the worker; the orchestrator does not interpret it).
- Acceptance criteria headings (used for sanity-check only).

Build the dependency graph. Sanity-check for cycles; if any, halt and surface the cycle for human resolution.

### 3. Per-issue state and agent routing

Track each child issue's state through the run:

- **pending** — at least one blocker not yet merged
- **in-flight** — a worker is running, or its PR is open and unmerged
- **failed** — autonomous recovery exhausted, awaiting human rescue
- **merged** — done

For each issue, pre-compute the primary agent type using the rules below. First match wins. **Never falls back to `general-purpose`** — sub-spawning of specialists is in-band per the [workflow-autonomous doc](../../../docs/agents/workflow-autonomous.md#agent-auto-routing).

| Condition | Primary agent |
|---|---|
| Paths in `packages/api/**` AND none in `packages/web/**` | `backend-engineer` |
| Paths in `packages/web/**` AND none in `packages/api/**` | `frontend-engineer` |
| Only `docs/**`, `CHANGELOG.md`, `CONTEXT.md`, or other `*.md` paths | `documenter` |
| Mixed backend + frontend paths | `backend-engineer` (FE consumes BE; the worker self-spawns `frontend-engineer` sub-agents in-band when it crosses domains) |

`--agent=<name>` overrides the per-issue routing for the whole run.

If a child issue's body has no path signal that matches any rule, halt and report — that issue needs human re-scoping before it can be autonomously executed.

### 4. Scheduling loop

```
while any issue in {pending, in-flight}:

    # 4a. Launch newly-ready slices
    #
    # MAX_WORKER_SLOTS = 4   (slots 2–5; slot 1 is the human's main checkout)
    # Raising this constant gains capacity at the cost of RAM — no callback
    # registration or static slot-list changes required (no longer auth-coupled).
    #
    for issue in pending where all blockers are merged
                         AND no worker currently running for it:

        # Slot gate: the orchestrator's in-flight count is the fast check.
        if len(in-flight workers) >= MAX_WORKER_SLOTS:
            log "waiting for a slot to free (in-flight: {count}/{MAX_WORKER_SLOTS})"
            skip this issue for now; reconsider when a slot releases
            continue

        # Fast gate passed — attempt authoritative allocation.
        # worktree-init.ps1 scans all live worktrees + .worktree-slot files and
        # claims the lowest free slot under a lockfile in the git common dir.
        # It is the hard guard: it also catches any slot a human grabbed mid-run
        # that the orchestrator's in-flight count doesn't know about.
        run `scripts/worktree-init.ps1` in the target worktree
        if worktree-init exits non-zero ("no free slot"):
            log "waiting for a slot to free (worktree-init reports all 4 slots taken)"
            skip this issue for now; reconsider when a slot releases
            continue

        spawn Agent with:
            subagent_type      = agent_type for issue
            isolation          = "worktree" if --worktrees else current
            run_in_background  = true       if --parallel  else false
            prompt = "Run /app-do-work {issue-ref}
                      [--reviewer={inherited}]
                      [--agent={inherited if --agent was passed}].
                      Completion contract: you are NOT done until `gh pr view <num>`
                      returns an open PR for this issue. Report the PR URL as the
                      final line of your reply. A successful reviewer pass is NOT
                      a substitute for opening the PR."
        mark issue = in-flight
        record { issue → worker handle, branch (when known), PR URL (when known) }

    # 4a-verify. After a worker returns, before treating it as in-flight,
    #            confirm the PR exists:
    #     gh pr list --repo <repo> --search "<issue-ref> in:body" --state open --json url
    # If a PR is found: record the URL, mark in-flight, continue the loop.
    # If no PR is found: enter the §4a-finisher deterministic node below.
    # Do NOT advance dependents until the worker has shipped a PR.

    # 4a-finisher. Workers reliably bail after the reviewer pass without committing /
    #              pushing / opening a PR (see memory:appdoprd-worker-handoff). Prompt
    #              tweaks don't change this. So the orchestrator finishes the slice
    #              itself — a deterministic node, not another agent spawn.
    #
    #     1. cd <worker-worktree>
    #     2. git status
    #          - clean tree AND HEAD == master → worker did nothing → §7 failure (real).
    #          - otherwise: there's work to ship; proceed.
    #     3. Run validation deterministically (NOT through an agent):
    #          bun run check --fix
    #          bun run test:web
    #          bun run test:api
    #          bun run test:integration   # only if the slice touched packages/api
    #        If any suite is red:
    #          - re-spawn the worker ONCE with prompt:
    #            "Validation failed in this worktree. Output: <pasted>. Fix only the
    #             failures, then return — orchestrator will commit + push + open PR.
    #             Do NOT open a PR yourself."
    #          - then re-run step 3. If still red after one attempt → §7 failure
    #            with the validation output preserved.
    #     4. If CHANGELOG.md was not updated by the worker, add a one-line entry
    #        under today's date using the issue title.
    #     5. Commit uncommitted changes via Skill('commit') (conventional commits +
    #        signed co-author footer per repo convention).
    #     6. Push the branch and `gh pr create --base master --title <issue title>
    #        --body "Fixes #<n>\n\n<short summary>"`.
    #     7. Verify with `gh pr view <num>` — record URL, mark in-flight.
    #
    # The finisher consumes ~no agent tokens for the happy path (validation green
    # + commit + push + PR are all shell). Tokens only spent when validation is
    # red and a single agent re-spawn is needed to fix it.

    # 4b. React to events
    wait for the next of:

      • Worker reports PR opened
            → record PR URL; issue stays in-flight (PR is the slice's body of work until merged)

      • PR CI turns red on an open PR
            → re-spawn the same worker on the same branch in iterate mode (see §5)
            (when --auto-merge=true, the same trigger also fires when threads
             stay unresolved or reviewer findings stay unaddressed — see §6)

      • PR merged (by human, or by orchestrator when --auto-merge=true)
            → run `scripts/worktree-destroy.ps1` in the slice's worktree
              (drops DDD_S{N} + IdentityDb_S{N} and releases the slot)
              BEFORE the harness removes the worktree directory
            → mark issue = merged
            → recompute the ready set; loop continues (a waiting slice may
              now satisfy the slot gate)

      • Worker halts with explicit failure (no PR opened, or stuck mid-work,
        or iterate-mode budget exhausted without making the PR mergeable)
            → invoke autonomous recovery (see §7)

      • All workers idle AND no pending issue is ready
            → loop ends (some issues may be in failed state — see final report)
```

### 5. Iterate mode (resolve blockers on an open PR)

When a PR isn't immediately mergeable — CI red, unresolved review threads, unaddressed reviewer-pass findings, or merge conflicts — the orchestrator gives the worker **one attempt** to resolve the blockers.

Re-spawn the **same agent type** with the **same worktree / branch** and a prompt like:

> "PR {url} for issue #{n} has blockers: {summary — CI failures with logs, unresolved threads, reviewer findings, etc.}. Re-launch /app-do-work #{n} — the skill detects the open PR and enters iterate mode automatically. Diagnose, push a fix to the same branch. Report the new HEAD."

After the attempt:
- Validate runs again locally inside the worker.
- CI re-runs on the new push.
- The orchestrator re-checks readiness on the PR.

**Budget: 1 iterate attempt per slice per orchestrator run** — *in addition to* the initial implementation that opened the PR. So a slice in autonomous mode sees at most two worker spawns: the original implementer + the iterator. The initial implementation does NOT count toward the iterate budget.

If the PR is still not mergeable after the single iterate attempt, the slice goes to failure mode (§7). The human takes over from there — typically by responding to review threads, fixing the build themselves, or signalling the orchestrator to re-launch after they've cleared the blocker.

Note: the worker can push commits that *address* review comments but cannot *resolve* the review threads themselves — that resolution stays the reviewer's call (see [Iterating on an open PR](../../../docs/agents/workflow-autonomous.md#iterating-on-an-open-pr)). After the worker's one attempt, if comments remain unresolved, the slice waits.

(The per-slice iterate-mode loop is inside `/app-do-work` itself — see that skill's step 1, which detects an open PR and switches to iterate mode.)

### 6. Auto-merge (when `--auto-merge=true`)

The orchestrator merges PRs itself once it has confirmed all readiness conditions. Not GitHub's declarative auto-merge — the orchestrator runs `gh pr merge` actively, so the conditions it applies are its own (not just whatever branch protection happens to enforce).

For each in-flight PR, in the scheduling loop:

```
ready_to_merge(pr) =
    pr.statusCheckRollup is "SUCCESS"             # CI green
    AND every reviewThread.isResolved == true     # all review threads resolved
    AND no reviewer-pass-from-app-do-work-step-5 finding is still unaddressed
    AND pr.mergeStateStatus is "CLEAN"            # mergeable, no conflicts

if ready_to_merge(pr):
    gh pr merge <pr-num> --squash --delete-branch --repo <repo>
    # (--squash / --merge / --rebase per the repo's default; check via
    #  `gh repo view --json mergeCommitAllowed,squashMergeAllowed,rebaseMergeAllowed`)
    # After merge: run `scripts/worktree-destroy.ps1` to release the slot
    # (same cleanup path as the §4b merged event — see there for details).
else:
    # Trigger iterate mode (§5) — one attempt to resolve the blockers.
    # After that single attempt, re-check readiness; if still not met,
    # the slice falls into failure mode (§7).
```

Fetch PR state via `gh pr view <num> --json statusCheckRollup,reviewThreads,mergeStateStatus,reviews`.

Edge cases:

- **No human has reviewed yet, no comments left.** `reviewThreads` is empty → `every isResolved` is vacuously true → orchestrator merges as soon as CI is green. That's the most autonomous path; it's what `--auto-merge=true` is *for*.
- **Human left review comments and hasn't resolved them.** Iterate mode (§5) runs once: the worker addresses the comments by pushing fix commits. After that one attempt, if the threads are still unresolved (resolution stays the reviewer's call), the slice waits — orchestrator does not try again on its own.
- **CI red.** Iterate mode (§5) runs once. If it brings CI back to green AND all other readiness conditions hold, the orchestrator merges. If CI is still red, slice → failure mode (§7).
- **Reviewer-pass findings unaddressed** (in the PR's "Reviewer notes" section). Iterate mode runs once: the worker either addresses them or confirms they're intentional. After the attempt, if findings remain "unaddressed" in the PR body, the slice waits for human direction — explicit approval or manual fix.

`--auto-merge` does not change failure-mode behaviour. It only changes what happens on the *happy* path: instead of pausing for human merge, the orchestrator runs the merge once all gates pass.

### 7. Autonomous failure recovery

When a worker halts with an unrecoverable failure (NOT a one-attempt iterate-mode blocker — that's §5; NOT a missing-PR worker bail — that's §4a-finisher, which the orchestrator runs deterministically before falling through to here):

1. **One diagnostic re-spawn.** Re-launch the same worker against the same issue, passing the previous attempt's last error / "stuck" summary explicitly in the prompt. Often a second attempt with diagnostic context succeeds where the first didn't.
2. **If still failing**, run `scripts/worktree-destroy.ps1` in the slice's worktree — drops the slot databases and releases the slot number BEFORE the harness cleans the worktree. Note: `worktree-destroy.ps1` deletes only the DBs and the `.worktree-slot` marker file; the git worktree directory + branch + worker summary are preserved for human diagnosis (see §8 and the manual rescue path). A waiting slice may now satisfy the slot gate.
3. Branch on `--on-failure`:
   - `continue-siblings` (default): mark the slice failed, preserve the worktree + branch + worker summary, log the failure, **continue launching independent siblings**. The DAG knows which open issues do NOT transitively depend on this slice — those keep flowing.
   - `halt`: stop all launches, surface immediately.
4. **Halt unconditionally** when every remaining `pending` issue transitively depends on a failed slice — there's nothing useful left to launch.

### 8. Final report

When the loop ends, emit a structured report:

- **Merged slices** — one bullet per slice with the PR URL.
- **Failed slices** — one bullet per slice with the worktree path, branch name, last worker summary, and PR URL (if one was opened). Each is ready for `workflow-manual.md` rescue.
- **Skipped slices** — slices that stayed `pending` because their blockers failed. Listed transitively so the human can see what unblocks if they manually resolve a failure.

## Parameters

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `<PRD-ref>` | issue ref or URL | **required** | The parent PRD issue |
| `--worktrees` | bool | `true` | Per-worker worktree isolation (one branch + one PR per slice) |
| `--parallel` | bool | `true` | Launch ready workers concurrently when the DAG allows |
| `--reviewer` | bool | `true` | Cascades to each worker; each `/app-do-work` invocation runs its reviewer sub-agent pass before commit |
| `--agent` | enum or `auto` | `auto` | Override the per-issue agent routing. Cascades to every worker |
| `--on-failure` | `continue-siblings` / `halt` | `continue-siblings` | Behaviour after autonomous recovery (§7) gives up |
| `--auto-merge` | bool | `false` | When true, the orchestrator merges PRs itself once all readiness conditions are met: CI green, every review thread resolved, no reviewer-pass findings outstanding, GitHub reports mergeable. Imperative — the orchestrator runs `gh pr merge` actively rather than relying on GitHub's declarative auto-merge. If conditions aren't met, iterate mode (§5) runs once; if still not mergeable, the slice goes to failure mode (§7). See §6 for the full conditions and edge cases. |

## What this skill does not do

- It does not auto-merge PRs by default. Human review stays in the loop on every slice unless `--auto-merge=true` is passed (see §6), in which case the orchestrator merges each PR itself once CI is green, all review threads are resolved, and no reviewer-pass findings sit unaddressed.
- It does not file new issues. It only runs against pre-existing tracker issues created by `/matt-to-prd` + `/matt-to-issues`.
- It does not edit `CONTEXT.md` or ADRs directly — those changes come only via individual workers acting on their assigned slices. (The §4a-finisher node may add a missing `CHANGELOG.md` entry on a worker's behalf as part of finishing a slice the worker failed to commit.)
- It does not decompose a single issue across multiple parallel workers. If an issue is too big for one worker, that's a signal to re-slice via `/matt-to-issues`, not to bolt on intra-issue swarms.

## See also

- `docs/agents/workflow-autonomous.md` — conceptual picture, failure modes, escalation paths
- `.claude/skills/app-do-work/SKILL.md` — per-worker linear chain (what each spawned worker runs)
- `docs/agents/workflow-automatic.md` — when to use single-issue `/app-do-work` instead
- `docs/agents/workflow-manual.md` — fully-manual rescue path when autopilot can't recover
- `docs/agents/tracker.md` — issue tracker conventions and label vocabulary
