using RepoDashboard.App.ViewModels;
using RepoDashboard.Core.Discovery;

namespace RepoDashboard.App.Services;

public sealed class DiscoveryDialogService : IDiscoveryDialogService
{
    public IReadOnlyList<string>? PickRepositoriesToAdd(
        IReadOnlyList<DiscoveredRepository> candidates,
        ISet<string> alreadyTrackedPaths)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(alreadyTrackedPaths);

        var viewModel = new DiscoveryDialogViewModel(candidates, alreadyTrackedPaths);
        var dialog = new DiscoveryDialog(viewModel)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true
            ? viewModel.SelectedPaths
            : null;
    }
}
