using CommunityToolkit.Mvvm.ComponentModel;
using RepoDashboard.Core.Discovery;

namespace RepoDashboard.App.ViewModels;

/// <summary>
/// One checklist row in the discovery dialog. Pre-checked when the
/// repository is not already tracked; already-tracked rows are shown
/// disabled so the user cannot add duplicates.
/// </summary>
public sealed partial class DiscoveredRepositoryOption : ObservableObject
{
    public string Path { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool _isChecked;

    public bool IsAlreadyTracked { get; }

    /// <summary>
    /// Bound to <c>CheckBox.IsEnabled</c>: already-tracked rows cannot be
    /// (re)selected. Selection logic additionally filters them out, so
    /// duplicates are impossible even if the binding is bypassed.
    /// </summary>
    public bool IsSelectable => !IsAlreadyTracked;

    public string DisplayText =>
        IsAlreadyTracked ? $"{Name} — already on dashboard" : Name;

    public string DetailsText => Path;

    public DiscoveredRepositoryOption(DiscoveredRepository repository, bool isAlreadyTracked)
    {
        ArgumentNullException.ThrowIfNull(repository);
        Path = repository.Path;
        Name = repository.Name;
        IsAlreadyTracked = isAlreadyTracked;
        _isChecked = !isAlreadyTracked;
    }
}
