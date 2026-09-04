# Task 7 — Detect whether a folder is a Git repository

- Milestone: 2 — Repository understanding
- Type: backend

## Goal

Build the first part of `RepositoryInspector`.

```csharp
public interface IRepositoryInspector
{
    Task<RepositorySnapshot> InspectAsync(
        RepositoryConfiguration repository,
        CancellationToken cancellationToken);
}
```

## Step 1: directory

Check `Directory.Exists(repository.Path)`. If false: `DirectoryExists = false`, `IsGitRepository = false`. Do not execute Git.

## Step 2: Git repository

Execute:

```text
git rev-parse --is-inside-work-tree
```

Expected: `true`. Do not rely on checking whether `.git` exists — worktrees and other configs don't behave like a simple `.git` folder. Git itself must tell us.

## Acceptance criteria

- [ ] Inspector correctly distinguishes: missing folder / ordinary folder / Git repository.
