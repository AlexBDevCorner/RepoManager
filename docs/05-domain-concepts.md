# 05 — Domain Concepts

Establish this vocabulary before implementing tasks.

## RepositoryConfiguration

Represents something the user explicitly added.

```csharp
public sealed record RepositoryConfiguration
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Path { get; init; }

    public string PreferredRemote { get; init; } = "origin";

    public string? DefaultBranchOverride { get; init; }

    public bool Enabled { get; init; } = true;
}
```

Example configuration:

```json
{
  "id": "abc...",
  "name": "Store",
  "path": "C:\\Source\\Repos\\Store",
  "preferredRemote": "origin",
  "defaultBranchOverride": null,
  "enabled": true
}
```

## RepositorySnapshot

Represents the Git state at one point in time. It is not persisted as configuration.

```csharp
public sealed record RepositorySnapshot
{
    public required Guid RepositoryId { get; init; }

    public required string Path { get; init; }

    public bool DirectoryExists { get; init; }

    public bool IsGitRepository { get; init; }

    public string? CurrentBranch { get; init; }

    public bool IsDetachedHead { get; init; }

    public bool IsDirty { get; init; }

    public string? UpstreamRef { get; init; }

    public string? UpstreamRemote { get; init; }

    public string? UpstreamBranch { get; init; }

    public string? DefaultRemoteBranch { get; init; }

    public Divergence? UpstreamDivergence { get; init; }

    public Divergence? DefaultBranchDivergence { get; init; }

    public bool MergeInProgress { get; init; }

    public bool RebaseInProgress { get; init; }

    public bool CherryPickInProgress { get; init; }

    public DateTimeOffset InspectedAt { get; init; }
}
```

## Divergence

```csharp
public sealed record Divergence(
    int Ahead,
    int Behind);
```

Example:

```text
HEAD vs origin/main

Ahead: 4
Behind: 7
```

means:

```text
HEAD contains 4 commits that origin/main does not contain.

origin/main contains 7 commits that HEAD does not contain.
```

## Important distinction: two kinds of divergence

Always keep these separate. Never combine them into one number.

### Current branch vs upstream

Example:

```text
feature/search
vs
origin/feature/search

Ahead 2
Behind 0
```

Answers:

> Have I pushed all my feature-branch commits? Has somebody changed my remote feature branch?

### Current HEAD vs remote default branch

Example:

```text
feature/search
vs
origin/main

Ahead 5
Behind 12
```

Answers:

> How far has my branch drifted from main?
