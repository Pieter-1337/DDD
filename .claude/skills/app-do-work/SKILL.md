---
name: app-do-work
description: 'Execute a unit of work for one issue end-to-end: plan, implement, validate with typecheck and tests, optional reviewer pass, then commit and open a PR. When re-launched on an issue that already has an open PR, enters iterate mode and pushes follow-up commits to that branch instead of starting fresh. Use when user wants to do work, build a feature, fix a bug, or implement a phase from a plan.'
argument-hint: '<issue> [--reviewer=true] [--agent=auto] — `<issue>` is a tracker reference (e.g. `#42` or a full issue URL) or a path to a local issue markdown file.'
disable-model-invocation: true
---

# Do Work

Execute a complete unit of work for one issue: plan it, build it, validate it, commit it, push and open a PR.

When re-launched on an issue that already has an open PR (e.g. by `/app-do-prd`'s iterate-mode re-spawn, or by a human responding to review comments), this skill detects the PR and switches to iterate mode — it works on the existing branch and pushes additional commits, rather than starting a fresh implementation.

Conceptual picture in `docs/agents/workflow-automatic.md`. Orchestrator that calls this skill per slice: `.claude/skills/app-do-prd/SKILL.md` and `docs/agents/workflow-autonomous.md`.

## Workflow

### 1. Understand the task and detect mode

Resolve the argument to a source of truth:

- **Tracker reference** — if the argument looks like `#<digits>`, a full issue URL, or otherwise refers to the project issue tracker: fetch the issue body via the tracker CLI configured in `docs/agents/tracker.md` (e.g. `gh issue view <N> --repo <repo> --json title,body,comments,labels,state`). Use the `title`, `body`, and any review `comments` as the source. If the issue body references a parent issue (e.g. a PRD via `## Parent`), fetch that too — it carries decisions and constraints the child issue depends on.
- **Local file path** — if the argument resolves to an existing markdown file, read it directly.
- **Neither** — abort the skill and ask the user to clarify.

Then check whether the issue already has an open PR linked to it:

```
gh pr list --repo <repo> --search "<issue-ref> in:body" --state open \
           --json number,headRefName,url,statusCheckRollup,reviews
```

Match by `Fixes #<N>` / `Closes #<N>` / `Resolves #<N>` in PR bodies, or by branch-name convention if the project uses one.

- **No open PR** → fresh mode. Continue to step 2.
- **Open PR exists** → **iterate mode**. Skip to §8 (Iterate mode) and don't run steps 2–7.

Then explore the codebase to understand the relevant files, patterns, and conventions. Delegate codebase exploration beyond ~3 greps to the built-in `Explore` agent to keep context light.

If the task is ambiguous after reading the source, ask the user to clarify scope before proceeding.

### 2. Implement

Work through the plan step by step.

When the work crosses a domain boundary (e.g. a backend-engineer slice that needs a small frontend schema regen, a frontend-engineer slice that needs to inspect a backend constraint), spawn a specialist sub-agent using the same routing rules `/app-do-prd` uses (see `docs/agents/workflow-autonomous.md#agent-auto-routing`). Never fall back to a generic agent for cross-domain help.

### 3. Validate

Run the feedback loops and fix any issues. Repeat until all pass cleanly.

```bash
bun run check --fix    # static analysis of Typescript code with linting, typechecking, and formatting
bun run test:web       # frontend unit tests
bun run test:api       # backend unit tests
bun run test:integration  # end-to-end tests
```

### 4. Simplify

Run `Skill('simplify')` to simplify the code.

Run the validation loops again and fix any issues. Repeat until all pass cleanly.

### 5. Reviewer pass _(default on, disable with `--reviewer=false`)_

Spawn a `reviewer` sub-agent and ask it to read the current diff for correctness, missing test coverage, and consistency with the repository's conventions. The reviewer returns prioritised findings.

- Address every high-priority finding (real bugs, broken patterns, missing test coverage of new behaviour) and re-run §3 validate.
- Capture every finding the reviewer raised that you chose **not** to address — include it in the §8 QA report so the human can weigh it during PR review.

Disable this pass with `--reviewer=false` for trivially small changes where the cost of the reviewer is not justified.

### 6. Commit

Once static analysis and tests pass cleanly:

- Update `CHANGELOG.md` under today's date with functional, user-facing bullet points. Each bullet answers "what can a user now do?" or "what behavior changed?" — not "what was built". No class/method names, no test counts, no migration names.
- Commit the work via `Skill('commit')` so the project's conventions (Conventional Commits, no scope, signed Co-Authored-By footer) are applied.

### 7. Push branch and open PR — REQUIRED for completion

**This step is non-negotiable.** A successful reviewer pass in §5 is NOT a substitute for opening a PR. If you exit this skill without an open PR for the issue, your caller (a human user or `/app-do-prd`) will treat the run as a failure and may discard the worktree.

Before pushing, sanity-check the working tree: `git status` should show only the files you intended to change. If you see hundreds of unrelated files (typically a line-ending mass-rewrite from a misconfigured worktree), **stop and report** — do not commit the noise.

**Pre-push validation gate** — re-run the full §3 validation suite one final time after commit, before push. If the §5 reviewer pass produced fixes after §3, those fixes were never re-validated against the full suite. Run:

```bash
bun run check --fix
bun run test:web
bun run test:api
bun run test:integration   # only if the slice touched packages/api
```

If red: one more attempt to fix locally. If still red after that attempt: push anyway and call it out in the PR body under a "Known failures" section — do not silently ship a red branch, but also do not block the PR (CI will surface it and `/app-do-prd` will trigger iterate mode).

- Push the branch to the remote.
- Open a PR with `gh pr create --base master --title "<derived from issue title>" --body "Fixes #<issue-num>\n\n<short summary>"`.
- If the §5 reviewer pass produced findings you chose not to address, include them in the PR body under a "Reviewer notes" section so they don't get lost.
- **Verify the PR exists** via `gh pr view <num>` before returning.
- Report the PR URL as the final line of your reply.

### 8. Iterate mode (when step 1 detected an existing open PR)

Replaces steps 2–7 when re-launched on an issue with an open PR.

1. **Check out the PR's branch** into the current worktree (or, if invoked under `/app-do-prd` with `--worktrees=true`, into the existing worktree the PR was opened from). Do not branch from master.
2. **Fetch PR state** via `gh pr view <num> --json files,comments,reviews,statusCheckRollup,headRefOid`.
3. **Diagnose** what the iteration needs to address:
   - Red CI checks → fetch the failing run's logs (`gh run view <run-id> --log-failed`) and identify what to fix.
   - Unresolved review comments → read each thread; understand what the reviewer wants.
   - Both can be present at once; address all of them in one iteration when feasible.
4. **Implement the fix.**
5. **Re-run §3 validate locally.** Iterate until clean.
6. **Re-run §4 simplify** (then re-validate).
7. **Re-run §5 reviewer pass** on the new diff (the reviewer reads the delta, not the whole PR history — give it the cumulative diff since the previous commit on the branch).
8. **Commit and push more commits to the same branch.** Do NOT open a new PR.
9. **Leave a PR comment** describing what changed in this iteration, but do NOT resolve any review threads — resolution stays the reviewer's call.
10. **Report** the new HEAD SHA and a summary of the changes.

### 9. Report QA

Write a list of items the user should manually verify before merge. Include any reviewer findings from §5 that were noted-but-not-addressed.

## Parameters

| Parameter    | Type                          | Default                       | Meaning                                                                                                                                                                                      |
| ------------ | ----------------------------- | ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `<issue>`    | tracker ref / URL / file path | **required**                  | The single issue to implement (or iterate on)                                                                                                                                                |
| `--reviewer` | bool                          | `true`                        | Run the §5 reviewer sub-agent pass before commit                                                                                                                                             |
| `--agent`    | enum                          | _no-op when invoked directly_ | Honoured when called from `/app-do-prd` (which sets the spawned worker's subagent_type); ignored in direct invocation since the agent type is already determined by who's running this skill |

## See also

- `docs/agents/workflow-automatic.md` — conceptual picture, when to use single-issue mode
- `.claude/skills/app-do-prd/SKILL.md` — orchestrator that spawns workers running this skill per slice
- `docs/agents/workflow-autonomous.md` — iterate mode, failure handling, per-issue quality gate (§5 reviewer pass)
- `docs/agents/workflow-manual.md` — fully-manual rescue path when autopilot can't recover
