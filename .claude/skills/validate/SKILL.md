---
name: validate
description: >
  Post-implementation validation step in the plan → implement → validate workflow. Use this skill
  whenever the user wants to verify their implementation is complete and correct — after coding,
  before committing, or when asking "did I miss anything?", "is this done?", "check my work",
  "review what I built", or "validate". Also trigger on /validate. Use proactively after the
  implement skill finishes if the user seems ready to commit or ship.
argument-hint: '[issue] (optional) — either an issue tracker reference (e.g. `#42` or a full issue URL) or a path to a local markdown file. If omitted, falls back to PLAN.md at the repo root.'
---

# Validate

This skill verifies that an implementation is correct, complete, and ready to commit. It runs
automated checks, cross-references against the source-of-truth for the work, and surfaces gaps
before they become bugs.

## Process

### 1. Load Context

Start by understanding what was supposed to be built and what actually changed. Resolve the
source-of-truth before doing anything else:

- **Tracker reference passed as argument** — if the argument looks like `#<digits>`, a full issue
  URL, or otherwise refers to the project issue tracker: fetch the issue body via the tracker CLI
  configured in `docs/agents/tracker.md`
  (e.g. `gh issue view <N> --repo <repo> --json title,body,comments,labels,state`). Use the
  `title`, `body`, and any review `comments` as the source. If the issue body references a parent
  issue (e.g. a PRD via `## Parent`), fetch that too — it carries decisions and constraints the
  child issue depends on.
- **Local file path passed as argument** — if the argument resolves to an existing markdown file,
  read it directly.
- **No argument** — fall back to reading `PLAN.md` at the repo root.
- **Nothing found** — ask the user to briefly describe what they implemented before proceeding.

Then run `git diff HEAD` (or `git diff main`) to see what was actually changed.

Summarize in one sentence: "You added X to Y and Z." Confirm with the user if it looks right.

### 2. Run Automated Checks

Run all checks that apply to this project. For each one, report pass/fail with the actual output
if it fails — not just "it failed".

**First, discover what scripts exist:**

```bash
cat package.json | grep -A 30 '"scripts"'
```

In monorepos, also check the root `package.json`. Run checks from the root if root scripts
delegate to packages (e.g. `bun run check` at root runs `bun run --filter '*' check`).

**Then run the relevant ones:**

- TypeScript + lint + format: `bun run check` from the repo root (delegates to package `check` scripts). In this repo the web package uses `vp check`, which covers TypeScript type-checking — no separate `typecheck` step is needed.
- Tests: `bun run test` from the repo root (bun delegates to packages that have a test script). If that fails, try `cd packages/<name> && bun run test`.
- Run both — don't stop at the first failure. Collect everything before reporting.

Run checks in parallel where they're independent. Don't stop at the first failure — collect all
failures before reporting.

If a script doesn't exist, skip it and note it's not configured. Don't treat a missing script as
a failure.

If you can't run bun commands (permissions issue), note that automated checks couldn't be
confirmed and recommend the user runs them manually before committing — but still proceed with
the rest of the validation using static analysis.

### 3. Cross-Reference Source vs Implementation

Walk the source-of-truth's checkable items and assess whether each was addressed in the diff:

- **For a tracker issue** — walk the `## Acceptance criteria` checkbox list. Each `- [ ]` item is
  an assertion to validate. Also pay attention to `## What to build` for any behavioural details
  not captured as explicit criteria.
- **For a PLAN.md file** — walk each **Step** section. Also check the **Tests** section if one
  exists.

For each item:

- **Done** — the diff clearly addresses it
- **Partial** — something was done but it looks incomplete
- **Missing** — no evidence in the diff that this item was tackled
- **N/A** — the item turned out not to be needed (explain why)

Be pragmatic — don't flag "missing" just because the exact function name in the source differs
from what was written. Assess intent, not literal text matching.

If tests were planned/specified and no test files were touched, flag it explicitly.

### 4. Surface Quality Issues

Look at the actual changed code and flag anything that would reasonably cause problems:

- Unhandled promise rejections or missing error handling **at system boundaries** (API calls,
  user input, file I/O) — not internal helpers where the caller is responsible
- TypeScript `any` casts or property accesses on types that are optional/nullable — check the
  actual type definition (in schema files, DTOs, generated types) not just what's inferred locally
- Console.log / debug statements left in
- Obvious missing edge cases the source-of-truth called out that aren't handled
- Dead code introduced (imports, variables, functions that are never used)

When code accesses `.property` on an external type (DTO, API response, generated schema), verify
whether that property is actually required or optional in the type definition. Accessing an
optional field without a null guard is a common source of runtime crashes that TypeScript strict
mode will flag.

Don't nitpick style or invent hypothetical edge cases. Focus on things that will actually matter.

### 5. Produce the Validation Report

Write a concise report directly in the conversation (no separate file unless the user asks).

Use this structure:

```
## Validation Report

### Automated Checks
- ✓ TypeScript: no errors
- ✗ Tests: 2 failing — [paste relevant failure output]
- ✓ Lint: clean
- — Build: no build script configured

### Source Coverage  (heading: "Acceptance criteria" for tracker issues, "Plan Coverage" for PLAN.md; omit if neither was found)
- ✓ Item 1: Added validateTokenExpiry() to src/auth/middleware.ts
- ~ Item 2: Error handling added but only for 401, source mentioned 403 too
- ✗ Item 3: Tests for the new middleware — no test files modified

### Issues Found
- `src/api/users.ts:47` — fetch() call has no error handling
- `src/auth/middleware.ts:12` — console.log left in

### Verdict: NEEDS WORK

**Before committing:**
1. Fix the 2 failing tests (see output above)
2. Add error handling to the fetch() at users.ts:47
3. Remove the console.log at middleware.ts:12
```

**Verdict rules:**

- **PASS** — all automated checks pass, no missing items from the source, no significant issues
- **NEEDS WORK** — automated checks pass but there are issues or gaps worth fixing first
- **FAILING** — one or more automated checks failed (type errors, test failures, lint errors)

### 6. Offer to Fix

After the report, ask the user how they'd like to proceed:

- If verdict is FAILING or NEEDS WORK, offer to fix the issues directly
- If verdict is PASS, offer to proceed to commit (using the commit skill if available)

Don't immediately start fixing without asking — the user might have context that changes the
priority or might prefer to fix things themselves.
