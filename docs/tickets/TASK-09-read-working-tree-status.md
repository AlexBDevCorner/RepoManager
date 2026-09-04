# Task 9 — Read working-tree status

- Milestone: 2 — Repository understanding
- Type: backend

## Goal

Determine whether it is safe to mutate the checked-out branch.

Use:

```text
git status --porcelain=v2
```

Machine-readable formats are important. Do not parse normal output like `On branch main / Your branch is up to date...` — Git may change text and localized installs differ.

## Simple implementation (v1)

```csharp
var changeLines = output
    .Split('\n')
    .Where(line => !string.IsNullOrWhiteSpace(line))
    .Where(line => !line.StartsWith("#"));
```

If any change lines exist: `IsDirty = true`. Includes modified / added / deleted / untracked files. That is desirable. Do not automatically stash any of them.

## Acceptance criteria

- [ ] Dirty (modified, staged, deleted, untracked) → `IsDirty = true`.
- [ ] Clean tree → `IsDirty = false`.
