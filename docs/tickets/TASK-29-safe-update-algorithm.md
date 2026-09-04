# Task 29 — Implement safe update algorithm

- Milestone: 6 — Safe updates
- Type: backend (safety-critical, order matters)

## Steps

1. **Fetch:** `git fetch origin --prune` (safe even if working tree is dirty).
2. **Inspect:** fresh `RepositorySnapshot`.
3. **Classify:** `_updateEligibilityClassifier.Classify(repository, snapshot)`.
4. **Skip if unsafe:** `Dirty` / `Detached` / `Diverged` / `Ahead` / `No upstream` → return `Skipped` with explanation.
5. **Pull only if CanFastForward:** `git pull --ff-only --no-rebase`. Include `--no-rebase` so a global `pull.rebase=true` can't turn this into a rebase. Promise: automatic update means fast-forward only.
6. **Reinspect:** `InspectAsync(...)` again. Expected final: `Ahead 0, Behind 0` vs upstream.

## Acceptance criteria

- [ ] Exact order fetch → inspect → classify → conditional pull → reinspect.
- [ ] Unsafe states never pull.
- [ ] Pull always uses `--ff-only --no-rebase`.
