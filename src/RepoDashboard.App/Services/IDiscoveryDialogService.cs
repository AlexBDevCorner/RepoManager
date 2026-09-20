using RepoDashboard.Core.Discovery;

namespace RepoDashboard.App.Services;

/// <summary>
/// Shows discovered repositories for explicit user confirmation.
/// Kept behind an interface so the main view model stays testable
/// without showing windows. Returns the selected paths, or null when
/// the user cancels. Nothing is added here — adding stays in the
/// dashboard service after confirmation. Async because Avalonia modal
/// dialogs are asynchronous with explicit owner lifetime.
/// </summary>
public interface IDiscoveryDialogService
{
    Task<IReadOnlyList<string>?> PickRepositoriesToAddAsync(
        IReadOnlyList<DiscoveredRepository> candidates,
        ISet<string> alreadyTrackedPaths,
        CancellationToken cancellationToken = default);
}
