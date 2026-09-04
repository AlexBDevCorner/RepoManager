# Task 28 — Implement RepositoryUpdater

- Milestone: 6 — Safe updates
- Type: backend

```csharp
public interface IRepositoryUpdater
{
    Task<RepositoryUpdateResult> UpdateAsync(
        RepositoryConfiguration repository,
        CancellationToken cancellationToken);
}
```

Do not let the caller specify arbitrary Git commands. The updater itself controls exactly what Git operation is allowed (only `git pull --ff-only --no-rebase`, see Tasks 29–30).

## Acceptance criteria

- [ ] Single entry point for updates; no arbitrary command injection.
