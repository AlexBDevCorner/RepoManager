using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDashboard.App.Services;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Discovery;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Lifetime;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.App.ViewModels;

/// <summary>
/// Main window view model. Always resolved through dependency injection
/// (see <see cref="App"/> composition root); never instantiated manually in views.
/// Consumes only <see cref="RepositoryDashboardItem"/> — Git decisions stay
/// in <see cref="IRepositoryDashboardService"/>.
/// Cancellation (Task 43): every long operation runs under a linked
/// <see cref="CancellationTokenSource"/> held in <c>_activeOperation</c>.
/// <see cref="CancelCommand"/> cancels it: pending repositories never start
/// (lock/semaphore waits observe the token), the running git.exe is killed
/// by the command runner, completed rows keep their results, and locks are
/// always released via finally blocks in the dashboard service.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IGitEnvironment _gitEnvironment;
    private readonly IRepositoryDashboardService _dashboard;
    private readonly IFolderPickerService _folderPicker;
    private readonly IRepositoryDiscoveryService _discovery;
    private readonly IDiscoveryDialogService _discoveryDialog;

    /// <summary>
    /// The currently running operation, if any. Cancelled by
    /// <see cref="CancelCommand"/> (user) and by
    /// <see cref="NotifyShuttingDown"/> (application exit, Task 44).
    /// </summary>
    private CancellationTokenSource? _activeOperation;

    /// <summary>
    /// Application lifetime: cancelled once on shutdown so no new Git work
    /// starts while the host drains. Linked into every operation.
    /// When a shared <see cref="IApplicationShutdown"/> is injected (production),
    /// its shutdown token is the canonical shutdown signal and this local
    /// source is still cancelled on shutdown for backward-compatible drains;
    /// unit tests without a lifetime use only this local source.
    /// </summary>
    private readonly CancellationTokenSource _appLifetime = new();
    private readonly IApplicationShutdown? _applicationShutdown;

    public ObservableCollection<RepositoryRowViewModel> Repositories { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenTerminalCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private RepositoryRowViewModel? _selectedRepository;

    [ObservableProperty]
    private bool _hasSelection;

    partial void OnSelectedRepositoryChanged(RepositoryRowViewModel? value)
    {
        HasSelection = value is not null;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscoverCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    [NotifyCanExecuteChangedFor(nameof(FetchAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenTerminalCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscoverCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    [NotifyCanExecuteChangedFor(nameof(FetchAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateAllCommand))]
    private bool _isGitAvailable;

    [ObservableProperty]
    private string _gitStatusText = "Checking Git…";

    [ObservableProperty]
    private string _statusText = string.Empty;

    public MainWindowViewModel(
        IGitEnvironment gitEnvironment,
        IRepositoryDashboardService dashboard,
        IFolderPickerService folderPicker,
        IRepositoryDiscoveryService? discovery = null,
        IDiscoveryDialogService? discoveryDialog = null,
        IApplicationShutdown? applicationShutdown = null)
    {
        ArgumentNullException.ThrowIfNull(gitEnvironment);
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(folderPicker);
        _gitEnvironment = gitEnvironment;
        _dashboard = dashboard;
        _folderPicker = folderPicker;
        _discovery = discovery ?? new StubDiscoveryService();
        _discoveryDialog = discoveryDialog ?? new StubDiscoveryDialogService();
        _applicationShutdown = applicationShutdown;
    }

    /// <summary>
    /// Starts tracking a new operation: links the command's token with the
    /// application lifetime token so shutdown (Task 44) cancels in-flight
    /// work. The caller must pass the result to
    /// <see cref="EndOperation"/> in a finally block.
    /// The shutdown side is the shared lifetime when wired, else the local
    /// lifetime — either way user Cancel (via <see cref="CancelActiveOperation"/>)
    /// and shutdown stay linked here, while post-commit stages downstream
    /// observe only the shutdown side.
    /// </summary>
    private CancellationTokenSource BeginOperation(CancellationToken commandToken)
    {
        var shutdownToken = _applicationShutdown?.ShutdownToken ?? _appLifetime.Token;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken, shutdownToken);
        _activeOperation = cts;
        CancelCommand.NotifyCanExecuteChanged();
        return cts;
    }

    private void EndOperation(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_activeOperation, cts))
        {
            _activeOperation = null;
        }

        cts.Dispose();
        CancelCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Cancels the running operation, if any. Safe to call when idle.
    /// Used by the Cancel button and by the application host on shutdown.
    /// </summary>
    public void CancelActiveOperation()
    {
        try
        {
            _activeOperation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Operation finished between the null check and Cancel.
        }
    }

    /// <summary>
    /// Signals application shutdown (Task 44): no new operations may start
    /// (their linked token is already cancelled) and the running operation
    /// is cancelled so its git.exe process tree is killed. Notifies the
    /// shared lifetime first so post-commit stages observing only shutdown
    /// are also terminated.
    /// </summary>
    public void NotifyShuttingDown()
    {
        try
        {
            _applicationShutdown?.NotifyShuttingDown();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _appLifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        CancelActiveOperation();
    }

    private sealed class StubDiscoveryService : IRepositoryDiscoveryService
    {
        public Task<IReadOnlyList<DiscoveredRepository>> DiscoverAsync(
            string rootPath,
            int maxDepth = 3,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DiscoveredRepository>>([]);
    }

    private sealed class StubDiscoveryDialogService : IDiscoveryDialogService
    {
        public IReadOnlyList<string>? PickRepositoriesToAdd(
            IReadOnlyList<DiscoveredRepository> candidates,
            ISet<string> alreadyTrackedPaths) => null;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var info = await _gitEnvironment.CheckAsync(cancellationToken);

        IsGitAvailable = info.Available;

        GitStatusText = info.Available
            ? $"Git {info.Version} detected"
            : info.Error ?? "Git status unknown.";

        // Without git.exe every inspection would fail with obscure process
        // errors, so stop here with one clear status instead.
        if (!IsGitAvailable)
        {
            StatusText = "Repository inspection is unavailable until Git is installed.";
            return;
        }

        await LoadAsync(cancellationToken);
    }

    private bool CanLoad() => IsGitAvailable && !IsBusy;

    private bool CanAdd() => IsGitAvailable && !IsBusy;

    private bool CanDiscover() => IsGitAvailable && !IsBusy;

    private bool CanCancel() => IsBusy;

    // Single-repository commands accept an explicit target (row context
    // menu) and fall back to the selection (toolbar): a null parameter
    // means "use the selection".
    private bool CanRefresh(RepositoryRowViewModel? target) =>
        IsGitAvailable && !IsBusy && (target ?? SelectedRepository) is not null;

    private bool CanRefreshAll() => IsGitAvailable && !IsBusy;

    private bool CanFetch(RepositoryRowViewModel? target) =>
        IsGitAvailable && !IsBusy && (target ?? SelectedRepository) is not null;

    private bool CanFetchAll() => IsGitAvailable && !IsBusy;

    private bool CanUpdate(RepositoryRowViewModel? target) =>
        IsGitAvailable && !IsBusy && (target ?? SelectedRepository) is not null;

    private bool CanUpdateAll() => IsGitAvailable && !IsBusy;

    // Filesystem conveniences need no Git, only a selection and no
    // conflicting in-flight operation.
    private bool CanOpenFolder(RepositoryRowViewModel? target) =>
        !IsBusy && (target ?? SelectedRepository) is not null;

    private bool CanOpenTerminal(RepositoryRowViewModel? target) =>
        !IsBusy && (target ?? SelectedRepository) is not null && TerminalAvailable.Value;

    private bool CanCopyPath(RepositoryRowViewModel? target) =>
        !IsBusy && (target ?? SelectedRepository) is not null;

    /// <summary>
    /// Windows Terminal (<c>wt.exe</c>) on PATH, resolved once. When it is
    /// missing the terminal action stays disabled instead of failing.
    /// </summary>
    private static readonly Lazy<bool> TerminalAvailable = new(FindTerminal);

    private static bool FindTerminal()
    {
        const string fileName = "wt.exe";

        var path = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path
            .Split(Path.PathSeparator)
            .Select(directory => directory.Trim().Trim('"'))
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Any(directory =>
            {
                try
                {
                    return File.Exists(Path.Combine(directory, fileName));
                }
                catch
                {
                    return false;
                }
            });
    }

    // Remove edits only repositories.json, so it stays available without Git.
    private bool CanRemove(RepositoryRowViewModel? target) =>
        !IsBusy && (target ?? SelectedRepository) is not null;

    /// <summary>
    /// Guards direct invocations (commands bypass <c>CanExecute</c> when
    /// executed programmatically); the UI additionally disables the button.
    /// </summary>
    private bool RequireGit()
    {
        if (IsGitAvailable)
        {
            return true;
        }

        StatusText = "Repository inspection is unavailable until Git is installed.";
        return false;
    }

    /// <summary>
    /// Cancels the running operation (Task 43). Pending repositories never
    /// start, the running git.exe is killed where possible, completed rows
    /// keep their results, and all locks are released via finally blocks.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        CancelActiveOperation();
        StatusText = "Cancelling…";
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadAsync(
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        if (!RequireGit())
        {
            return;
        }

        var operation = BeginOperation(cancellationToken);
        IsBusy = true;
        StatusText = "Loading repositories…";

        try
        {
            var repositories =
                await _dashboard.LoadAsync(operation.Token);

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
            EndOperation(operation);
        }
    }

    /// <summary>
    /// Re-reads local Git information for the selected repository.
    /// Performs no network operation.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync(
        RepositoryRowViewModel? target,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        if (!RequireGit())
        {
            return;
        }

        var selected = target ?? SelectedRepository;

        if (selected is null)
        {
            StatusText = "Select a repository first.";
            return;
        }

        var operation = BeginOperation(cancellationToken);
        IsBusy = true;
        selected.SetActivity(RepositoryActivity.Refreshing, "Refreshing...");

        try
        {
            var item = await _dashboard.RefreshAsync(
                selected.RepositoryId, operation.Token);

            selected.Update(item);
            MarkCompletedWhenQuiet(selected, "Refreshed");
            StatusText = $"Refreshed '{selected.Name}'.";
        }
        catch (OperationCanceledException)
        {
            selected.SetActivity(RepositoryActivity.Idle, string.Empty);
            StatusText = "Refresh cancelled.";
        }
        catch (Exception ex)
        {
            selected.SetActivity(
                RepositoryActivity.Failed, $"Failed — {ex.Message}");
            StatusText = $"Could not refresh '{selected.Name}': {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            EndOperation(operation);
        }
    }

    /// <summary>
    /// Re-reads local Git information for every repository.
    /// Performs no network operation.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefreshAll))]
    private async Task RefreshAllAsync(
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        if (!RequireGit())
        {
            return;
        }

        var operation = BeginOperation(cancellationToken);
        IsBusy = true;
        SetAllActivities(RepositoryActivity.Refreshing, "Refreshing...");

        try
        {
            var repositories =
                await _dashboard.RefreshAllAsync(operation.Token);

            SyncRows(repositories, "Refreshed");

            var failedCount = repositories.Count(
                r => r.InspectionError is not null);
            var successfulCount = repositories.Count - failedCount;

            StatusText = failedCount == 0
                ? $"Refreshed {successfulCount} repositories."
                : $"Refreshed {successfulCount} of {repositories.Count} " +
                  $"repositories. {failedCount} failed.";
        }
        catch (OperationCanceledException)
        {
            ResetInProgressActivities();
            StatusText = "Refresh cancelled.";
        }
        catch (Exception ex)
        {
            ResetInProgressActivities();
            StatusText = $"Could not refresh repositories: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            EndOperation(operation);
        }
    }

    /// <summary>
    /// Fetches remote state for the selected repository. Progress and the
    /// outcome are shown inline on the row — never as modal dialogs.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchAsync(
        RepositoryRowViewModel? target,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        if (!RequireGit())
        {
            return;
        }

        var selected = target ?? SelectedRepository;

        if (selected is null)
        {
            StatusText = "Select a repository first.";
            return;
        }

        var operation = BeginOperation(cancellationToken);
        IsBusy = true;
        selected.SetActivity(RepositoryActivity.Fetching, "Fetching...");

        try
        {
            var item = await _dashboard.FetchAsync(
                selected.RepositoryId, operation.Token);

            selected.Update(item);
            MarkCompletedWhenQuiet(selected, "Fetched");
            StatusText =
                item.FetchError is not null
                    ? $"Fetch failed for '{selected.Name}': {item.FetchError}"
                    : item.InspectionError is not null
                        ? $"Fetched '{selected.Name}', but refreshing repository status failed: {item.InspectionError}"
                        : $"Fetched '{selected.Name}'.";
        }
        catch (OperationCanceledException)
        {
            selected.SetActivity(RepositoryActivity.Idle, string.Empty);
            StatusText = "Fetch cancelled.";
        }
        catch (Exception ex)
        {
            selected.SetActivity(
                RepositoryActivity.Failed, $"Failed — {ex.Message}");
            StatusText = $"Could not fetch '{selected.Name}': {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            EndOperation(operation);
        }
    }

    /// <summary>
    /// Fetches every repository with bounded concurrency. One failure never
    /// aborts the batch; each row shows its own terminal activity inline.
    /// Cancel stops the queue, kills running git where possible, keeps
    /// completed rows, and returns the UI to idle with locks released.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFetchAll))]
    private async Task FetchAllAsync(
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        if (!RequireGit())
        {
            return;
        }

        var operation = BeginOperation(cancellationToken);
        IsBusy = true;
        SetAllActivities(RepositoryActivity.Fetching, "Fetching...");

        try
        {
            var batch =
                await _dashboard.FetchAllAsync(operation.Token);

            // Completed rows keep their new state even when cancelled;
            // repositories that never finished stay in-progress here and
            // are reset to idle below. Rows are never removed: the batch
            // may be partial.
            ApplyBatchItems(batch.CompletedItems, "Fetched");

            if (batch.WasCancelled)
            {
                ResetInProgressActivities();
                StatusText = batch.CompletedItems.Count == 0
                    ? "Fetch cancelled."
                    : $"Fetch cancelled: {batch.CompletedItems.Count} " +
                      $"repositories completed before cancel.";
                return;
            }

            var repositories = batch.CompletedItems;
            var failedCount = repositories.Count(
                r => r.FetchError is not null || r.InspectionError is not null);
            var successfulCount = repositories.Count - failedCount;

            // Aggregate summary (Task 33): counts in the status bar, details
            // stay visible inline on each row.
            StatusText = failedCount == 0
                ? $"Fetch complete: {repositories.Count} repositories, " +
                  $"{successfulCount} successful."
                : $"Fetch complete: {repositories.Count} repositories, " +
                  $"{successfulCount} successful, {failedCount} failed.";
        }
        catch (OperationCanceledException)
        {
            ResetInProgressActivities();
            StatusText = "Fetch cancelled.";
        }
        catch (Exception ex)
        {
            ResetInProgressActivities();
            StatusText = $"Could not fetch repositories: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            EndOperation(operation);
        }
    }

    /// <summary>
    /// Safely updates the selected repository (fetch → inspect → classify →
    /// conditional fast-forward pull → reinspect). Skips and safe failures
    /// are shown inline on the row — never as modal dialogs.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync(
        RepositoryRowViewModel? target,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        if (!RequireGit())
        {
            return;
        }

        var selected = target ?? SelectedRepository;

        if (selected is null)
        {
            StatusText = "Select a repository first.";
            return;
        }

        var operation = BeginOperation(cancellationToken);
        IsBusy = true;
        selected.SetActivity(RepositoryActivity.Updating, "Updating...");

        try
        {
            var item = await _dashboard.UpdateAsync(
                selected.RepositoryId, operation.Token);

            selected.Update(item);
            StatusText = DescribeUpdateOutcome(selected.Name, item);
        }
        catch (OperationCanceledException)
        {
            selected.SetActivity(RepositoryActivity.Idle, string.Empty);
            StatusText = "Update cancelled.";
        }
        catch (Exception ex)
        {
            selected.SetActivity(
                RepositoryActivity.Failed, $"Failed — {ex.Message}");
            StatusText = $"Could not update '{selected.Name}': {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            EndOperation(operation);
        }
    }

    /// <summary>
    /// Updates every safe repository with bounded concurrency. Only
    /// fast-forwardable branches are pulled; the rest show their skip
    /// reason inline. One failure never aborts the batch.
    /// Cancel stops the queue, kills running git where possible, keeps
    /// completed rows, and returns the UI to idle with locks released.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUpdateAll))]
    private async Task UpdateAllAsync(
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        if (!RequireGit())
        {
            return;
        }

        var operation = BeginOperation(cancellationToken);
        IsBusy = true;
        SetAllActivities(RepositoryActivity.Updating, "Updating...");

        try
        {
            var batch =
                await _dashboard.UpdateAllAsync(operation.Token);

            ApplyBatchItems(batch.CompletedItems);

            if (batch.WasCancelled)
            {
                ResetInProgressActivities();
                StatusText = batch.CompletedItems.Count == 0
                    ? "Update cancelled."
                    : $"Update cancelled: {batch.CompletedItems.Count} " +
                      $"repositories completed before cancel.";
                return;
            }

            StatusText = DescribeUpdateSummary(batch.CompletedItems);
        }
        catch (OperationCanceledException)
        {
            ResetInProgressActivities();
            StatusText = "Update cancelled.";
        }
        catch (Exception ex)
        {
            ResetInProgressActivities();
            StatusText = $"Could not update repositories: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            EndOperation(operation);
        }
    }

    /// <summary>
    /// Opens the selected repository folder in Explorer.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder(RepositoryRowViewModel? target)
    {
        var selected = target ?? SelectedRepository;

        if (selected is null)
        {
            StatusText = "Select a repository first.";
            return;
        }

        try
        {
            if (!Directory.Exists(selected.DetailsPath))
            {
                StatusText = $"Folder does not exist: '{selected.DetailsPath}'.";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = selected.DetailsPath,
                UseShellExecute = true
            });

            StatusText = $"Opened folder for '{selected.Name}'.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open folder for '{selected.Name}': {ex.Message}";
        }
    }

    /// <summary>
    /// Opens Windows Terminal in the selected repository folder
    /// (<c>wt.exe -d &lt;path&gt;</c>). Disabled when <c>wt.exe</c> is
    /// not on PATH.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenTerminal))]
    private void OpenTerminal(RepositoryRowViewModel? target)
    {
        var selected = target ?? SelectedRepository;

        if (selected is null)
        {
            StatusText = "Select a repository first.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-d \"{selected.DetailsPath}\"",
                UseShellExecute = true
            });

            StatusText = $"Opened terminal for '{selected.Name}'.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open terminal for '{selected.Name}': {ex.Message}";
        }
    }

    /// <summary>
    /// Copies the selected repository path to the clipboard.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopyPath))]
    private void CopyPath(RepositoryRowViewModel? target)
    {
        var selected = target ?? SelectedRepository;

        if (selected is null)
        {
            StatusText = "Select a repository first.";
            return;
        }

        try
        {
            Clipboard.SetText(selected.DetailsPath);
            StatusText = $"Copied path for '{selected.Name}'.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not copy path for '{selected.Name}': {ex.Message}";
        }
    }

    private void SetAllActivities(
        RepositoryActivity activity, string activityText)
    {
        foreach (var row in Repositories)
        {
            row.SetActivity(activity, activityText);
        }
    }

    private void ResetInProgressActivities()
    {
        foreach (var row in Repositories)
        {
            if (row.Activity is RepositoryActivity.Refreshing
                or RepositoryActivity.Fetching
                or RepositoryActivity.Updating)
            {
                row.SetActivity(RepositoryActivity.Idle, string.Empty);
            }
        }
    }

    /// <summary>
    /// Aggregate summary after Update Safe Repositories (Task 33): total
    /// counts plus the per-reason breakdown (already current, ahead,
    /// dirty, diverged, ...), mirroring the ticket example
    /// ("5 updated, 6 already current, 1 ahead, 1 dirty, 1 diverged").
    /// Per-repository details stay visible inline on each row.
    /// </summary>
    private static string DescribeUpdateSummary(
        IReadOnlyList<RepositoryDashboardItem> repositories)
    {
        var updated = 0;
        var failed = 0;
        var skippedByReason = new Dictionary<UpdateEligibility, int>();

        foreach (var item in repositories)
        {
            if (item.InspectionError is not null
                || item.FetchError is not null
                || item.UpdateResult?.Outcome == RepositoryUpdateOutcome.Failed)
            {
                failed++;
            }
            else if (item.UpdateResult?.Outcome == RepositoryUpdateOutcome.Updated)
            {
                updated++;
            }
            else
            {
                var reason = item.UpdateDecision.Eligibility;
                skippedByReason[reason] = skippedByReason.TryGetValue(reason, out var count)
                    ? count + 1
                    : 1;
            }
        }

        var segments = new List<string> { $"{updated} updated" };

        foreach (var reason in SkipReasonOrder.Where(skippedByReason.ContainsKey))
        {
            segments.Add($"{skippedByReason[reason]} {UpdateEligibilityLabels.Short(reason)}");
        }

        if (failed > 0)
        {
            segments.Add($"{failed} failed");
        }

        return $"Update complete: {repositories.Count} repositories, " +
               $"{string.Join(", ", segments)}.";
    }

    private static readonly IReadOnlyList<UpdateEligibility> SkipReasonOrder =
    [
        UpdateEligibility.AlreadyUpToDate,
        UpdateEligibility.Ahead,
        UpdateEligibility.Dirty,
        UpdateEligibility.Diverged,
        UpdateEligibility.NoUpstream,
        UpdateEligibility.UpstreamUsesDifferentRemote,
        UpdateEligibility.DetachedHead,
        UpdateEligibility.OperationInProgress,
        UpdateEligibility.RepositoryMissing,
        UpdateEligibility.InvalidRepository,
        UpdateEligibility.CanFastForward,
        UpdateEligibility.Unknown,
    ];

    private static string DescribeUpdateOutcome(
        string name, RepositoryDashboardItem item)
    {
        if (item.UpdateResult is null)
        {
            return item.InspectionError is not null
                ? $"Could not update '{name}': {item.InspectionError}"
                : $"Could not update '{name}'.";
        }

        return item.UpdateResult.Outcome switch
        {
            Core.Sync.RepositoryUpdateOutcome.Updated =>
                $"Updated '{name}'.",
            Core.Sync.RepositoryUpdateOutcome.Skipped =>
                $"Skipped '{name}' — {item.UpdateResult.Message}",
            _ => $"Update failed for '{name}': {item.UpdateResult.Message}",
        };
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync(
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        if (!RequireGit())
        {
            return;
        }

        var path = _folderPicker.PickFolder(
            "Choose a repository folder");

        if (path is null)
        {
            return;
        }

        var operation = BeginOperation(cancellationToken);
        IsBusy = true;

        try
        {
            var item = await _dashboard.AddAsync(path, operation.Token);

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
            EndOperation(operation);
        }
    }

    /// <summary>
    /// Discovers Git repositories under a user-chosen parent folder
    /// (Task 40, max depth 3, hidden folders skipped, no descent into
    /// found repositories) and shows them in a checklist. Nothing is
    /// added until the user confirms with <c>Add Selected</c>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDiscover))]
    private async Task DiscoverAsync(
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        if (!RequireGit())
        {
            return;
        }

        var root = _folderPicker.PickFolder(
            "Choose a folder to search for repositories");

        if (root is null)
        {
            return;
        }

        var operation = BeginOperation(cancellationToken);
        IsBusy = true;
        StatusText = $"Searching for repositories under '{root}'…";

        try
        {
            var found = await _discovery.DiscoverAsync(
                root, 3, operation.Token);

            if (found.Count == 0)
            {
                StatusText = $"No Git repositories found under '{root}'.";
                return;
            }

            var alreadyTracked = Repositories
                .Select(r => r.DetailsPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Dialog is synchronous UI: run it on the UI thread before any
            // further awaits so cancellation during the scan (not the dialog)
            // is what the token governs.
            var selected = _discoveryDialog.PickRepositoriesToAdd(
                found, alreadyTracked);

            if (selected is null)
            {
                StatusText = "Discovery cancelled.";
                return;
            }

            if (selected.Count == 0)
            {
                StatusText = "No repositories selected.";
                return;
            }

            var added = 0;
            var failed = 0;

            foreach (var candidate in selected)
            {
                operation.Token.ThrowIfCancellationRequested();

                try
                {
                    var item = await _dashboard.AddAsync(
                        candidate, operation.Token);
                    Repositories.Add(new RepositoryRowViewModel(item));
                    added++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    StatusText = $"Could not add '{candidate}': {ex.Message}";
                }
            }

            if (Repositories.Count > 0)
            {
                SelectedRepository = Repositories[^1];
            }

            StatusText = failed == 0
                ? $"Added {added} repositories."
                : $"Added {added} repositories, {failed} failed.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Discovery cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not discover repositories: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            EndOperation(operation);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync(
        RepositoryRowViewModel? target,
        CancellationToken cancellationToken)
    {
        var selected = target ?? SelectedRepository;

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

        var operation = BeginOperation(cancellationToken);
        IsBusy = true;

        try
        {
            await _dashboard.RemoveAsync(
                selected.RepositoryId, operation.Token);

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
            EndOperation(operation);
        }
    }

    /// <summary>
    /// Recalculates time-derived text on every row without touching Git or
    /// operation state. Invoked periodically from the view so fetch age and
    /// the stale indicator evolve while the application sits open.
    /// </summary>
    public void RefreshTimeDisplays()
    {
        foreach (var row in Repositories)
        {
            row.RefreshTimeDisplay();
        }
    }

    /// <summary>
    /// After a successful fetch/refresh the item carries no failure or
    /// update outcome, so <see cref="RepositoryRowViewModel.Update"/>
    /// leaves the row <c>Idle</c>: record the explicit completed state
    /// here instead of leaving a blank Activity cell. Rows reporting a
    /// failure keep their terminal state.
    /// </summary>
    private static void MarkCompletedWhenQuiet(
        RepositoryRowViewModel row, string completedText)
    {
        if (row.Activity == RepositoryActivity.Idle)
        {
            row.SetActivity(RepositoryActivity.Completed, completedText);
        }
    }

    /// <summary>
    /// Applies batch items without removing rows: cancelled batches are
    /// partial by design, and missing rows mean "did not finish", not
    /// "was removed". Non-completed rows keep their in-progress activity
    /// so the caller can reset them to idle afterwards.
    /// </summary>
    private void ApplyBatchItems(
        IReadOnlyList<RepositoryDashboardItem> completed,
        string? batchCompletedText = null)
    {
        foreach (var item in completed)
        {
            var existing = Repositories.FirstOrDefault(
                r => r.RepositoryId == item.Configuration.Id);

            RepositoryRowViewModel row;

            if (existing is null)
            {
                row = new RepositoryRowViewModel(item);
                Repositories.Add(row);
            }
            else
            {
                existing.Update(item);
                row = existing;
            }

            if (batchCompletedText is not null)
            {
                MarkCompletedWhenQuiet(row, batchCompletedText);
            }
        }
    }

    private void SyncRows(
        IReadOnlyList<RepositoryDashboardItem> repositories,
        string? batchCompletedText = null)
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

            RepositoryRowViewModel row;

            if (existing is null)
            {
                row = new RepositoryRowViewModel(item);
                Repositories.Add(row);
            }
            else
            {
                existing.Update(item);
                row = existing;
            }

            if (batchCompletedText is not null)
            {
                MarkCompletedWhenQuiet(row, batchCompletedText);
            }
        }
    }
}
