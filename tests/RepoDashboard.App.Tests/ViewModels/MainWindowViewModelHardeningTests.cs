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

        public int RenameCalls { get; private set; }

        public int MoveCalls { get; private set; }

        public bool FailMove { get; set; }

        /// <summary>
        /// Returns an exception to throw for a path, or null to succeed.
        /// </summary>
        public Func<string, Exception?> AddFailureFor { get; set; } = _ => null;

        /// <summary>
        /// Invoked with the 1-based call number on every add attempt.
        /// </summary>
        public Action<int>? OnAdd { get; set; }

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

        public Task<IReadOnlyList<RepositoryConfiguration>> LoadConfigurationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RepositoryConfiguration>>(
                _items.Select(i => i.Configuration).ToList());

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
            OnAdd?.Invoke(AddCalls);

            var failure = AddFailureFor(path);

            if (failure is not null)
            {
                throw failure;
            }

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

        public Task<RepositoryConfiguration> RenameAsync(
            Guid repositoryId,
            string name,
            CancellationToken cancellationToken)
        {
            RenameCalls++;

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "Repository name must not be empty.", nameof(name));
            }

            var index = _items.FindIndex(
                i => i.Configuration.Id == repositoryId);

            if (index < 0)
            {
                throw new KeyNotFoundException(
                    $"Repository '{repositoryId}' is not on the dashboard.");
            }

            var updated = _items[index].Configuration with { Name = name.Trim() };
            _items[index] = _items[index] with { Configuration = updated };
            return Task.FromResult(updated);
        }

        public Task MoveAsync(
            Guid repositoryId,
            int newIndex,
            CancellationToken cancellationToken)
        {
            MoveCalls++;

            if (FailMove)
            {
                throw new InvalidOperationException("move boom");
            }

            var current = _items.FindIndex(
                i => i.Configuration.Id == repositoryId);

            if (current < 0)
            {
                throw new KeyNotFoundException(
                    $"Repository '{repositoryId}' is not on the dashboard.");
            }

            if (newIndex < 0 || newIndex >= _items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(newIndex));
            }

            if (current == newIndex)
            {
                return Task.CompletedTask;
            }

            var item = _items[current];
            _items.RemoveAt(current);
            _items.Insert(newIndex, item);
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

        public IReadOnlyList<string>? PickFolders(string title) => null;
    }

    private sealed class FixedPicker(string? path) : IFolderPickerService
    {
        public string? PickFolder(string title) => path;

        public IReadOnlyList<string>? PickFolders(string title) =>
            path is null ? null : [path];
    }

    private sealed class FixedMultiPicker(IReadOnlyList<string>? paths) : IFolderPickerService
    {
        public string? PickFolder(string title) => paths?.FirstOrDefault();

        public IReadOnlyList<string>? PickFolders(string title) => paths;
    }

    /// <summary>
    /// Proves discovery uses the single-folder picker for its search root:
    /// multi-select must never be consulted here.
    /// </summary>
    private sealed class DiscoverRootPicker(string root) : IFolderPickerService
    {
        public string? PickFolder(string title) => root;

        public IReadOnlyList<string>? PickFolders(string title) =>
            throw new InvalidOperationException(
                "Discovery must use the single-folder picker.");
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

        public Task<IReadOnlyList<RepositoryConfiguration>> LoadConfigurationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RepositoryConfiguration>>(
                [completed.Configuration]);

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

        public Task<RepositoryConfiguration> RenameAsync(
            Guid repositoryId,
            string name,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MoveAsync(
            Guid repositoryId,
            int newIndex,
            CancellationToken cancellationToken) =>
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

    [Fact]
    public async Task Add_adds_multiple_repositories_in_selection_order()
    {
        var dashboard = new FakeDashboard();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard,
            new FixedMultiPicker(
            [
                @"C:\Source\Repos\RepoA",
                @"C:\Source\Repos\RepoB",
                @"C:\Source\Repos\RepoC"
            ]));
        await sut.InitializeAsync();

        await sut.AddCommand.ExecuteAsync(null);

        dashboard.AddCalls.Should().Be(3);
        sut.Repositories.Select(r => r.Name)
            .Should().Equal("RepoA", "RepoB", "RepoC");
        sut.SelectedRepository.Should().Be(sut.Repositories[^1]);
        sut.StatusText.Should().Be("Added 3 repositories.");
        sut.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Add_single_folder_keeps_legacy_status_wording()
    {
        var dashboard = new FakeDashboard();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard,
            new FixedMultiPicker([@"C:\Source\Repos\RepoManager"]));
        await sut.InitializeAsync();

        await sut.AddCommand.ExecuteAsync(null);

        dashboard.AddCalls.Should().Be(1);
        sut.Repositories.Should().ContainSingle();
        sut.StatusText.Should().Be("Added 'RepoManager'.");
    }

    [Fact]
    public async Task Add_picker_cancelled_calls_nothing_and_stays_idle()
    {
        var dashboard = new FakeDashboard();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());
        await sut.InitializeAsync();

        await sut.AddCommand.ExecuteAsync(null);

        dashboard.AddCalls.Should().Be(0);
        sut.Repositories.Should().BeEmpty();
        sut.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Add_empty_selection_is_treated_like_cancellation()
    {
        var dashboard = new FakeDashboard();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard,
            new FixedMultiPicker([]));
        await sut.InitializeAsync();

        await sut.AddCommand.ExecuteAsync(null);

        dashboard.AddCalls.Should().Be(0);
        sut.Repositories.Should().BeEmpty();
        sut.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Add_partial_failure_keeps_valid_repositories()
    {
        var dashboard = new FakeDashboard
        {
            AddFailureFor = path => path.EndsWith("RepoB")
                ? new InvalidOperationException(
                    "'C:\\Source\\Repos\\RepoB' is already on the dashboard as 'RepoB'.")
                : null
        };
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard,
            new FixedMultiPicker(
            [
                @"C:\Source\Repos\RepoA",
                @"C:\Source\Repos\RepoB",
                @"C:\Source\Repos\RepoC"
            ]));
        await sut.InitializeAsync();

        await sut.AddCommand.ExecuteAsync(null);

        dashboard.AddCalls.Should().Be(3);
        sut.Repositories.Select(r => r.Name)
            .Should().Equal("RepoA", "RepoC");
        sut.StatusText.Should().Be(
            "Added 2 of 3 repositories. 1 could not be added.");
        sut.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Add_duplicate_paths_are_attempted_once()
    {
        var dashboard = new FakeDashboard();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard,
            new FixedMultiPicker(
            [
                @"C:\Source\Repos\RepoA",
                @"C:\Source\Repos\RepoA"
            ]));
        await sut.InitializeAsync();

        await sut.AddCommand.ExecuteAsync(null);

        dashboard.AddCalls.Should().Be(1);
        sut.Repositories.Should().ContainSingle();
        sut.StatusText.Should().Be("Added 'RepoA'.");
    }

    [Fact]
    public async Task Add_cancelled_mid_batch_keeps_completed_and_stops()
    {
        var dashboard = new FakeDashboard();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard,
            new FixedMultiPicker(
            [
                @"C:\Source\Repos\RepoA",
                @"C:\Source\Repos\RepoB",
                @"C:\Source\Repos\RepoC"
            ]));
        await sut.InitializeAsync();
        dashboard.OnAdd = call =>
        {
            if (call == 2)
            {
                sut.CancelActiveOperation();
            }
        };

        await sut.AddCommand.ExecuteAsync(null);

        // The third repository never started: cancelled before its attempt.
        dashboard.AddCalls.Should().Be(2);
        sut.Repositories.Select(r => r.Name)
            .Should().Equal("RepoA", "RepoB");
        sut.StatusText.Should().Be("Adding repositories cancelled.");
        sut.IsBusy.Should().BeFalse();
        sut.CancelCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Discover_still_uses_single_folder_picker_for_root()
    {
        var dashboard = new FakeDashboard();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard,
            new DiscoverRootPicker(@"C:\Source\Repos"),
            new FakeDiscovery([Discovered("Store")]),
            new FakeDialog([@"C:\Source\Repos\Store"]));
        await sut.InitializeAsync();

        await sut.DiscoverCommand.ExecuteAsync(null);

        sut.Repositories.Select(r => r.Name)
            .Should().BeEquivalentTo("Store");
        dashboard.AddCalls.Should().Be(1);
    }

    private static async Task<MainWindowViewModel> SeededWithAsync(
        FakeDashboard dashboard,
        params string[] names)
    {
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard,
            new FixedMultiPicker(
                names.Select(n => $"""C:\Source\Repos\{n}""").ToList()));
        await sut.InitializeAsync();
        await sut.AddCommand.ExecuteAsync(null);
        return sut;
    }

    [Fact]
    public async Task MoveUp_reorders_and_keeps_selection()
    {
        var dashboard = new FakeDashboard();
        var sut = await SeededWithAsync(dashboard, "A", "B", "C");
        sut.SelectedRepository = sut.Repositories[1];

        await sut.MoveUpCommand.ExecuteAsync(null);

        sut.Repositories.Select(r => r.Name)
            .Should().Equal("B", "A", "C");
        sut.SelectedRepository!.Name.Should().Be("B");
        sut.StatusText.Should().Be("Moved 'B' up.");
        sut.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task MoveDown_reorders_and_keeps_selection()
    {
        var dashboard = new FakeDashboard();
        var sut = await SeededWithAsync(dashboard, "A", "B", "C");
        sut.SelectedRepository = sut.Repositories[1];

        await sut.MoveDownCommand.ExecuteAsync(null);

        sut.Repositories.Select(r => r.Name)
            .Should().Equal("A", "C", "B");
        sut.SelectedRepository!.Name.Should().Be("B");
        sut.StatusText.Should().Be("Moved 'B' down.");
    }

    [Fact]
    public async Task MoveDown_with_explicit_target_uses_target_not_selection()
    {
        var dashboard = new FakeDashboard();
        var sut = await SeededWithAsync(dashboard, "A", "B", "C");
        sut.SelectedRepository = sut.Repositories[0];
        var target = sut.Repositories[1];

        await sut.MoveDownCommand.ExecuteAsync(target);

        sut.Repositories.Select(r => r.Name)
            .Should().Equal("A", "C", "B");
        sut.SelectedRepository!.Name.Should().Be("B");
    }

    [Fact]
    public async Task Move_boundary_commands_reflect_position()
    {
        var dashboard = new FakeDashboard();
        var sut = await SeededWithAsync(dashboard, "A", "B", "C");

        sut.SelectedRepository = sut.Repositories[0];
        sut.MoveUpCommand.CanExecute(null).Should().BeFalse();
        sut.MoveUpCommand.CanExecute(sut.Repositories[0]).Should().BeFalse();
        sut.MoveDownCommand.CanExecute(null).Should().BeTrue();

        sut.SelectedRepository = sut.Repositories[2];
        sut.MoveUpCommand.CanExecute(null).Should().BeTrue();
        sut.MoveDownCommand.CanExecute(null).Should().BeFalse();
        sut.MoveDownCommand.CanExecute(sut.Repositories[2]).Should().BeFalse();
    }

    [Fact]
    public async Task Move_with_single_repository_is_disabled_both_ways()
    {
        var dashboard = new FakeDashboard();
        var sut = await SeededWithAsync(dashboard, "Only");
        sut.SelectedRepository = sut.Repositories[0];

        sut.MoveUpCommand.CanExecute(null).Should().BeFalse();
        sut.MoveDownCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Move_failure_leaves_visible_order_and_selection()
    {
        var dashboard = new FakeDashboard { FailMove = true };
        var sut = await SeededWithAsync(dashboard, "A", "B", "C");
        var selected = sut.Repositories[1];
        sut.SelectedRepository = selected;

        await sut.MoveUpCommand.ExecuteAsync(null);

        sut.Repositories.Select(r => r.Name)
            .Should().Equal("A", "B", "C");
        sut.SelectedRepository.Should().Be(selected);
        sut.StatusText.Should().Contain("Could not move 'B'");
        sut.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAll_preserves_custom_order_regardless_of_return_order()
    {
        var dashboard = new FakeDashboard();
        var sut = await SeededWithAsync(dashboard, "Store", "Search", "RepoManager");

        // Custom order: Search first.
        sut.SelectedRepository = sut.Repositories[1];
        await sut.MoveUpCommand.ExecuteAsync(null);
        sut.Repositories.Select(r => r.Name)
            .Should().Equal("Search", "Store", "RepoManager");

        // The service returns items in a different (persisted) order —
        // rows must still follow the visible custom order, keyed by id.
        await dashboard.MoveAsync(
            sut.Repositories[2].RepositoryId, 0, CancellationToken.None);

        await sut.RefreshAllCommand.ExecuteAsync(null);

        sut.Repositories.Select(r => r.Name)
            .Should().Equal("Search", "Store", "RepoManager");
        sut.SelectedRepository!.Name.Should().Be("Search");
    }
}
