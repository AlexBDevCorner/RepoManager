using RepoDashboard.Core.Discovery;

namespace RepoDashboard.App.Services;

/// <summary>
/// Shows discovered repositories for explicit user confirmation (Task 40).
/// Kept behind an interface so the main view model stays testable
/// without showing windows. Returns the selected paths, or null when
/// the user cancels. Nothing is added here — adding stays in the
/// dashboard service after confirmation.
/// </summary>
public interface IDiscoveryDialogService
{
    IReadOnlyList<string>? PickRepositoriesToAdd(
        IReadOnlyList<DiscoveredRepository> candidates,
        ISet<string> alreadyTrackedPaths);
}
