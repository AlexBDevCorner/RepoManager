using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace RepoDashboard.App.Services;

/// <summary>
/// Avalonia folder picker (RM-004). Uses the storage provider of the
/// current desktop main window so dialogs are owned correctly on both
/// Windows and Linux. Returns null when the user cancels or when no
/// desktop session is available (treated as cancellation, never a crash).
/// </summary>
public sealed class FolderPickerService : IFolderPickerService
{
    private readonly Func<Window?> _mainWindowProvider;

    public FolderPickerService(Func<Window?>? mainWindowProvider = null)
    {
        _mainWindowProvider = mainWindowProvider ?? GetMainWindow;
    }

    public async Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
    {
        var folders = await PickAsync(title, allowMultiple: false, cancellationToken);
        return folders?.FirstOrDefault();
    }

    public async Task<IReadOnlyList<string>?> PickFoldersAsync(string title, CancellationToken cancellationToken = default)
    {
        return await PickAsync(title, allowMultiple: true, cancellationToken);
    }

    private async Task<IReadOnlyList<string>?> PickAsync(
        string title, bool allowMultiple, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var window = _mainWindowProvider();

        if (window is null)
        {
            return null;
        }

        var topLevel = TopLevel.GetTopLevel(window);

        if (topLevel is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFolder> folders;

        try
        {
            folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = allowMultiple
                });
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        if (folders.Count == 0)
        {
            return null;
        }

        var paths = folders
            .Select(folder => folder.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToList();

        return paths.Count == 0 ? null : paths;
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
