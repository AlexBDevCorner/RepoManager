# Task 23 — Implement local Refresh

- Milestone: 4 — Read-only MVP
- Type: backend + UI

`Refresh` means: read the currently available local Git information again. It must perform no network operation.

Individual: `Refresh`. Global: `Refresh All`. Do not call `git fetch` during this operation. This distinction must remain clear to the user.

## Acceptance criteria

- [ ] Single + all refresh work offline.
- [ ] No `fetch` executed.
