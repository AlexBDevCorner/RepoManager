# Task 10 — Detect merge/rebase/cherry-pick state

- Milestone: 2 — Repository understanding
- Type: backend (safety-critical)

## Goal

Automatic update must be disabled while another Git operation is active.

First obtain the actual Git directory:

```text
git rev-parse --absolute-git-dir
```

Example: `C:/Source/Repos/Store/.git`. Then inspect:

```text
MERGE_HEAD
CHERRY_PICK_HEAD
rebase-merge/
rebase-apply/
```

```csharp
MergeInProgress =
    File.Exists(Path.Combine(gitDirectory, "MERGE_HEAD"));

CherryPickInProgress =
    File.Exists(Path.Combine(gitDirectory, "CHERRY_PICK_HEAD"));

RebaseInProgress =
    Directory.Exists(Path.Combine(gitDirectory, "rebase-merge"))
    || Directory.Exists(Path.Combine(gitDirectory, "rebase-apply"));
```

Do not attempt automatic pull if any of these are true.

## Acceptance criteria

- [ ] Merge / rebase / cherry-pick in progress correctly detected.
- [ ] Classifier blocks auto-update in these states (see Task 16).
