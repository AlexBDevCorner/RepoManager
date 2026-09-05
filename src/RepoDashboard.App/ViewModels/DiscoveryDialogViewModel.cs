using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using RepoDashboard.Core.Discovery;

namespace RepoDashboard.App.ViewModels;

/// <summary>
/// View model for the discovery confirmation dialog (Task 40).
/// The dialog never adds anything itself — it only collects which
/// discovered repositories the user checked.
/// </summary>
public sealed partial class DiscoveryDialogViewModel : ObservableObject
{
    public ObservableCollection<DiscoveredRepositoryOption> Options { get; } = [];

    [ObservableProperty]
    private string _title = "Select repositories to add";

    public DiscoveryDialogViewModel(
        IReadOnlyList<DiscoveredRepository> candidates,
        ISet<string> alreadyTrackedPaths)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(alreadyTrackedPaths);

        foreach (var candidate in candidates)
        {
            Options.Add(new DiscoveredRepositoryOption(
                candidate,
                IsAlreadyTracked(candidate.Path, alreadyTrackedPaths)));
        }
    }

    public IReadOnlyList<string> SelectedPaths =>
        Options
            .Where(o => o.IsChecked && !o.IsAlreadyTracked)
            .Select(o => o.Path)
            .ToList();

    private static bool IsAlreadyTracked(
        string candidatePath, ISet<string> alreadyTrackedPaths)
    {
        var normalized = Normalize(candidatePath);

        return alreadyTrackedPaths.Any(
            tracked => string.Equals(
                Normalize(tracked), normalized,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }
}
