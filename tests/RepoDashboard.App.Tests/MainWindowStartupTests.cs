using System.Reflection;
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
/// RM-005: runtime regression coverage for MainWindow startup wiring.
///
/// The previous lightweight <c>MainWindowXamlTests</c> only parsed AXAML as
/// XML and verified <c>x:Name="RepositoryGrid"</c> exists. That did not catch
/// the startup <c>NullReferenceException</c>: the code-behind defined a
/// private parameterless <c>InitializeComponent()</c> that shadowed Avalonia's
/// generated <c>InitializeComponent(bool)</c>, so the generated
/// <c>RepositoryGrid</c> field was never assigned and
/// <c>RepositoryGrid.Focus()</c> crashed in the Loaded handler.
///
/// These tests exercise actual Avalonia control initialization via the
/// headless platform and fail against the broken implementation (null field)
/// while passing after the fix.
/// </summary>
public sealed class MainWindowStartupTests : IDisposable
{
    private readonly HeadlessUnitTestSession _session;

    public MainWindowStartupTests()
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

    private static MainWindowViewModel CreateViewModel() =>
        new(
            new FakeGitEnvironment(),
            new StubDashboard(),
            new CancelledPicker());

    private static DataGrid? GetRepositoryGridField(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
            "RepositoryGrid",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        field.Should().NotBeNull("MainWindow must have a RepositoryGrid field wired from XAML x:Name");
        return field!.GetValue(window) as DataGrid;
    }

    [Fact]
    public async Task MainWindow_resolves_RepositoryGrid_after_XAML_initialization()
    {
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(CreateViewModel());
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                // Avalonia-supported lookup: the namescope must contain the grid.
                var viaLookup = window.FindControl<DataGrid>("RepositoryGrid");
                viaLookup.Should().NotBeNull(
                    "MainWindow.axaml must define <DataGrid x:Name=\"RepositoryGrid\" ...>");

                // Code-behind field wiring: the broken RM-004 implementation
                // shadowed the generated InitializeComponent(bool) with a
                // private parameterless overload that only called
                // AvaloniaXamlLoader.Load, leaving this field null and
                // crashing startup with NullReferenceException.
                var viaField = GetRepositoryGridField(window);
                viaField.Should().NotBeNull(
                    "RepositoryGrid field must be assigned by the generated "
                    + "InitializeComponent via the XAML namescope; a null field "
                    + "means the manual InitializeComponent shadows the generated wiring");
                viaField.Should().BeSameAs(viaLookup);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MainWindow_moves_initial_focus_to_RepositoryGrid_on_loaded()
    {
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(CreateViewModel());
            try
            {
                window.Show();

                // Flush Loaded plus the Dispatcher.UIThread.Post(() => grid.Focus())
                // queued by the startup handler. RunJobs twice to cover the
                // posted callback scheduling a nested focus job.
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                var grid = window.FindControl<DataGrid>("RepositoryGrid");
                grid.Should().NotBeNull();

                // The fix must preserve keyboard-first startup, not merely
                // avoid the null dereference by skipping focus.
                grid!.IsFocused.Should().BeTrue(
                    "the repository grid must receive initial focus after the "
                    + "window loads, even while async repository loading is in flight");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MainWindow_startup_does_not_throw_NullReferenceException()
    {
        await _session.Dispatch(() =>
        {
            var window = new MainWindow(CreateViewModel());
            try
            {
                var act = () =>
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    Dispatcher.UIThread.RunJobs();
                };

                act.Should().NotThrow(
                    "creating and showing MainWindow must not throw because "
                    + "RepositoryGrid is null");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
