namespace RepoDashboard.App.Services;

/// <summary>
/// Picks repository folders via the Avalonia storage provider (RM-004).
/// Kept behind an interface so view models stay testable without showing
/// dialogs. Async because Avalonia folder picking is asynchronous and
/// requires a <c>TopLevel</c>.
/// </summary>
public interface IFolderPickerService
{
    /// <summary>
    /// Returns the chosen directory, or null when the user cancels.
    /// </summary>
    Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the chosen directories (multi-add), or null when the user
    /// cancels. The single-folder method stays for flows that intentionally
    /// pick one root (repository discovery).
    /// </summary>
    Task<IReadOnlyList<string>?> PickFoldersAsync(string title, CancellationToken cancellationToken = default);
}
