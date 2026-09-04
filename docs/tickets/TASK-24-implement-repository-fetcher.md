# Task 24 — Implement RepositoryFetcher

- Milestone: 5 — Fetch
- Type: backend

```csharp
public interface IRepositoryFetcher
{
    Task<RepositoryOperationResult> FetchAsync(
        RepositoryConfiguration repository,
        CancellationToken cancellationToken);
}
```

Execute:

```text
git fetch origin --prune
```

As arguments: `["fetch", repository.PreferredRemote, "--prune"]`. Do not parse interactive terminal output. Git Credential Manager or SSH handles credentials.

## Result model

```csharp
public sealed record RepositoryOperationResult
{
    public required bool Success { get; init; }

    public required RepositoryOperationType Operation { get; init; }

    public required string Message { get; init; }

    public string? RawOutput { get; init; }

    public int? ExitCode { get; init; }

    public TimeSpan Duration { get; init; }
}
```

```csharp
public enum RepositoryOperationType
{
    Refresh,
    Fetch,
    Update
}
```

## Acceptance criteria

- [ ] `git fetch <preferredRemote> --prune` executed via runner.
- [ ] Success/failure with raw output + duration returned.
