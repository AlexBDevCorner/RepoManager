# Task 27 — Prevent simultaneous operations on one repository

- Milestone: 5 — Fetch
- Type: backend (concurrency)

Global concurrency and repository concurrency are separate. The user might click `Fetch` while `Update Safe Repositories` touches the same repo. Prevent it with a per-repository lock:

```csharp
ConcurrentDictionary<Guid, SemaphoreSlim>
```

```csharp
private readonly ConcurrentDictionary<Guid, SemaphoreSlim>
    _repositoryLocks = new();

private SemaphoreSlim GetLock(Guid repositoryId)
{
    return _repositoryLocks.GetOrAdd(
        repositoryId,
        _ => new SemaphoreSlim(1, 1));
}
```

Every mutation/network operation acquires this lock.

## Acceptance criteria

- [ ] Concurrent ops on the same repo are serialized.
- [ ] Ops on different repos still run in parallel (up to the global bound).
