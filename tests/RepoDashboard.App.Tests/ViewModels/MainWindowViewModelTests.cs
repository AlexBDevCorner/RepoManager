using FluentAssertions;
using RepoDashboard.App.Services;
using RepoDashboard.App.ViewModels;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.App.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    private sealed class FakeGitEnvironment(bool available = true) : IGitEnvironment
    {
        public Task<GitEnvironmentInfo> CheckAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(available
                ? new GitEnvironmentInfo(true, "2.47.0", null)
                : new GitEnvironmentInfo(
                    false, null, "Git could not be found on PATH."));
    }

    private sealed class FakeDashboard : IRepositoryDashboardService
    {
        private readonly List<RepositoryDashboardItem> _items;

        public int LoadCalls { get; private set; }

        public int RefreshCalls { get; private set; }

        public int RefreshAllCalls { get; private set; }

        public int AddCalls { get; private set; }

        /// <summary>
        /// When true, every method throws: proves the ViewModel never
        /// reaches the dashboard (for example when Git is unavailable).
        /// </summary>
        public bool FailOnCall { get; set; }

        public FakeDashboard(IEnumerable<RepositoryDashboardItem>? items = null)
        {
            _items = items?.ToList() ?? [];
        }

        private void ThrowIfStrict()
        {
            if (FailOnCall)
            {
                throw new InvalidOperationException(
                    "Dashboard must not be called.");
            }
        }

        public Task<IReadOnlyList<RepositoryDashboardItem>> LoadAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfStrict();
            LoadCalls++;
            return Task.FromResult<IReadOnlyList<RepositoryDashboardItem>>(
                _items.ToList());
        }

        public Task<RepositoryDashboardItem> RefreshAsync(
            Guid repositoryId,
            CancellationToken cancellationToken)
        {
            ThrowIfStrict();
            RefreshCalls++;
            return Task.FromResult(_items.First(i => i.Configuration.Id == repositoryId));
        }

        public Task<IReadOnlyList<RepositoryDashboardItem>> RefreshAllAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfStrict();
            RefreshAllCalls++;
            return Task.FromResult<IReadOnlyList<RepositoryDashboardItem>>(
                _items.ToList());
        }

        public Task<RepositoryDashboardItem> FetchAsync(
            Guid repositoryId,
            CancellationToken cancellationToken)
        {
            ThrowIfStrict();
            return Task.FromResult(_items.First(i => i.Configuration.Id == repositoryId));
        }

        public Task<RepositoryBatchResult> FetchAllAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfStrict();
            return Task.FromResult(RepositoryBatchResult.Completed(_items.ToList()));
        }

        public Task<RepositoryDashboardItem> UpdateAsync(
            Guid repositoryId,
            CancellationToken cancellationToken)
        {
            ThrowIfStrict();
            return Task.FromResult(_items.First(i => i.Configuration.Id == repositoryId));
        }

        public Task<RepositoryBatchResult> UpdateAllAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfStrict();
            return Task.FromResult(RepositoryBatchResult.Completed(_items.ToList()));
        }

        public Task<RepositoryDashboardItem> AddAsync(
            string path,
            CancellationToken cancellationToken)
        {
            ThrowIfStrict();
            AddCalls++;
            throw new NotSupportedException();
        }

        public Task RemoveAsync(
            Guid repositoryId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CancelledPicker : IFolderPickerService
    {
        public string? PickFolder(string title) => null;
    }

    private static RepositoryDashboardItem Item(string name)
    {
        var configuration = new RepositoryConfiguration
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = $"""C:\Source\Repos\{name}"""
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

    private static RepositoryDashboardItem FailedItem(string name)
    {
        var configuration = new RepositoryConfiguration
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = $"""C:\Source\Repos\{name}"""
        };

        const string error = "git status unexpectedly failed";

        return new RepositoryDashboardItem
        {
            Configuration = configuration,
            Snapshot = new RepositorySnapshot
            {
                RepositoryId = configuration.Id,
                Path = configuration.Path,
                InspectedAt = DateTimeOffset.UtcNow
            },
            UpdateDecision = new UpdateDecision(
                UpdateEligibility.Unknown, error),
            InspectionError = error
        };
    }

    [Fact]
    public async Task Initialize_loads_repositories_through_dashboard()
    {
        var dashboard = new FakeDashboard([Item("Store"), Item("Legacy")]);
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());

        await sut.InitializeAsync();

        sut.IsGitAvailable.Should().BeTrue();
        sut.Repositories.Select(r => r.Name)
            .Should().BeEquivalentTo("Store", "Legacy");
        dashboard.LoadCalls.Should().Be(1);
    }

    [Fact]
    public async Task RefreshAll_syncs_rows_without_rebuilding_selection()
    {
        var dashboard = new FakeDashboard([Item("Store")]);
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());
        await sut.InitializeAsync();
        sut.SelectedRepository = sut.Repositories[0];

        await sut.RefreshAllCommand.ExecuteAsync(null);

        sut.Repositories.Should().ContainSingle();
        sut.SelectedRepository.Should().Be(sut.Repositories[0]);
        dashboard.RefreshAllCalls.Should().Be(1);
    }

    [Fact]
    public async Task Add_cancelled_by_user_does_nothing()
    {
        var dashboard = new FakeDashboard();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());
        await sut.InitializeAsync();

        await sut.AddCommand.ExecuteAsync(null);

        sut.Repositories.Should().BeEmpty();
    }

    [Fact]
    public async Task Initialize_git_unavailable_does_not_touch_dashboard()
    {
        var dashboard = new FakeDashboard { FailOnCall = true };
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(available: false),
            dashboard,
            new CancelledPicker());

        await sut.InitializeAsync();

        sut.IsGitAvailable.Should().BeFalse();
        sut.Repositories.Should().BeEmpty();
        sut.StatusText.Should().Contain("until Git is installed");
        dashboard.LoadCalls.Should().Be(0);
    }

    [Fact]
    public async Task Git_unavailable_disables_git_commands_but_not_remove()
    {
        var dashboard = new FakeDashboard { FailOnCall = true };
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(available: false),
            dashboard,
            new CancelledPicker());
        await sut.InitializeAsync();

        sut.LoadCommand.CanExecute(null).Should().BeFalse();
        sut.AddCommand.CanExecute(null).Should().BeFalse();
        sut.RefreshCommand.CanExecute(null).Should().BeFalse();
        sut.RefreshAllCommand.CanExecute(null).Should().BeFalse();

        // Direct execution (which bypasses CanExecute) still cannot
        // reach the dashboard without Git.
        sut.SelectedRepository = new RepositoryRowViewModel(Item("Store"));
        await sut.RefreshCommand.ExecuteAsync(null);
        await sut.RefreshAllCommand.ExecuteAsync(null);
        await sut.AddCommand.ExecuteAsync(null);
        dashboard.RefreshCalls.Should().Be(0);
        dashboard.RefreshAllCalls.Should().Be(0);
        dashboard.AddCalls.Should().Be(0);

        // Remove edits only configuration, so it stays available.
        sut.RemoveCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task RefreshAll_reports_partial_failure_count()
    {
        var dashboard = new FakeDashboard([Item("Store"), FailedItem("Broken")]);
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());
        await sut.InitializeAsync();

        await sut.RefreshAllCommand.ExecuteAsync(null);

        sut.StatusText.Should().Be(
            "Refreshed 1 of 2 repositories. 1 failed.");
    }

    private static RepositoryDashboardItem WithUpdate(
        RepositoryDashboardItem item,
        RepositoryUpdateOutcome outcome,
        UpdateEligibility reason,
        string message) =>
        item with
        {
            UpdateDecision = new UpdateDecision(reason, message),
            UpdateResult = new RepositoryUpdateResult
            {
                RepositoryId = item.Configuration.Id,
                Outcome = outcome,
                Message = message,
                Decision = new UpdateDecision(reason, message),
                FetchResult = new RepositoryOperationResult
                {
                    Success = true,
                    Operation = RepositoryOperationType.Fetch,
                    Message = "Fetched 'origin' (pruned).",
                    Duration = TimeSpan.Zero
                },
                FinalSnapshot = item.Snapshot
            }
        };

    [Fact]
    public async Task FetchAll_reports_aggregate_summary()
    {
        var fetched = Item("Store");
        var failed = Item("Broken") with
        {
            FetchError = "git fetch origin --prune failed with exit code 128: boom"
        };
        var dashboard = new FakeDashboard([fetched, failed]);
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());
        await sut.InitializeAsync();

        await sut.FetchAllCommand.ExecuteAsync(null);

        sut.StatusText.Should().Be(
            "Fetch complete: 2 repositories, 1 successful, 1 failed.");
        sut.Repositories.Should().HaveCount(2);
        sut.Repositories[1].Activity.Should().Be(RepositoryActivity.Failed);
        sut.Repositories[1].ActivityText.Should().Contain("Fetch failed");
    }

    [Fact]
    public async Task UpdateAll_reports_per_reason_breakdown()
    {
        var updated = WithUpdate(
            Item("FileStore"),
            RepositoryUpdateOutcome.Updated,
            UpdateEligibility.CanFastForward,
            "Fast-forwarded 'main'.");
        var current = Item("Viewer");
        var dirty = WithUpdate(
            Item("Search"),
            RepositoryUpdateOutcome.Skipped,
            UpdateEligibility.Dirty,
            "The working tree contains uncommitted changes.");
        var diverged = WithUpdate(
            Item("Legacy"),
            RepositoryUpdateOutcome.Skipped,
            UpdateEligibility.Diverged,
            "Local and remote branches have diverged.");
        var dashboard = new FakeDashboard([updated, current, dirty, diverged]);
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());
        await sut.InitializeAsync();

        await sut.UpdateAllCommand.ExecuteAsync(null);

        sut.StatusText.Should().Be(
            "Update complete: 4 repositories, 1 updated, " +
            "1 already current, 1 dirty, 1 diverged.");
        sut.Repositories[0].Activity.Should().Be(RepositoryActivity.Completed);
        sut.Repositories[2].Activity.Should().Be(RepositoryActivity.Skipped);
    }

    [Fact]
    public async Task Selecting_repository_exposes_details()
    {
        var dashboard = new FakeDashboard([Item("Store")]);
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());
        await sut.InitializeAsync();

        sut.HasSelection.Should().BeFalse();
        sut.SelectedRepository = sut.Repositories[0];

        sut.HasSelection.Should().BeTrue();
        sut.SelectedRepository!.DetailsPath.Should()
            .Be("""C:\Source\Repos\Store""");
        sut.SelectedRepository.DetailsRemote.Should().Be("origin");
    }

    [Fact]
    public async Task Fetch_marks_successful_row_completed()
    {
        var dashboard = new FakeDashboard([Item("Store")]);
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());
        await sut.InitializeAsync();
        sut.SelectedRepository = sut.Repositories[0];

        await sut.FetchCommand.ExecuteAsync(null);

        sut.Repositories[0].Activity.Should().Be(RepositoryActivity.Completed);
        sut.Repositories[0].ActivityText.Should().Be("Fetched");
        sut.StatusText.Should().Be("Fetched 'Store'.");
    }

    [Fact]
    public async Task Fetch_with_failed_reinspection_reports_both()
    {
        // The fetch itself succeeded (timestamp-worthy) but the mandatory
        // re-inspection threw: the row must not claim a clean fetch.
        var failed = FailedItem("Broken") with
        {
            LastOperation = RepositoryOperationType.Fetch
        };
        var dashboard = new FakeDashboard([failed]);
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());
        await sut.InitializeAsync();
        sut.SelectedRepository = sut.Repositories[0];

        await sut.FetchCommand.ExecuteAsync(null);

        sut.StatusText.Should().Be(
            "Fetched 'Broken', but refreshing repository status failed: " +
            "git status unexpectedly failed");
        sut.Repositories[0].Activity.Should().Be(RepositoryActivity.Failed);
        sut.Repositories[0].DetailsLastOperation.Should().Be("Inspection failed");
    }

    [Fact]
    public async Task Refresh_marks_successful_row_completed()
    {
        var dashboard = new FakeDashboard([Item("Store")]);
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());
        await sut.InitializeAsync();
        sut.SelectedRepository = sut.Repositories[0];

        await sut.RefreshCommand.ExecuteAsync(null);

        sut.Repositories[0].Activity.Should().Be(RepositoryActivity.Completed);
        sut.Repositories[0].ActivityText.Should().Be("Refreshed");
    }

    [Fact]
    public async Task Row_command_acts_on_explicit_target_not_selection()
    {
        var dashboard = new FakeDashboard([Item("Store"), Item("Legacy")]);
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());
        await sut.InitializeAsync();
        sut.SelectedRepository = sut.Repositories[0];
        var target = sut.Repositories[1];

        await sut.RefreshCommand.ExecuteAsync(target);

        sut.StatusText.Should().Be("Refreshed 'Legacy'.");
        target.Activity.Should().Be(RepositoryActivity.Completed);
        target.ActivityText.Should().Be("Refreshed");
        // The untouched selection keeps no completed state.
        sut.Repositories[0].Activity.Should().Be(RepositoryActivity.Idle);
    }

    [Fact]
    public void Remove_can_execute_accepts_explicit_target()
    {
        var dashboard = new FakeDashboard([Item("Store")]);
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());
        var target = new RepositoryRowViewModel(Item("Store"));

        sut.RemoveCommand.CanExecute(null).Should().BeFalse();
        sut.RemoveCommand.CanExecute(target).Should().BeTrue();
        sut.RefreshCommand.CanExecute(target).Should().BeFalse(
            "Git is unavailable until InitializeAsync runs");
    }

    [Fact]
    public async Task RefreshAll_keeps_failed_row_with_error_and_selection()
    {
        var dashboard = new FakeDashboard([Item("Store"), FailedItem("Broken")]);
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(), dashboard, new CancelledPicker());
        await sut.InitializeAsync();
        sut.SelectedRepository = sut.Repositories[0];

        await sut.RefreshAllCommand.ExecuteAsync(null);

        sut.Repositories.Should().HaveCount(2);
        sut.Repositories[0].UpdateStatus
            .Should().Be(nameof(UpdateEligibility.AlreadyUpToDate));
        sut.Repositories[1].Name.Should().Be("Broken");
        sut.Repositories[1].WorktreeStatus.Should().Be("Error");
        sut.Repositories[1].UpdateStatus
            .Should().Be(nameof(UpdateEligibility.Unknown));
        sut.Repositories[1].Explanation
            .Should().Contain("git status unexpectedly failed");
        sut.SelectedRepository.Should().Be(sut.Repositories[0]);
    }
}
