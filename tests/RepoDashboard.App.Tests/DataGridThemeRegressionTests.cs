using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using RepoDashboard.App.Services;
using RepoDashboard.App.ViewModels;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.App.Tests;

/// <summary>
/// RM-007: runtime regression coverage for the blank repository-grid
/// regression. The Avalonia <c>DataGrid</c> lives in the separate
/// <c>Avalonia.Controls.DataGrid</c> package and renders nothing without its
/// Fluent theme resources. Parsing <c>MainWindow.axaml</c> as XML cannot catch
/// that: the grid element exists either way. These tests verify the
/// application resources register the DataGrid theme and that a grid bound to
/// repository rows actually materializes content headlessly.
/// </summary>
[Collection("Headless")]
public sealed class DataGridThemeRegressionTests : IDisposable
{
    private readonly HeadlessUnitTestSession _session;

    public DataGridThemeRegressionTests()
    {
        _session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp));
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    private static string FindAxaml(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "RepoDashboard.App",
                fileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate src/RepoDashboard.App/{fileName} from "
            + AppContext.BaseDirectory);
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

    private static RepositoryDashboardItem ItemFor(string name)
    {
        var configuration = new RepositoryConfiguration
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = OperatingSystem.IsWindows() ? $@"C:\Source\Repos\{name}" : $"/source/repos/{name}"
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
    public void App_axaml_registers_DataGrid_Fluent_theme()
    {
        // The blank-grid regression was a missing resource include: the
        // DataGrid element in MainWindow.axaml exists either way, so this
        // guards the production App.axaml resource list itself.
        var text = File.ReadAllText(FindAxaml("App.axaml"));

        text.Should().Contain("Avalonia.Controls.DataGrid",
            "App.axaml must reference the DataGrid theme package resources");
        text.Should().Contain("Fluent.xaml",
            "App.axaml must include the DataGrid Fluent theme (avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml)");
        text.Should().Contain("StyleInclude",
            "the DataGrid theme must be registered via <StyleInclude .../> alongside <FluentTheme />");
    }

    [Fact]
    public async Task Headless_resources_include_DataGrid_theme()
    {
        // Proves the headless test application mirrors production App.axaml:
        // if the DataGrid StyleInclude is removed from the initialized
        // resources, this fails instead of silently testing an unstyled grid.
        await _session.Dispatch(() =>
        {
            var styles = Application.Current?.Styles;
            styles.Should().NotBeNull("an Avalonia application must be initialized");

            var hasDataGridTheme = styles!
                .OfType<StyleInclude>()
                .Any(s => s.Source?.ToString().Contains(
                    "Avalonia.Controls.DataGrid",
                    StringComparison.OrdinalIgnoreCase) == true);

            hasDataGridTheme.Should().BeTrue(
                "initialized application resources must include the DataGrid theme "
                + "(avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml); "
                + "without it the repository DataGrid renders blank");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RepositoryGrid_renders_bound_repository_rows()
    {
        // End-to-end rendering guard: rows present in
        // MainWindowViewModel.Repositories must materialize visibly in
        // RepositoryGrid once Avalonia resources (Fluent + DataGrid Fluent)
        // are initialized. Without the DataGrid theme the grid binds but
        // renders no row visuals, reproducing the RM-007 blank grid.
        await _session.Dispatch(() =>
        {
            var viewModel = new MainWindowViewModel(
                new FakeGitEnvironment(),
                new StubDashboard(),
                new CancelledPicker());

            viewModel.Repositories.Add(new RepositoryRowViewModel(ItemFor("Store")));
            viewModel.Repositories.Add(new RepositoryRowViewModel(ItemFor("Viewer")));

            var window = new MainWindow(viewModel);
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                var grid = window.FindControl<DataGrid>("RepositoryGrid");
                grid.Should().NotBeNull(
                    "MainWindow.axaml must define <DataGrid x:Name=\"RepositoryGrid\" ...>");

                // Binding is intact: the grid sees the view-model rows.
                var itemsSource = grid!.ItemsSource as System.Collections.IList;
                itemsSource.Should().NotBeNull(
                    "RepositoryGrid must be bound to MainWindowViewModel.Repositories");
                itemsSource!.Count.Should().Be(2,
                    "RepositoryGrid must be bound to MainWindowViewModel.Repositories");

                // Theme is applied: the grid template produces its internal
                // presenters. Without the DataGrid Fluent theme the control
                // has no row/cell visuals even though Items is populated.
                var descendants = grid.GetVisualDescendants().ToList();
                descendants.Should().NotBeEmpty(
                    "a themed DataGrid must produce visual children; an empty visual tree "
                    + "means the DataGrid theme resources are missing");

                var hasRowVisuals = descendants.OfType<DataGridRow>().Any()
                    || descendants.Any(d => d.GetType().Name.Contains(
                        "RowsPresenter", StringComparison.Ordinal))
                    || descendants.Any(d => d.GetType().Name.Contains(
                        "CellsPresenter", StringComparison.Ordinal));

                hasRowVisuals.Should().BeTrue(
                    "the repository grid must materialize row visuals for bound repositories; "
                    + "a bound-but-empty visual tree is the RM-007 blank-grid symptom");

                // Selection stays wired: a row visible in the details panel
                // must correspond to a selectable grid row.
                viewModel.SelectedRepository = viewModel.Repositories[0];
                Dispatcher.UIThread.RunJobs();

                grid.SelectedItem.Should().Be(viewModel.Repositories[0],
                    "selecting a repository (as shown in the details panel) must "
                    + "correspond to a selected RepositoryGrid row");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
