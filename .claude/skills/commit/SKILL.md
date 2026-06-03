---
name: 'commit'
description: 'How to commit changes in this repo  triggers: commit'
model: 'haiku'
---

# How we commit in this repo

Follow these steps in order.

## 1. Inspect

! `git status`
! `git diff`

## 2. Decide on splitting

Bundle all pending changes into a single commit by default. If the changes clearly span unrelated concerns, ask the user whether to split before committing.

## 3. Check for secrets

Before staging, scan filenames and diff content for anything that looks like a secret (`.env`, `*credentials*`, API keys, tokens, private keys, etc.). If anything looks sensitive, stop and confirm with the user before staging it.

## 4. Write the message

Use Conventional Commits with a flat type — no scope.

Subject line:

- Imperative mood ("add", not "added" or "adds")
- 72 characters or fewer
- No trailing period
- Format: `<type>: <subject>`

Types:

- `feat` — new user-facing feature
- `fix` — bug fix
- `docs` — documentation only
- `style` — formatting / whitespace, no behavior change
- `refactor` — restructure without changing behavior or fixing a bug
- `perf` — performance improvement
- `test` — adding or correcting tests
- `build` — build system / dependencies
- `ci` — CI configuration
- `chore` — tooling, configs, anything outside src/test
- `revert` — reverts a previous commit

Description:

- Write a concise description of the change in the body, wrapped at 72 characters. Explain the motivation and context

## 5. Do not push

Do not run `git push` automatically. Push only when the user explicitly asks (e.g., "push", "commit and push").
