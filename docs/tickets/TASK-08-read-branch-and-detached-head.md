# Task 8 — Read current branch and detached HEAD

- Milestone: 2 — Repository understanding
- Type: backend

## Goal

Determine what HEAD represents.

Execute:

```text
git symbolic-ref --quiet --short HEAD
```

Example result: `feature/search`. If successful: `CurrentBranch = "feature/search"`, `IsDetachedHead = false`.

If it fails, determine HEAD commit via:

```text
git rev-parse --short HEAD
```

and mark `IsDetachedHead = true`, `CurrentBranch = null`. UI may display `Detached HEAD @ a84c019` rather than `No branch`.

## Acceptance criteria

- [ ] Branch name returned for normal checkout.
- [ ] Detached HEAD detected with short SHA available for display.
