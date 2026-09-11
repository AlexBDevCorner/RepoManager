using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>
    /// Task 52: currently highlighted checklist row. Bound to
    /// ListBox.SelectedItem so Space can toggle it without the mouse.
    /// Checkboxes themselves are Focusable="False" so arrow navigation
    /// stays on the ListBox.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleSelectedCommand))]
    private DiscoveredRepositoryOption? _selectedOption;

    /// <summary>
    /// Task 52: Space toggles the highlighted row. Already-tracked rows
    /// expose IsSelectable == false and are never toggled — the existing
    /// option model owns duplicate prevention.
    /// </summary>
    private bool CanToggleSelected() =>
        SelectedOption?.IsSelectable == true;

    [RelayCommand(CanExecute = nameof(CanToggleSelected))]
    private void ToggleSelected()
    {
        if (SelectedOption is { IsSelectable: true } option)
        {
            option.IsChecked = !option.IsChecked;
        }
    }

    /// <summary>
    /// Task 52: Ctrl+A checks every selectable row.
    /// </summary>
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var option in Options.Where(o => o.IsSelectable))
        {
            option.IsChecked = true;
        }
    }

    /// <summary>
    /// Task 52: Ctrl+Shift+A unchecks every selectable row.
    /// </summary>
    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var option in Options.Where(o => o.IsSelectable))
        {
            option.IsChecked = false;
        }
    }

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

        // Review #15: preselect the first selectable row so the
        // ListBox-scoped shortcuts (Space / Ctrl+A) are meaningful even
        // before the user moves the highlight. Falls back to the first
        // row when everything is already tracked; stays null when empty.
        SelectedOption =
            Options.FirstOrDefault(o => o.IsSelectable)
            ?? Options.FirstOrDefault();
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
