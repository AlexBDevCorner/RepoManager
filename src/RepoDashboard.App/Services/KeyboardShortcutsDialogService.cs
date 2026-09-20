using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace RepoDashboard.App.Services;

/// <summary>
/// Avalonia shortcut-help dialog (RM-004). Opened via F1 through the main
/// view model's <c>ShowHelpCommand</c>; the view model stays testable
/// because showing the window lives here.
/// </summary>
public sealed class KeyboardShortcutsDialogService : IKeyboardShortcutsDialogService
{
    private readonly Func<Window?> _mainWindowProvider;

    public KeyboardShortcutsDialogService(Func<Window?>? mainWindowProvider = null)
    {
        _mainWindowProvider = mainWindowProvider ?? GetMainWindow;
    }

    public async Task ShowAsync(CancellationToken cancellationToken = default)
    {
        var owner = _mainWindowProvider();

        var dialog = new KeyboardShortcutsDialog();

        if (owner is null)
        {
            dialog.Show();
            return;
        }

        await dialog.ShowDialog(owner);
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }
}
