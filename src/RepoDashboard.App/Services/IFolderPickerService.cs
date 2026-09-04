namespace RepoDashboard.App.Services;

/// <summary>
/// Picks a repository folder. Kept behind an interface so ViewModels
/// stay testable without showing dialogs.
/// </summary>
public interface IFolderPickerService
{
    /// <summary>
    /// Returns the chosen directory, or null when the user cancels.
    /// </summary>
    string? PickFolder(string title);
}
