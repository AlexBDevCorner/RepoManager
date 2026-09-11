namespace RepoDashboard.App.Services;

/// <summary>
/// Confirms removal of a repository from the dashboard (Review #15).
/// Kept behind an interface — consistent with
/// <see cref="IDiscoveryDialogService"/> and
/// <see cref="IRepositoryNameDialogService"/> — so the main view model
/// stays testable without showing windows. Returns true when the user
/// confirms; the repository files themselves are never deleted.
/// </summary>
public interface IRepositoryRemovalConfirmationService
{
    bool ConfirmRemoval(
        string repositoryName,
        string repositoryPath);
}
