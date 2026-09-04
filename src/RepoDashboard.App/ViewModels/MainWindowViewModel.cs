using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDashboard.App.Services;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Git;

namespace RepoDashboard.App.ViewModels;

/// <summary>
/// Main window view model. Always resolved through dependency injection
/// (see <see cref="App"/> composition root); never instantiated manually in views.
/// Consumes only <see cref="RepositoryDashboardItem"/> — Git decisions stay
/// in <see cref="IRepositoryDashboardService"/>.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IGitEnvironment _gitEnvironment;
    private readonly IRepositoryDashboardService _dashboard;
    private readonly IFolderPickerService _folderPicker;

    public ObservableCollection<RepositoryRowViewModel> Repositories { get; } = [];

    [ObservableProperty]
    private RepositoryRowViewModel? _selectedRepository;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isGitAvailable;

    [ObservableProperty]
    private string _gitStatusText = "Checking Git…";

    [ObservableProperty]
    private string _statusText = string.Empty;

    public MainWindowViewModel(
        IGitEnvironment gitEnvironment,
        IRepositoryDashboardService dashboard,
        IFolderPickerService folderPicker)
    {
        ArgumentNullException.ThrowIfNull(gitEnvironment);
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(folderPicker);
        _gitEnvironment = gitEnvironment;
        _dashboard = dashboard;
        _folderPicker = folderPicker;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var info = await _gitEnvironment.CheckAsync(cancellationToken);

        IsGitAvailable = info.Available;

        GitStatusText = info.Available
            ? $"Git {info.Version} detected"
            : info.Error ?? "Git status unknown.";

        await LoadAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task LoadAsync(
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var repositories =
                await _dashboard.LoadAsync(cancellationToken);

            Repositories.Clear();

            foreach (var repository in repositories)
            {
                Repositories.Add(
                    new RepositoryRowViewModel(repository));
            }

            StatusText = Repositories.Count == 0
                ? "No repositories yet. Use Add Repository to get started."
                : $"Loaded {Repositories.Count} repositories.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Loading cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not load repositories: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Re-reads local Git information for the selected repository.
    /// Performs no network operation.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync(
        CancellationToken cancellationToken)
    {
        var selected = SelectedRepository;

        if (selected is null)
        {
            StatusText = "Select a repository first.";
            return;
        }

        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var item = await _dashboard.RefreshAsync(
                selected.RepositoryId, cancellationToken);

            selected.Update(item);
            StatusText = $"Refreshed '{selected.Name}'.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Refresh cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not refresh '{selected.Name}': {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Re-reads local Git information for every repository.
    /// Performs no network operation.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAllAsync(
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var repositories =
                await _dashboard.RefreshAllAsync(cancellationToken);

            SyncRows(repositories);
            StatusText = $"Refreshed {repositories.Count} repositories.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Refresh cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not refresh repositories: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddAsync(
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        var path = _folderPicker.PickFolder(
            "Choose a repository folder");

        if (path is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var item = await _dashboard.AddAsync(path, cancellationToken);

            var row = new RepositoryRowViewModel(item);
            Repositories.Add(row);
            SelectedRepository = row;
            StatusText = $"Added '{item.Configuration.Name}'.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Adding repository cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            MessageBox.Show(
                ex.Message,
                "Repo Dashboard — Cannot add repository",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(
        CancellationToken cancellationToken)
    {
        var selected = SelectedRepository;

        if (selected is null)
        {
            StatusText = "Select a repository first.";
            return;
        }

        if (IsBusy)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            $"Remove {selected.Name} from Repo Dashboard?\n\n" +
            "The repository and its files will not be deleted.",
            "Repo Dashboard",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _dashboard.RemoveAsync(
                selected.RepositoryId, cancellationToken);

            Repositories.Remove(selected);
            StatusText = $"Removed '{selected.Name}'.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Removing repository cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not remove '{selected.Name}': {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SyncRows(
        IReadOnlyList<RepositoryDashboardItem> repositories)
    {
        var fresh = repositories.ToDictionary(
            r => r.Configuration.Id);

        for (var i = Repositories.Count - 1; i >= 0; i--)
        {
            if (!fresh.ContainsKey(Repositories[i].RepositoryId))
            {
                Repositories.RemoveAt(i);
            }
        }

        foreach (var item in repositories)
        {
            var existing = Repositories.FirstOrDefault(
                r => r.RepositoryId == item.Configuration.Id);

            if (existing is null)
            {
                Repositories.Add(new RepositoryRowViewModel(item));
            }
            else
            {
                existing.Update(item);
            }
        }
    }
}
