using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using FluentAssertions;
using RepoDashboard.App.Services;
using RepoDashboard.App.ViewModels;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.App.Tests;

/// <summary>
/// RM-009: double-clicking a repository row opens that row's folder through
/// the existing <c>OpenFolderCommand</c> / <c>IFolderLauncher</c> path.
/// Empty-space double-clicks no-op without throwing; selection is untouched.
/// </summary>
[Collection("Headless")]
public sealed class DoubleClickOpenFolderBehaviorTests : IDisposable
{
    private readonly HeadlessUnitTestSession _session;

    public DoubleClickOpenFolderBehaviorTests()
    {
        _session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp));
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    private sealed class FakeGitEnvironment : IGitEnvironment
    {
        public Task<GitEnvironmentInfo> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitEnvironmentInfo(true, "2.47.0", null));
    }

    private sealed class FakeDashboard : IRepositoryDashboardService
    {
        private readonly List<RepositoryDashboardItem> _items;

        public FakeDashboard(IEnumerable<RepositoryDashboardItem>? items = null)
        {
            _items = items?.ToList() ?? [];
        }

        public static RepositoryDashboardItem Item(string name, string path)
        {
            var configuration = new RepositoryConfiguration
            {
                Id = Guid.NewGuid(),
                Name = name,
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
                UpdateDecision = new UpdateDecision(UpdateEligibility.AlreadyUpToDate, "up to date")
            };
        }

        public Task<IReadOnlyList<RepositoryDashboardItem>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RepositoryDashboardItem>>(_items.ToList());

        public Task<IReadOnlyList<RepositoryConfiguration>> LoadConfigurationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RepositoryConfiguration>>(_items.Select(i => i.Configuration).ToList());

        public Task<RepositoryDashboardItem> RefreshAsync(Guid repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.First(i => i.Configuration.Id == repositoryId));

        public Task<IReadOnlyList<RepositoryDashboardItem>> RefreshAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RepositoryDashboardItem>>(_items.ToList());

        public Task<RepositoryDashboardItem> FetchAsync(Guid repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.First(i => i.Configuration.Id == repositoryId));

        public Task<RepositoryBatchResult> FetchAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(RepositoryBatchResult.Completed(_items.ToList()));

        public Task<RepositoryDashboardItem> UpdateAsync(Guid repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.First(i => i.Configuration.Id == repositoryId));

        public Task<RepositoryBatchResult> UpdateAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(RepositoryBatchResult.Completed(_items.ToList()));

        public Task<RepositoryDashboardItem> AddAsync(string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(Guid repositoryId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RepositoryConfiguration> RenameAsync(Guid repositoryId, string name, CancellationToken cancellationToken) =>
            Task.FromResult(_items.First(i => i.Configuration.Id == repositoryId).Configuration);

        public Task MoveAsync(Guid repositoryId, int newIndex, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class CancelledPicker : IFolderPickerService
    {
        public Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>?> PickFoldersAsync(string title, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>?>(null);
    }

    private sealed class FakeFolderLauncher : IFolderLauncher
    {
        public string? LastPath { get; private set; }

        public int Calls { get; private set; }

        public void OpenFolder(string path)
        {
            Calls++;
            LastPath = path;
        }
    }

    private static async Task<(MainWindowViewModel ViewModel, FakeFolderLauncher Launcher, string FirstDir, string SecondDir)> CreateTwoRepoSutAsync()
    {
        var firstDir = Directory.CreateTempSubdirectory("repomanager-dblclick-a").FullName;
        var secondDir = Directory.CreateTempSubdirectory("repomanager-dblclick-b").FullName;

        var dashboard = new FakeDashboard(
        [
            FakeDashboard.Item("Alpha", firstDir),
            FakeDashboard.Item("Beta", secondDir)
        ]);
        var launcher = new FakeFolderLauncher();
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(),
            dashboard,
            new CancelledPicker(),
            folderLauncher: launcher);

        await sut.InitializeAsync();
        return (sut, launcher, firstDir, secondDir);
    }

    [Fact]
    public async Task OpenFolder_with_explicit_target_opens_that_row_not_selection()
    {
        // The double-clicked row is passed as the command parameter, so the
        // opened folder must be the target row even when the selection
        // points elsewhere.
        var (sut, launcher, _, secondDir) = await CreateTwoRepoSutAsync();
        try
        {
            sut.SelectedRepository = sut.Repositories[0];
            var target = sut.Repositories[1];

            sut.OpenFolderCommand.Execute(target);

            launcher.LastPath.Should().Be(secondDir);
            launcher.LastPath.Should().Be(target.DetailsPath);
            sut.StatusText.Should().Be("Opened folder for 'Beta'.");
        }
        finally
        {
            Directory.Delete(sut.Repositories[0].DetailsPath);
            Directory.Delete(sut.Repositories[1].DetailsPath);
        }
    }

    [Fact]
    public void TryResolve_returns_null_for_missing_or_non_visual_source()
    {
        MainWindow.TryResolveDoubleClickedRepository(null).Should().BeNull();
        MainWindow.TryResolveDoubleClickedRepository(new object()).Should().BeNull();
        MainWindow.TryResolveDoubleClickedRepository("not-a-visual").Should().BeNull();
    }

    [Fact]
    public async Task TryResolve_returns_row_for_row_visual_and_null_for_empty_space()
    {
        await _session.Dispatch(() =>
        {
            var row = new RepositoryRowViewModel(
                FakeDashboard.Item("Store", Path.GetTempPath()));

            var rowVisual = new TextBlock { DataContext = row };
            MainWindow.TryResolveDoubleClickedRepository(rowVisual).Should().BeSameAs(row);

            var viewModel = new MainWindowViewModel(
                new FakeGitEnvironment(),
                new FakeDashboard(),
                new CancelledPicker());

            var emptySpaceVisual = new TextBlock { DataContext = viewModel };
            MainWindow.TryResolveDoubleClickedRepository(emptySpaceVisual).Should().BeNull(
                "empty grid space carries the window view model, not a row");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Double_clicking_row_opens_that_folder_and_keeps_selection()
    {
        await _session.Dispatch(async () =>
        {
            var (viewModel, launcher, _, _) = await CreateTwoRepoSutAsync();
            var window = new MainWindow(viewModel);
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                viewModel.SelectedRepository = viewModel.Repositories[0];
                var doubleClicked = viewModel.Repositories[1];
                var rowVisual = new TextBlock { DataContext = doubleClicked };

                window.HandleDoubleTappedSource(rowVisual);

                launcher.LastPath.Should().Be(doubleClicked.DetailsPath);
                viewModel.StatusText.Should().Be("Opened folder for 'Beta'.");
                viewModel.SelectedRepository.Should().Be(
                    viewModel.Repositories[0],
                    "double-click opens via explicit target and must not disturb single-click selection");

                Directory.Delete(viewModel.Repositories[0].DetailsPath);
                Directory.Delete(viewModel.Repositories[1].DetailsPath);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Double_clicking_empty_space_does_nothing_and_does_not_throw()
    {
        await _session.Dispatch(async () =>
        {
            var (viewModel, launcher, firstDir, secondDir) = await CreateTwoRepoSutAsync();
            var window = new MainWindow(viewModel);
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                viewModel.SelectedRepository = viewModel.Repositories[0];

                var emptySpaceVisual = new TextBlock { DataContext = viewModel };

                var act = () =>
                {
                    window.HandleDoubleTappedSource(null);
                    window.HandleDoubleTappedSource(emptySpaceVisual);
                    window.HandleDoubleTappedSource(new TextBlock { DataContext = null });
                };

                act.Should().NotThrow("double-click without a valid row target must no-op");
                launcher.Calls.Should().Be(0);
                viewModel.SelectedRepository.Should().Be(viewModel.Repositories[0]);

                Directory.Delete(firstDir);
                Directory.Delete(secondDir);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
