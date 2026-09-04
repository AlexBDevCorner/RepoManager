# Task 18 — Implement application-level repository service

- Milestone: 4 — Read-only MVP
- Type: backend

```csharp
public interface IRepositoryDashboardService
{
    Task<IReadOnlyList<RepositoryDashboardItem>>
        LoadAsync(CancellationToken cancellationToken);

    Task<RepositoryDashboardItem>
        RefreshAsync(
            Guid repositoryId,
            CancellationToken cancellationToken);

    Task<IReadOnlyList<RepositoryDashboardItem>>
        RefreshAllAsync(
            CancellationToken cancellationToken);
}
```

`RepositoryDashboardItem` may contain:

```csharp
public sealed record RepositoryDashboardItem
{
    public required RepositoryConfiguration Configuration { get; init; }

    public required RepositorySnapshot Snapshot { get; init; }

    public required UpdateDecision UpdateDecision { get; init; }

    public DateTimeOffset? LastSuccessfulFetch { get; init; }
}
```

This is what the UI consumes. The UI must not independently combine configuration / snapshot / eligibility — that is the application's responsibility.

## Acceptance criteria

- [ ] UI consumes only `RepositoryDashboardItem`.
- [ ] Load / Refresh / RefreshAll work locally (no fetch yet).
