namespace RepoDashboard.Core.Dashboard;

/// <summary>
/// Application-level service behind the dashboard UI.
/// The UI consumes only <see cref="RepositoryDashboardItem"/>.
/// Refresh means re-reading local Git information; it never fetches.
/// </summary>
public interface IRepositoryDashboardService
{
    Task<IReadOnlyList<RepositoryDashboardItem>> LoadAsync(
        CancellationToken cancellationToken);

    Task<RepositoryDashboardItem> RefreshAsync(
        Guid repositoryId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RepositoryDashboardItem>> RefreshAllAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates the directory (exists, is a Git repository, not already
    /// added), persists the new configuration and returns its dashboard item.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">Directory does not exist.</exception>
    /// <exception cref="InvalidOperationException">
    /// Not a Git repository, or already on the dashboard.
    /// </exception>
    Task<RepositoryDashboardItem> AddAsync(
        string path,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes the entry from <c>repositories.json</c> only.
    /// Never deletes the folder, Git data or remotes.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Unknown repository id.</exception>
    Task RemoveAsync(
        Guid repositoryId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches remote state (<c>git fetch --prune</c>) for one repository
    /// and then fully re-inspects it, so divergence is never stale.
    /// A fetch failure does not throw: the returned item carries
    /// <c>FetchError</c> alongside the freshly inspected local state.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Unknown repository id.</exception>
    Task<RepositoryDashboardItem> FetchAsync(
        Guid repositoryId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches every repository with bounded concurrency (at most 4
    /// simultaneous Git operations). One repository's failure never
    /// aborts the batch — every result is collected. Cancellation never
    /// discards completed work: pending repositories never start,
    /// in-flight Git is killed where possible, and the returned
    /// <see cref="RepositoryBatchResult"/> carries every completed item
    /// plus <c>WasCancelled</c>.
    /// </summary>
    Task<RepositoryBatchResult> FetchAllAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Safely updates one repository
    /// (fetch → inspect → classify → conditional pull → reinspect).
    /// Only fast-forwardable branches are pulled; anything else becomes
    /// <c>Skipped</c> with a reason, and a refused pull becomes a safe
    /// <c>Failed</c> — never a merge, rebase, reset or stash.
    /// The returned item carries the <c>UpdateResult</c> alongside the
    /// final snapshot.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Unknown repository id.</exception>
    Task<RepositoryDashboardItem> UpdateAsync(
        Guid repositoryId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates every repository with bounded concurrency (at most 4
    /// simultaneous Git operations). One repository's failure or skip
    /// never aborts the batch — every result is collected. Cancellation
    /// never discards completed work: see <see cref="FetchAllAsync"/>.
    /// </summary>
    Task<RepositoryBatchResult> UpdateAllAsync(
        CancellationToken cancellationToken);
}
