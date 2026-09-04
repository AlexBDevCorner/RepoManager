# Task 42 — Improve Git failure classification

- Milestone: 8 — Convenience and hardening
- Type: backend + UX

Start with raw error display; later recognize common errors.

- Auth (`Authentication failed`) → `Authentication failed. Check Git Credential Manager or SSH credentials.`
- Network (`Could not resolve host`) → `Remote could not be reached.`
- Removed (`Repository not found`) → `Remote repository could not be found or access was denied.`

Always retain raw Git output in details. Don't replace useful diagnostics with only generic messages.

## Acceptance criteria

- [ ] Known patterns mapped to friendly hints + raw output preserved.
