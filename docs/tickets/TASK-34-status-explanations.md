# Task 34 — Implement useful status explanations

- Milestone: 7 — Good UX
- Type: UI (core UX requirement)

Users must understand why no action occurred. Bad: `Cannot update`. Good examples:

```text
Dirty
3 files contain uncommitted changes.
Automatic update was skipped.
```

```text
Ahead
Local branch has 2 commits that are not on origin/main.
There is nothing to pull.
```

```text
Diverged
Local branch: +3 commits
Remote branch: +5 commits

Manual merge or rebase is required.
```

```text
No upstream
Branch feature/foo does not track a remote branch.
Automatic update is unavailable.
```

## Acceptance criteria

- [ ] Every refusal shows human-readable reason (see classifier explanations).
