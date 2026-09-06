namespace RepoDashboard.Core.Dashboard;

/// <summary>
/// The outcome of a batch operation (Fetch All / Update All) under
/// cancellation (Task 43 review). Completed repositories keep their
/// results even when the batch is cancelled: <see cref="CompletedItems"/>
/// carries every repository that finished (success, failure or skip),
/// while <see cref="WasCancelled"/> signals that pending repositories
/// never started and in-flight Git work was killed. Callers apply the
/// completed items to the UI and reset only the repositories that did
/// not complete — never discarding successes.
/// </summary>
public sealed record RepositoryBatchResult
{
    public required IReadOnlyList<RepositoryDashboardItem> CompletedItems { get; init; }

    public required bool WasCancelled { get; init; }

    public static RepositoryBatchResult Cancelled(
        IReadOnlyList<RepositoryDashboardItem>? completed = null) =>
        new()
        {
            CompletedItems = completed ?? [],
            WasCancelled = true
        };

    public static RepositoryBatchResult Completed(
        IReadOnlyList<RepositoryDashboardItem> items) =>
        new()
        {
            CompletedItems = items,
            WasCancelled = false
        };
}
