using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using FluentAssertions;
using RepoDashboard.App.Services;
using RepoDashboard.App.ViewModels;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;

namespace RepoDashboard.App.Tests;

/// <summary>
/// RM-008: main window title shows the running application version from
/// runtime assembly metadata. Covers the view-model exposure, the AXAML
/// binding (no hard-coded version constant), and the headless runtime title.
/// </summary>
[Collection("Headless")]
public sealed class MainWindowTitleTests : IDisposable
{
    private readonly HeadlessUnitTestSession _session;

    public MainWindowTitleTests()
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

    private sealed class StubDashboard : IRepositoryDashboardService
    {
        public Task<IReadOnlyList<RepositoryDashboardItem>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RepositoryDashboardItem>>([]);

        public Task<IReadOnlyList<RepositoryConfiguration>> LoadConfigurationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RepositoryConfiguration>>([]);

        public Task<RepositoryDashboardItem> RefreshAsync(Guid repositoryId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<RepositoryDashboardItem>> RefreshAllAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<RepositoryDashboardItem> AddAsync(string path, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task RemoveAsync(Guid repositoryId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<RepositoryConfiguration> RenameAsync(Guid repositoryId, string name, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task MoveAsync(Guid repositoryId, int newIndex, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<RepositoryDashboardItem> FetchAsync(Guid repositoryId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<RepositoryBatchResult> FetchAllAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<RepositoryDashboardItem> UpdateAsync(Guid repositoryId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<RepositoryBatchResult> UpdateAllAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }

    private sealed class CancelledPicker : IFolderPickerService
    {
        public Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>?> PickFoldersAsync(string title, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>?>(null);
    }

    private static MainWindowViewModel CreateViewModel(string? windowTitle = null) =>
        new(
            new FakeGitEnvironment(),
            new StubDashboard(),
            new CancelledPicker(),
            windowTitle: windowTitle);

    private static string FindMainWindowAxaml()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "RepoDashboard.App",
                "MainWindow.axaml");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate src/RepoDashboard.App/MainWindow.axaml from "
            + AppContext.BaseDirectory);
    }

    [Fact]
    public void ViewModel_WindowTitle_contains_product_name_and_runtime_version()
    {
        var sut = CreateViewModel();

        sut.WindowTitle.Should().Contain(AppVersion.ProductName);
        sut.WindowTitle.Should().Contain(AppVersion.ResolveVersion(typeof(AppVersion).Assembly));
        sut.WindowTitle.Should().NotContain("+");
    }

    [Fact]
    public void ViewModel_WindowTitle_honors_explicit_override()
    {
        var sut = CreateViewModel(windowTitle: "RepoManager — 9.9.9-test");

        sut.WindowTitle.Should().Be("RepoManager — 9.9.9-test");
    }

    [Fact]
    public void MainWindow_axaml_binds_title_to_view_model_without_hardcoded_version()
    {
        var document = XDocument.Load(FindMainWindowAxaml());
        var title = document.Root?.Attribute("Title")?.Value ?? string.Empty;

        title.Should().Contain("WindowTitle",
            "the main window title must be bound to the view-model version title, not hard-coded");
        title.Should().NotContain(AppVersion.ResolveVersion(typeof(AppVersion).Assembly),
            "the version must come from runtime assembly metadata, not a hard-coded AXAML constant");

        // Designer fallback keeps just the product name readable.
        title.Should().Contain(AppVersion.ProductName);
    }

    [Fact]
    public async Task MainWindow_runtime_title_shows_product_name_and_version()
    {
        await _session.Dispatch(() =>
        {
            var viewModel = CreateViewModel();
            var window = new MainWindow(viewModel);
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                window.Title.Should().Contain(AppVersion.ProductName);
                window.Title.Should().Contain(
                    AppVersion.ResolveVersion(typeof(AppVersion).Assembly));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
