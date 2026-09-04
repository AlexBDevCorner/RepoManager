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
    private sealed class FakeGitEnvironment : IGitEnvironment
    {
        public Task<GitEnvironmentInfo> CheckAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitEnvironmentInfo(true, "2.47.0", null));
    }

    private sealed class FakeDashboard : IRepositoryDashboardService
    {
        private readonly List<RepositoryDashboardItem> _items;

        public int LoadCalls { get; private set; }

        public int RefreshAllCalls { get; private set; }

        public FakeDashboard(IEnumerable<RepositoryDashboardItem>? items = null)
        {
            _items = items?.ToList() ?? [];
        }

        public Task<IReadOnlyList<RepositoryDashboardItem>> LoadAsync(
            CancellationToken cancellationToken)
        {
            LoadCalls++;
            return Task.FromResult<IReadOnlyList<RepositoryDashboardItem>>(
                _items.ToList());
        }

        public Task<RepositoryDashboardItem> RefreshAsync(
            Guid repositoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_items.First(i => i.Configuration.Id == repositoryId));

        public Task<IReadOnlyList<RepositoryDashboardItem>> RefreshAllAsync(
            CancellationToken cancellationToken)
        {
            RefreshAllCalls++;
            return Task.FromResult<IReadOnlyList<RepositoryDashboardItem>>(
                _items.ToList());
        }

        public Task<RepositoryDashboardItem> AddAsync(
            string path,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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
}
