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
}
