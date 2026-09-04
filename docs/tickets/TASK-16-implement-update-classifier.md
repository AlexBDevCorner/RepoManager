# Task 16 — Implement update classifier

- Milestone: 3 — Safety engine
- Type: backend (pure function, no IO)

## Suggested decision order

```csharp
public UpdateDecision Classify(
    RepositoryConfiguration configuration,
    RepositorySnapshot snapshot)
{
    if (!snapshot.DirectoryExists)
    {
        return new(
            UpdateEligibility.RepositoryMissing,
            "Repository directory does not exist.");
    }

    if (!snapshot.IsGitRepository)
    {
        return new(
            UpdateEligibility.InvalidRepository,
            "Directory is not a Git repository.");
    }

    if (snapshot.IsDetachedHead)
    {
        return new(
            UpdateEligibility.DetachedHead,
            "HEAD is detached.");
    }

    if (snapshot.MergeInProgress
        || snapshot.RebaseInProgress
        || snapshot.CherryPickInProgress)
    {
        return new(
            UpdateEligibility.OperationInProgress,
            "A Git merge, rebase or cherry-pick is in progress.");
    }

    if (snapshot.IsDirty)
    {
        return new(
            UpdateEligibility.Dirty,
            "The working tree contains uncommitted changes.");
    }

    if (snapshot.UpstreamRef is null)
    {
        return new(
            UpdateEligibility.NoUpstream,
            "Current branch has no upstream branch.");
    }

    if (!string.Equals(
            snapshot.UpstreamRemote,
            configuration.PreferredRemote,
            StringComparison.OrdinalIgnoreCase))
    {
        return new(
            UpdateEligibility.UpstreamUsesDifferentRemote,
            $"Current branch tracks '{snapshot.UpstreamRemote}', " +
            $"but this repository uses '{configuration.PreferredRemote}'.");
    }

    var divergence =
        snapshot.UpstreamDivergence;

    if (divergence is null)
    {
        return new(
            UpdateEligibility.Unknown,
            "Upstream divergence could not be determined.");
    }

    if (divergence.Ahead == 0
        && divergence.Behind == 0)
    {
        return new(
            UpdateEligibility.AlreadyUpToDate,
            "Current branch is already up to date.");
    }

    if (divergence.Ahead > 0
        && divergence.Behind == 0)
    {
        return new(
            UpdateEligibility.Ahead,
            $"Current branch is {divergence.Ahead} commit(s) ahead.");
    }

    if (divergence.Ahead > 0
        && divergence.Behind > 0)
    {
        return new(
            UpdateEligibility.Diverged,
            $"Local and remote branches have diverged: " +
            $"+{divergence.Ahead} / -{divergence.Behind}.");
    }

    return new(
        UpdateEligibility.CanFastForward,
        $"Current branch can fast-forward by " +
        $"{divergence.Behind} commit(s).");
}
```

## Acceptance criteria

- [ ] Order matches spec (missing → invalid → detached → operation → dirty → no upstream → different remote → divergence).
- [ ] Pure function, no IO.
