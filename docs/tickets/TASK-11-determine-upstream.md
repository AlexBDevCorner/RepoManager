# Task 11 — Determine current branch upstream

- Milestone: 2 — Repository understanding
- Type: backend

## Goal

Determine what the current branch tracks.

Execute:

```text
git rev-parse --abbrev-ref --symbolic-full-name @{upstream}
```

Argument array:

```csharp
[
    "rev-parse",
    "--abbrev-ref",
    "--symbolic-full-name",
    "@{upstream}"
]
```

Example output: `origin/feature/search`. A failure normally means no upstream configured — valid state, not an application error.

## Extract remote and branch

Given `origin/feature/search`, use the FIRST slash:

```csharp
var separatorIndex = upstream.IndexOf('/');

var remote =
    upstream[..separatorIndex];

var branch =
    upstream[(separatorIndex + 1)..];
```

Do not split on every slash (branch may contain slashes). Result: `UpstreamRef = "origin/feature/search"`, `UpstreamRemote = "origin"`, `UpstreamBranch = "feature/search"`.

## Acceptance criteria

- [ ] Tracking branch parsed into remote + branch.
- [ ] No upstream → nulls, not an error.
