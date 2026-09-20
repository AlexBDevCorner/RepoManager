using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using RepoDashboard.App.ViewModels;
using RepoDashboard.Core.Discovery;

namespace RepoDashboard.App.Services;

/// <summary>
/// Avalonia discovery dialog (RM-004). Ownership is explicit via the desktop
/// main window so modal behavior is correct on both Windows and Linux.
/// </summary>
public sealed class DiscoveryDialogService : IDiscoveryDialogService
{
    private readonly Func<Window?> _mainWindowProvider;

    public DiscoveryDialogService(Func<Window?>? mainWindowProvider = null)
    {
        _mainWindowProvider = mainWindowProvider ?? GetMainWindow;
    }

    public async Task<IReadOnlyList<string>?> PickRepositoriesToAddAsync(
        IReadOnlyList<DiscoveredRepository> candidates,
        ISet<string> alreadyTrackedPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(alreadyTrackedPaths);

        var viewModel = new DiscoveryDialogViewModel(candidates, alreadyTrackedPaths);
        var dialog = new DiscoveryDialog(viewModel);
        var owner = _mainWindowProvider();

        if (owner is null)
        {
            throw new InvalidOperationException("No main window available for the discovery dialog.");
        }

        var result = await dialog.ShowDialog<bool?>(owner);

        return result == true
            ? viewModel.SelectedPaths
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
