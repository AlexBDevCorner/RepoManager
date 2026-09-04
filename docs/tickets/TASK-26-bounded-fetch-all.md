# Task 26 — Implement bounded Fetch All

- Milestone: 5 — Fetch
- Type: backend (concurrency)

Do not launch 100 Git processes simultaneously. Initial concurrency: `4`.

```csharp
private readonly SemaphoreSlim _gitConcurrency =
    new(initialCount: 4);
```

Pattern:

```csharp
await _gitConcurrency.WaitAsync(cancellationToken);

try
{
    return await FetchRepositoryAsync(...);
}
finally
{
    _gitConcurrency.Release();
}
```

A failure in Repository A must not stop B/C/D. Collect every result.

## Acceptance criteria

- [ ] Max 4 concurrent Git operations.
- [ ] One failure doesn't abort the batch; all results collected.
