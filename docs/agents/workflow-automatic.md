# Automatic Workflow

One-shot autopilot from an issue file to a committed change. Use when planning is done, scope is captured in an issue, and you want the implementation phase to run without per-step approval.

For PRD-scale runs (the orchestrator across many slice issues, with worktree isolation, dependency-graph scheduling, and per-issue agent auto-routing) see [workflow-autonomous.md](./workflow-autonomous.md).

## When to use this flow

- The plan side is finished (PRD + issues exist on the tracker, ADRs / CONTEXT.md committed)
- Scope is small enough for one session
- You trust the implementer not to need pacing
- You're comfortable doing the human review _after_ the commit lands rather than mid-implementation

## The chain

```
Prerequisite: plan-side chain has produced an issue file or tracker issue
              (see workflow-manual.md)

  /app-do-work <issue-file>
        │
        ├─ explore codebase
        ├─ implement
        ├─ validate (typecheck + tests, retry on error)
        ├─ simplify (refactor, then re-validate)
        ├─ commit (changelog + git commit)
        └─ produce QA list for human

  human review (post-commit)
```

## How `/app-do-work` works internally

Defined in `.claude/skills/app-do-work/SKILL.md`. Takes an issue file as its argument; aborts if not given one.

1. **Understand the task** — read the issue file, explore the codebase (delegates broad exploration to the built-in `Explore` agent to keep context light). If the issue already has an open PR, enter [iterate mode](./workflow-autonomous.md#iterating-on-an-open-pr) instead of starting fresh.
2. **Implement** — works through the plan step by step.
3. **Validate** — runs the feedback loops, repeats until clean:
   ```
   bun run check --fix
   bun run test:web
   bun run test:api
   bun run test:integration
   ```
4. **Simplify** — invokes `Skill('simplify')`, then re-runs the feedback loops.
5. **Reviewer pass** _(default on, disable with `--reviewer=false`)_ — spawns a `reviewer` sub-agent that reads the diff + tests and returns prioritised findings. The primary agent addresses anything actionable and re-validates before committing. See [Per-issue quality gate](./workflow-autonomous.md#per-issue-quality-gate).
6. **Commit** — updates `CHANGELOG.md` (user-facing bullets only, no class/method names), then commits via the `commit` skill.
7. **Report QA** — writes a list of items the user should manually verify.

## Per-edit feedback loop (Agent Hook)

A `PostToolUse` hook wired in `.claude/settings.json` runs single-file checks on every `Edit` or `Write`, before the slower full-suite validate at the end.

**Routing:**

| File pattern         | Tool                        | What runs                                                |
| -------------------- | --------------------------- | -------------------------------------------------------- |
| `**/packages/web/**` | `bun vp check --fix <file>` | TypeScript typecheck + lint + format on the changed file |

On failure the hook prints the tool's output and exits with code 2, which tells the model "your last edit broke something — fix it." Successful runs are silent.

## Manual rescue path

If autopilot stalls or produces a bad result on the single-issue run:

1. Don't fight the autopilot — stop it and inspect what was changed (`git status`, `git diff`).
2. If a PR was opened and CI is red, re-launching `/app-do-work <issue>` re-enters [iterate mode](./workflow-autonomous.md#iterating-on-an-open-pr) against the existing PR rather than starting fresh — try that before taking over manually.
3. Otherwise switch to the manual chain ([workflow-manual.md](./workflow-manual.md)) from whichever phase failed.
4. If the issue file was the root cause (under-specified, wrong slicing), regenerate via `/matt-to-issues` from the PRD.

## Relationship to the other workflows

There is one plan-side chain (always manual — humans hold the pen on architecture) and three implement-side modes that vary by autonomy. This doc covers the **single-issue automatic** path:

```
PLAN SIDE                            IMPLEMENT SIDE  (pick one mode per slice)
──────────────────────────           ───────────────────────────────────────
matt-grill-with-docs                 ┌─ workflow-manual.md       step-by-step
  │                                  │                           human-paced
  ▼                                  │
matt-to-prd ──► PRD issue            ├─ workflow-automatic.md    one-shot
  │                                  │                           (this doc)
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

Typical pattern: think with the manual chain, ship with autopilot, return to the manual chain when scope changes.
