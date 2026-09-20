namespace RepoDashboard.App.Services;

/// <summary>
/// Asks the user for a repository display name (alias).
/// Kept behind an interface so the main view model stays testable
/// without showing windows. Returns the entered name, or null when
/// the user cancels. The physical folder is never renamed here —
/// renaming stays in the dashboard service after confirmation.
/// Async because Avalonia modal dialogs are asynchronous.
/// </summary>
public interface IRepositoryNameDialogService
{
    Task<string?> RequestNameAsync(
        string currentName,
        string repositoryPath,
        CancellationToken cancellationToken = default);
}
