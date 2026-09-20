namespace RepoDashboard.App.Services;

/// <summary>
/// Confirms removal of a repository from the dashboard.
/// Kept behind an interface — consistent with
/// <see cref="IDiscoveryDialogService"/> and
/// <see cref="IRepositoryNameDialogService"/> — so the main view model
/// stays testable without showing windows. Returns true when the user
/// confirms; the repository files themselves are never deleted.
/// Async because Avalonia modal dialogs are asynchronous.
/// </summary>
public interface IRepositoryRemovalConfirmationService
{
    Task<bool> ConfirmRemovalAsync(
        string repositoryName,
        string repositoryPath,
        CancellationToken cancellationToken = default);
}
