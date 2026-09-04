# Task 31 — Implement Update Safe Repositories

- Milestone: 6 — Safe updates
- Type: backend

Global algorithm:

```text
Configured repositories
        ↓
bounded parallel execution
        ↓
Fetch
        ↓
Inspect
        ↓
Classify
        ↓
CanFastForward?
     /       \
   yes       no
    ↓         ↓
pull          skip
    ↓
Inspect
```

Pseudo-code:

```csharp
foreach repository in repositories
    execute with bounded concurrency:

        fetch

        snapshot = inspect

        decision = classify

        if decision.CanUpdate
            pull --ff-only --no-rebase

        finalSnapshot = inspect
```

Do not stop the batch because one repository fails.

## Acceptance criteria

- [ ] Only fast-forwardable branches pulled; rest skipped with reasons.
- [ ] Bounded concurrency; per-repo final re-inspection.
