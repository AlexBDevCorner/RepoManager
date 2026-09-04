# Task 43 — Add cancellation

- Milestone: 8 — Convenience and hardening
- Type: backend + UI

Global ops accept `CancellationToken`. User presses `Cancel`. Then: pending repos don't start; running Git processes terminated where possible; completed repos stay completed; UI returns to idle. Don't leave `SemaphoreSlim` locks unreleased — use `finally`.

## Acceptance criteria

- [ ] Cancel stops queue, kills running git where possible, releases all locks.
