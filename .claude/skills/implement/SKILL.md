---
name: 'implement'
description: >
  Executes a software engineering plan step-by-step from a PLAN.md file.
  Use this skill whenever the user wants to implement, execute, or build something
  from a plan. Trigger on: /implement, "implement the plan", "let's implement",
  "go ahead and build it", "execute the plan", "start implementing", "now write the code",
  or when a planning session has concluded and the user is ready to code.
  Always use this skill when a PLAN.md exists and the user says they're ready to start.
context: fork
model: sonnet
disable-model-invocation: true
---

# Implement

This skill executes a plan from a `PLAN.md` file — one step at a time, pausing for user confirmation between steps. The goal is confident, methodical execution with the user in the loop.

## Process

### 1. Find and Read the Plan

Look for `PLAN.md` in the project root. If it doesn't exist, ask the user for the path — do not proceed without a plan file.

Read the entire plan before starting. Make sure you understand:

- The goal
- All files that will be touched
- Every step in order
- What tests are expected

If anything in the plan is ambiguous before you start, raise it now — not mid-execution.

### 2. Brief the User

Before touching any code, show the user a quick summary:

```
Ready to implement: <Goal from plan>

Steps:
1. <Step 1 title>
2. <Step 2 title>
...

Files to touch: <list>

Shall I begin with step 1?
```

Wait for confirmation before proceeding.

### 3. Execute Steps One at a Time

For each step:

1. **Announce the step** — tell the user what you're about to do and why (one sentence)
2. **Do the work** — make the code changes, create files, whatever the step calls for
3. **Verify** — run any relevant checks (type check, lint, test for that module) if the tooling is available
4. **Report** — show what changed (file paths, what was added/modified)
5. **Pause** — ask "Step N complete. Ready for step N+1?" before continuing

Keep each step tight. If a step involves multiple small changes, do them together but report them as a unit.

### 4. Handle Blockers Immediately

Stop the moment you hit any of these:

- A test fails and you can't determine why
- A step references a file or function that doesn't exist
- An instruction is ambiguous enough that two reasonable interpretations would produce different code
- A dependency is missing and you're not sure whether to install it or flag it
- Something in the codebase contradicts an assumption in the plan

When you stop, explain:

- What you were trying to do
- What went wrong or what's unclear
- What you need from the user to proceed

Do not skip the step and continue — the steps may depend on each other.

### 5. Run Tests

After all steps are complete (or after any step that the plan marks as a checkpoint), run the full test suite if one exists. Report:

- How many tests passed / failed
- Which tests failed and what error they show
- Whether this looks like a pre-existing failure or something introduced by this implementation

If tests fail, treat it as a blocker — stop and ask the user how to proceed.

### 6. Write IMPLEMENTATION.md

When all steps are done, save `IMPLEMENTATION.md` in the project root using this template:

```markdown
# Implementation: <Goal from PLAN.md>

## Status

<Overall: Complete / Partial / Blocked>

## Steps

| Step            | Status    | Notes    |
| --------------- | --------- | -------- |
| 1. <step title> | ✓ Done    |          |
| 2. <step title> | ✓ Done    |          |
| 3. <step title> | ✗ Skipped | <reason> |

## Files Changed

- `path/to/file.ts` — <what changed>
- `path/to/new-file.ts` — created

## Test Results

<Pass/fail summary, or "No test suite found">

## Deviations from Plan

<Anything you did differently than the plan specified, and why. If nothing, write "None.">

## Open Items

<Anything left unresolved — follow-up steps, known issues, things the user needs to handle>
```

Tell the user the report has been saved and summarize any open items in one or two sentences.

---

## What Good Execution Looks Like

- You read the full plan before starting, not step by step as you go
- You don't invent steps that aren't in the plan
- You don't silently skip something because it's hard — you stop and say so
- You don't continue past a test failure
- The user always knows exactly what step you're on and what's next
- IMPLEMENTATION.md accurately reflects what happened — not just what the plan said should happen
