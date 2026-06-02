# Issue Tracker Conventions

## Tracker

GitHub Issues on `Pieter-1337/DDD`, accessed via the `gh` CLI.

```powershell
gh issue list --repo Pieter-1337/DDD
gh issue view <number> --repo Pieter-1337/DDD --json title,body,labels,state
```

## Triage label vocabulary

Canonical role names map 1:1 to GitHub label strings:

| Canonical role    | GitHub label      | Meaning                                        |
| ----------------- | ----------------- | ---------------------------------------------- |
| `needs-triage`    | `needs-triage`    | Awaiting triage                                |
| `needs-info`      | `needs-info`      | Waiting on reporter for more information       |
| `ready-for-agent` | `ready-for-agent` | Fully specified, ready for an AFK agent        |
| `ready-for-human` | `ready-for-human` | Needs human judgment or hands                  |
| `wontfix`         | `wontfix`         | This will not be worked on                     |

## Other labels

GitHub's defaults (`bug`, `enhancement`, `documentation`, …) are used as type labels alongside the triage roles. PRDs published by `/matt-to-prd` get `enhancement` + `ready-for-agent`.
