namespace RepoDashboard.App.Services;

/// <summary>
/// Shows the keyboard-shortcut reference (F1) behind an interface (RM-004)
/// so the main view model stays testable without a real desktop session.
/// </summary>
public interface IKeyboardShortcutsDialogService
{
    Task ShowAsync(CancellationToken cancellationToken = default);
}
