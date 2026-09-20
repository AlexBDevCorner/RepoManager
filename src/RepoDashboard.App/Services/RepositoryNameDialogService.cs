using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace RepoDashboard.App.Services;

/// <summary>
/// Avalonia repository rename dialog (RM-004). Ownership is explicit via
/// the desktop main window so modality is correct on Windows and Linux.
/// </summary>
public sealed class RepositoryNameDialogService : IRepositoryNameDialogService
{
    private readonly Func<Window?> _mainWindowProvider;

    public RepositoryNameDialogService(Func<Window?>? mainWindowProvider = null)
    {
        _mainWindowProvider = mainWindowProvider ?? GetMainWindow;
    }

    public async Task<string?> RequestNameAsync(
        string currentName,
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var dialog = new RenameRepositoryDialog(currentName, repositoryPath);
        var owner = _mainWindowProvider();

        if (owner is null)
        {
            throw new InvalidOperationException("No main window available for the rename dialog.");
        }

        var result = await dialog.ShowDialog<bool?>(owner);

        return result == true
            ? dialog.RepositoryName
            : null;
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
