# Task 45 — Add complete integration test matrix

- Milestone: 8 — Convenience and hardening
- Type: testing (uses Task 5 factory)

1. **Up to date:** `local/main = origin/main` → `Ahead 0 Behind 0`, `AlreadyUpToDate`.
2. **Behind:** remote `A-B-C`, local `A` → `Ahead 0 Behind 2`, `CanFastForward`.
3. **Ahead:** remote `A`, local `A-B` → `Ahead 1 Behind 0`, `Ahead`.
4. **Diverged:** remote `A-B`, local `A-C` → `Ahead 1 Behind 1`, `Diverged`.
5. **Dirty:** modify `README.md` uncommitted → `IsDirty = true`, `Update = Dirty`.
6. **Untracked:** create `new-file.txt` → `IsDirty = true`.
7. **No upstream:** `feature/local-only` untracked → `NoUpstream`.
8. **Detached HEAD:** checkout SHA → `IsDetachedHead = true`, `Update = DetachedHead`.
9. **Fast-forward update:** start `Behind 2`, run updater → pull ok, `Ahead 0 Behind 0`.
10. **Dirty update:** `Behind 2` + dirty → no pull, files unchanged, skipped.
11. **Diverged update:** no merge/rebase/reset, skipped.
12. **Main default:** remote HEAD `main` → `DefaultRemoteBranch = main`.
13. **Master default:** remote HEAD `master` → `DefaultRemoteBranch = master`.
14. **Missing folder:** delete dir → `RepositoryMissing`, app continues.

## Acceptance criteria

- [ ] All 14 scenarios automated and passing.
