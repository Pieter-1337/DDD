# Manual Workflow

Step-by-step chain from idea to commit. Use when you want a human in the loop at every boundary — planning, implementation, and shipping. Each skill is invoked explicitly via its slash command.

## When to use this flow

- The scope is unclear and you want to think aloud before writing any code
- The change crosses architectural boundaries and decisions need to surface as ADRs / CONTEXT.md updates
- You want to control pacing and review each phase
- The work is too big for one session and will be picked up later from issues on the tracker

## The chain

```
PLAN SIDE
  /matt-grill-with-docs           interview-style planning
  /matt-to-prd             publish PRD as draft issue to tracker
  human review
  /matt-to-issues          break PRD into tracer-bullet issues
  /matt-triage             (optional) move issues through ready states
  human review
  /commit                  commit CONTEXT.md / ADR changes to git

──────────────── handover via tracker ────────────────

IMPLEMENT / VALIDATE SIDE
  /implement               step-by-step build from PLAN.md or issue
  /validate                typecheck + tests + plan-coverage + diff review
  /verify                  run the app, observe behavior
  /simplify                review changes for reuse/quality, then re-validate
  /commit                  changelog + git commit
```

## Plan side — step by step

### 1. `/matt-grill-with-docs`

Interview-style discovery. The skill asks questions one at a time, walking down the decision tree, recommending an answer for each.

User-invocable only — won't trigger automatically. Run when you know you need to think a change through before touching code.

### 2. `/matt-to-prd`

Compacts the grill session into a PRD and publishes it as a **draft issue** on the tracker. The tracker is the review surface — the PRD lives there as a living document while it's being sharpened. Use the issue's URL as the source of truth for the rest of the chain.

### 3. Human review

Read the PRD on the tracker. Sharpen, redirect, or kill scope here — directly via comments/edits on the issue — before it becomes implementation issues.

### 4. `/matt-to-issues`

Splits the PRD into independently-grabbable issues using tracer-bullet vertical slices. Each issue should be small enough that one session can finish it.

### 5. `/matt-triage` _(optional)_

Moves issues through your triage state machine (e.g., `needs-triage` → `ready-for-agent`). Use when you want to prepare issues for an AFK agent to pick up.

### 6. Human review (again)

Issue titles, scope, ordering. Last chance before someone (or some agent) starts building.

### 7. `/commit`

The grill session likely produced in-repo doc artifacts: new entries in `CONTEXT.md`, new files in `docs/adr/`. Commit them so the implementer agent sees the current state of the domain language.

> Note: the plan-side `/commit` is **not** a handover doc. The tracker issues already serve as the handover. `matt-handoff` is for unplanned context breaks (running out of session, switching machines), not for the planned plan→implement boundary.

## Implement / Validate side — step by step

### 1. `/implement`

User-invocable only. Reads `PLAN.md` (or the issue file) and executes step-by-step, pausing for confirmation between steps.

### 2. `/validate`

Runs `dotnet build DDD.sln` + `dotnet test DDD.sln` (plus the SPA's `npm run build` / `npm test` when `Frontend/Angular/**` changed), then cross-references the diff against `PLAN.md`:

- Marks each plan step **Done / Partial / Missing / N/A**
- Surfaces quality issues in the diff (unhandled rejections, `any` casts, stray `console.log`, dead code)
- Produces a verdict: **PASS / NEEDS WORK / FAILING**
- Pauses to ask how you want to proceed

### 3. `/verify`

Built-in skill. Actually launches the app and observes runtime behavior. Catches the things `validate` can't — UI bugs, runtime crashes, "compiles and tests pass but the feature doesn't work."

Skip for pure backend / library changes; essential for UI work.

### 4. `/simplify`

Reviews the diff for reuse opportunities and code quality, then fixes what it finds, then re-runs `/validate`. Built-in skill. Always run — autopilot runs it too, the manual chain shouldn't ship a lower bar than autopilot.

### 5. `/commit`

Updates `CHANGELOG.md` with user-facing bullets and creates the git commit.

## Notes

- `karpathy-guidelines` runs passively in the background during all phases — keeps changes surgical and surfaces hidden assumptions.
- `matt-handoff` is **not** in this chain. Use it only for unplanned context breaks (running out of context mid-session, switching agents during investigation), not at the planned plan→implement boundary.
- For architectural refactors (not feature work), start with `/matt-improve-codebase-architecture` instead of `/matt-grill-with-docs`.

## Relationship to the other workflows

There is one plan-side chain (always manual — humans hold the pen on architecture) and three implement-side modes that vary by autonomy. This doc covers the **fully manual** path on both sides:

```
PLAN SIDE                            IMPLEMENT SIDE  (pick one mode per slice)
──────────────────────────           ───────────────────────────────────────
matt-grill-with-docs                 ┌─ workflow-manual.md       step-by-step
  │                                  │                           (this doc)
  ▼                                  │
matt-to-prd ──► PRD issue            ├─ workflow-automatic.md    one-shot
  │                                  │                           per issue
  ▼                                  │
matt-to-issues ──► slice issues ─►───┴─ workflow-autonomous.md   Product Requirement
                                                                 Document implementation
                       handover via the tracker issue
```

| Mode | Doc | Surface | When |
|---|---|---|---|
| Manual | [workflow-manual.md](./workflow-manual.md) | `/implement` → `/validate` → `/verify` → `/simplify` → `/commit` | Scope unclear, risk high, or you want to control pacing phase-by-phase |
| Automatic | [workflow-automatic.md](./workflow-automatic.md) | `/app-do-work <issue>` (one issue, linear chain) | Scope captured in an issue and you trust the implementer not to need pacing |
| Autonomous | [workflow-autonomous.md](./workflow-autonomous.md) | `/app-do-prd <PRD>` (orchestrator across slices, worktrees, parallel) | You want to walk away while a whole PRD unwinds |

Escalate _toward_ manual when a slice rejects autopilot or scope changes mid-flight; de-escalate _toward_ autonomous as the PRD's remaining slices become well-defined and low-risk.
