using FluentAssertions;
using RepoDashboard.App.Services;
using RepoDashboard.App.ViewModels;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Discovery;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.App.Tests.ViewModels;

/// <summary>
/// Task 40/43/46: discovery flow, cancellation affordance, and friendly-hint
/// mapping. No XAML is exercised — only view-model behavior.
/// </summary>
public sealed class MainWindowViewModelHardeningTests
{
    private sealed class FakeGitEnvironment(bool available = true) : IGitEnvironment
    {
        public Task<GitEnvironmentInfo> CheckAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(available
                ? new GitEnvironmentInfo(true, "2.47.0", null)
                : new GitEnvironmentInfo(false, null, "Git missing"));
    }

    private sealed class FakeDashboard : IRepositoryDashboardService
    {
        private readonly List<RepositoryDashboardItem> _items = [];

        public int AddCalls { get; private set; }

        public static RepositoryDashboardItem ItemFor(string path)
        {
            var configuration = new RepositoryConfiguration
            {
                Id = Guid.NewGuid(),
                Name = new DirectoryInfo(path).Name,
                Path = path
            };

            return new RepositoryDashboardItem
            {
                Configuration = configuration,
                Snapshot = new RepositorySnapshot
                {
                    RepositoryId = configuration.Id,
                    Path = configuration.Path,
                    DirectoryExists = true,
                    IsGitRepository = true,
                    CurrentBranch = "main",
                    UpstreamRef = "origin/main",
                    UpstreamRemote = "origin",
                    UpstreamBranch = "main",
                    UpstreamDivergence = new Divergence(0, 0),
                    InspectedAt = DateTimeOffset.UtcNow
                },
                UpdateDecision = new UpdateDecision(
                    UpdateEligibility.AlreadyUpToDate, "up to date")
            };
        }

        public Task<IReadOnlyList<RepositoryDashboardItem>> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RepositoryDashboardItem>>(
                _items.ToList());

        public Task<RepositoryDashboardItem> RefreshAsync(
            Guid repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.First(i => i.Configuration.Id == repositoryId));

        public Task<IReadOnlyList<RepositoryDashboardItem>> RefreshAllAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RepositoryDashboardItem>>(
                _items.ToList());

        public Task<RepositoryDashboardItem> FetchAsync(
            Guid repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.First(i => i.Configuration.Id == repositoryId));

        public Task<RepositoryBatchResult> FetchAllAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(RepositoryBatchResult.Completed(_items.ToList()));

        public Task<RepositoryDashboardItem> UpdateAsync(
            Guid repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.First(i => i.Configuration.Id == repositoryId));

        public Task<RepositoryBatchResult> UpdateAllAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(RepositoryBatchResult.Completed(_items.ToList()));

        public Task<RepositoryDashboardItem> AddAsync(
            string path, CancellationToken cancellationToken)
        {
            AddCalls++;
            var item = ItemFor(Path.GetFullPath(path));
            _items.Add(item);
            return Task.FromResult(item);
        }

        public Task RemoveAsync(
            Guid repositoryId, CancellationToken cancellationToken)
        {
            _items.RemoveAll(i => i.Configuration.Id == repositoryId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDiscovery(
        IReadOnlyList<DiscoveredRepository> found) : IRepositoryDiscoveryService
    {
        public Task<IReadOnlyList<DiscoveredRepository>> DiscoverAsync(
            string rootPath, int maxDepth = 3,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            maxDepth.Should().Be(3);
            return Task.FromResult(found);
        }
    }

    private sealed class FakeDialog(IReadOnlyList<string>? selection)
        : IDiscoveryDialogService
    {
        public IReadOnlyList<string>? PickRepositoriesToAdd(
            IReadOnlyList<DiscoveredRepository> candidates,
            ISet<string> alreadyTrackedPaths) => selection;
    }

    private sealed class CancelledPicker : IFolderPickerService
    {
        public string? PickFolder(string title) => null;
    }

    private sealed class FixedPicker(string? path) : IFolderPickerService
    {
        public string? PickFolder(string title) => path;
    }

    /// <summary>
    /// Discovery that stays running until cancelled: proves Cancel actually
    /// interrupts the scan (review: discovery must not block the UI thread).
    /// </summary>
    private sealed class BlockingDiscovery : IRepositoryDiscoveryService
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<DiscoveredRepository>> DiscoverAsync(
            string rootPath, int maxDepth = 3,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return DiscoverBlockedAsync(cancellationToken);
        }

        private static async Task<IReadOnlyList<DiscoveredRepository>> DiscoverBlockedAsync(
            CancellationToken cancellationToken)
        {
            // Never completes on its own — only via cancellation.
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }
    }

    private sealed class UnreachableDialog : IDiscoveryDialogService
    {
        public IReadOnlyList<string>? PickRepositoriesToAdd(
            IReadOnlyList<DiscoveredRepository> candidates,
            ISet<string> alreadyTrackedPaths) =>
            throw new InvalidOperationException(
                "Dialog must not be reached when discovery is cancelled.");
    }

    private static DiscoveredRepository Discovered(string name) =>
        new() { Path = $"""C:\Source\Repos\{name}""", Name = name };

    [Fact]
    public async Task Discover_adds_selected_repositories()
    {
        var dashboard = new FakeDashboard();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new FixedPicker(@"C:\Source\Repos"),
            new FakeDiscovery([Discovered("Store"), Discovered("Viewer")]),
            new FakeDialog([@"C:\Source\Repos\Store"]));
        await sut.InitializeAsync();

        await sut.DiscoverCommand.ExecuteAsync(null);

        sut.Repositories.Select(r => r.Name)
            .Should().BeEquivalentTo("Store");
        dashboard.AddCalls.Should().Be(1);
        sut.StatusText.Should().Be("Added 1 repositories.");
    }

    [Fact]
    public async Task Discover_no_repositories_found_reports_clearly()
    {
        var dashboard = new FakeDashboard();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new FixedPicker(@"C:\Source\Repos"),
            new FakeDiscovery([]),
            new FakeDialog([]));
        await sut.InitializeAsync();

        await sut.DiscoverCommand.ExecuteAsync(null);

        sut.Repositories.Should().BeEmpty();
        sut.StatusText.Should().Contain("No Git repositories found");
        dashboard.AddCalls.Should().Be(0);
    }

    [Fact]
    public async Task Discover_cancelled_in_dialog_adds_nothing()
    {
        var dashboard = new FakeDashboard();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new FixedPicker(@"C:\Source\Repos"),
            new FakeDiscovery([Discovered("Store")]),
            new FakeDialog(null));
        await sut.InitializeAsync();

        await sut.DiscoverCommand.ExecuteAsync(null);

        sut.Repositories.Should().BeEmpty();
        sut.StatusText.Should().Be("Discovery cancelled.");
    }

    [Fact]
    public async Task Discover_picker_cancelled_does_nothing()
    {
        var dashboard = new FakeDashboard();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker(),
            new FakeDiscovery([Discovered("Store")]),
            new FakeDialog([@"C:\Source\Repos\Store"]));
        await sut.InitializeAsync();

        await sut.DiscoverCommand.ExecuteAsync(null);

        sut.Repositories.Should().BeEmpty();
        dashboard.AddCalls.Should().Be(0);
    }

    [Fact]
    public async Task Discover_in_flight_cancel_ends_with_cancellation()
    {
        var dashboard = new FakeDashboard();
        var discovery = new BlockingDiscovery();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new FixedPicker(@"C:\Source\Repos"),
            discovery, new UnreachableDialog());
        await sut.InitializeAsync();

        var executeTask = sut.DiscoverCommand.ExecuteAsync(null);

        // Discovery is running on its own task: the UI thread is free and
        // Cancel is available — the exact property the review found missing.
        await discovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        sut.CancelCommand.CanExecute(null).Should().BeTrue();

        sut.CancelActiveOperation();
        await executeTask;

        sut.StatusText.Should().Be("Discovery cancelled.");
        sut.Repositories.Should().BeEmpty();
        dashboard.AddCalls.Should().Be(0);
        sut.CancelCommand.CanExecute(null).Should().BeFalse();
    }

    private sealed class PartialBatchDashboard(
        RepositoryDashboardItem completed) : IRepositoryDashboardService
    {
        public Task<IReadOnlyList<RepositoryDashboardItem>> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RepositoryDashboardItem>>([completed]);

        public Task<RepositoryDashboardItem> RefreshAsync(
            Guid repositoryId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RepositoryDashboardItem>> RefreshAllAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RepositoryDashboardItem> FetchAsync(
            Guid repositoryId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RepositoryBatchResult> FetchAllAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new RepositoryBatchResult
            {
                CompletedItems = [completed],
                WasCancelled = true
            });

        public Task<RepositoryDashboardItem> UpdateAsync(
            Guid repositoryId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RepositoryBatchResult> UpdateAllAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RepositoryDashboardItem> AddAsync(
            string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(
            Guid repositoryId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task FetchAll_cancelled_batch_keeps_completed_row_resets_pending()
    {
        var done = FakeDashboard.ItemFor(@"C:\Source\Repos\Done");
        var pending = FakeDashboard.ItemFor(@"C:\Source\Repos\Pending");
        var dashboard = new PartialBatchDashboard(done);
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());
        await sut.InitializeAsync();

        // Initialize loads the Done row; seed the pending row so one can
        // complete while the other stays in-flight.
        sut.Repositories.Add(new RepositoryRowViewModel(pending));

        await sut.FetchAllCommand.ExecuteAsync(null);

        // Completed row shows its terminal state; the pending row returns
        // to idle instead of being dropped or left spinning.
        sut.Repositories.Should().HaveCount(2);
        sut.Repositories.First(r => r.Name == "Done").Activity
            .Should().Be(RepositoryActivity.Completed);
        sut.Repositories.First(r => r.Name == "Pending").Activity
            .Should().Be(RepositoryActivity.Idle);
        sut.StatusText.Should().Contain("cancelled");
        sut.StatusText.Should().Contain("1");
    }

    [Fact]
    public async Task Cancel_is_disabled_when_idle_and_safe_to_call()
    {
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), new FakeDashboard(), new CancelledPicker());
        await sut.InitializeAsync();

        sut.CancelCommand.CanExecute(null).Should().BeFalse();

        // Safe when no operation is running (shutdown path calls this).
        var act = () => sut.CancelActiveOperation();
        act.Should().NotThrow();

        sut.NotifyShuttingDown();
        sut.CancelCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Discover_requires_git_like_other_mutations()
    {
        var dashboard = new FakeDashboard();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(available: false), dashboard,
            new FixedPicker(@"C:\Source\Repos"),
            new FakeDiscovery([Discovered("Store")]),
            new FakeDialog([@"C:\Source\Repos\Store"]));
        await sut.InitializeAsync();

        sut.DiscoverCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Row_maps_friendly_hint_alongside_raw_error()
    {
        var configuration = new RepositoryConfiguration
        {
            Id = Guid.NewGuid(),
            Name = "Identity",
            Path = @"C:\Source\Repos\Identity"
        };

        const string raw =
            "git fetch origin --prune failed with exit code 128: " +
            "fatal: Authentication failed for 'https://example.invalid/'";
        const string hint =
            "Authentication failed. Check Git Credential Manager or SSH credentials.";

        var row = new RepositoryRowViewModel(new RepositoryDashboardItem
        {
            Configuration = configuration,
            Snapshot = new RepositorySnapshot
            {
                RepositoryId = configuration.Id,
                Path = configuration.Path,
                DirectoryExists = true,
                IsGitRepository = true,
                CurrentBranch = "main",
                InspectedAt = DateTimeOffset.UtcNow
            },
            UpdateDecision = new UpdateDecision(UpdateEligibility.Unknown, raw),
            FetchError = raw,
            FriendlyHint = hint,
            LastOperation = RepositoryOperationType.Fetch
        });

        row.DetailsGitHint.Should().Be(hint);
        row.DetailsGitError.Should().Be(raw);
        row.Activity.Should().Be(RepositoryActivity.Failed);
    }

    [Fact]
    public void Row_without_hint_leaves_hint_empty_but_keeps_error()
    {
        var configuration = new RepositoryConfiguration
        {
            Id = Guid.NewGuid(),
            Name = "Store",
            Path = @"C:\Source\Repos\Store"
        };

        const string raw = "fatal: Not possible to fast-forward to 'abc123'.";

        var row = new RepositoryRowViewModel(new RepositoryDashboardItem
        {
            Configuration = configuration,
            Snapshot = new RepositorySnapshot
            {
                RepositoryId = configuration.Id,
                Path = configuration.Path,
                DirectoryExists = true,
                IsGitRepository = true,
                CurrentBranch = "main",
                InspectedAt = DateTimeOffset.UtcNow
            },
            UpdateDecision = new UpdateDecision(UpdateEligibility.Unknown, raw),
            InspectionError = raw
        });

        row.DetailsGitHint.Should().BeEmpty();
        row.DetailsGitError.Should().Be(raw);
    }
}
