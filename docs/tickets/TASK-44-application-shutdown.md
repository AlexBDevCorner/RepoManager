# Task 44 — Handle application shutdown

- Milestone: 8 — Convenience and hardening
- Type: backend

If the app closes while Git commands run: cancel app token, kill Git process trees where appropriate, await shutdown briefly via host disposal, don't start new ops. Must not leave dozens of orphaned `git.exe` processes.

## Acceptance criteria

- [ ] Clean shutdown with no orphaned `git.exe`.
