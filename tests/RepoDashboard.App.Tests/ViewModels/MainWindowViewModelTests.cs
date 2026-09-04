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
