# Task 14 — Complete RepositoryInspector

- Milestone: 2 — Repository understanding
- Type: backend

## Goal

`InspectAsync` produces the complete `RepositorySnapshot`.

Pseudo-code:

```csharp
public async Task<RepositorySnapshot> InspectAsync(
    RepositoryConfiguration repository,
    CancellationToken cancellationToken)
{
    if (!Directory.Exists(repository.Path))
    {
        return MissingRepository(repository);
    }

    if (!await IsGitRepositoryAsync(...))
    {
        return InvalidRepository(repository);
    }

    var branch =
        await GetBranchAsync(...);

    var dirty =
        await GetDirtyStateAsync(...);

    var gitDirectory =
        await GetGitDirectoryAsync(...);

    var operationState =
        GetOperationState(gitDirectory);

    var upstream =
        await GetUpstreamAsync(...);

    var defaultBranch =
        await ResolveDefaultBranchAsync(...);

    Divergence? upstreamDivergence = null;

    if (upstream is not null)
    {
        upstreamDivergence =
            await _divergenceCalculator.CalculateAsync(
                repository.Path,
                "HEAD",
                upstream.FullRef,
                cancellationToken);
    }

    Divergence? defaultDivergence = null;

    if (defaultBranch is not null)
    {
        defaultDivergence =
            await _divergenceCalculator.CalculateAsync(
                repository.Path,
                "HEAD",
                $"{repository.PreferredRemote}/{defaultBranch}",
                cancellationToken);
    }

    return new RepositorySnapshot
    {
        ...
    };
}
```

The inspector only reads. It must never `fetch` / `pull` / `checkout` / `reset` / `stash`. That separation is extremely important.

## Acceptance criteria

- [ ] Full snapshot populated (branch, dirty, operation state, upstream, default branch, both divergences).
- [ ] No network or mutation side effects.
