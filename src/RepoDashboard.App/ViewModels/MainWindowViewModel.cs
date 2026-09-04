using CommunityToolkit.Mvvm.ComponentModel;
using RepoDashboard.Core.Git;

namespace RepoDashboard.App.ViewModels;

/// <summary>
/// Main window view model. Always resolved through dependency injection
/// (see <see cref="App"/> composition root); never instantiated manually in views.
/// Application services (for example <c>IRepositoryDashboardService</c> in Task 19)
/// will be added as constructor parameters as they are implemented.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IGitEnvironment _gitEnvironment;

    [ObservableProperty]
    private bool _isGitAvailable;

    [ObservableProperty]
    private string _gitStatusText = "Checking Git…";

    public MainWindowViewModel(IGitEnvironment gitEnvironment)
    {
        ArgumentNullException.ThrowIfNull(gitEnvironment);
        _gitEnvironment = gitEnvironment;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var info = await _gitEnvironment.CheckAsync(cancellationToken);

        IsGitAvailable = info.Available;

        GitStatusText = info.Available
            ? $"Git {info.Version} detected"
            : info.Error ?? "Git status unknown.";
    }
}
