using FluentAssertions;
using RepoDashboard.App.Services;
using RepoDashboard.App.ViewModels;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.App.Tests.ViewModels;

/// <summary>
/// RM-004: desktop integration (open folder/terminal, clipboard, help) goes
/// through small injected services so view models stay testable without a
/// real desktop session. Failures map to the existing status error style,
/// never a crash; missing terminals stay disabled.
/// </summary>
public sealed class DesktopIntegrationTests
{
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

    private sealed class FakeClipboard(string? failWith = null) : IClipboardService
    {
        public string? LastText { get; private set; }

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            if (failWith is not null)
            {
                throw new InvalidOperationException(failWith);
            }

            LastText = text;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFolderLauncher(Action<string>? onOpen = null, bool fail = false) : IFolderLauncher
    {
        public string? LastPath { get; private set; }

        public void OpenFolder(string path)
        {
            if (fail)
            {
                throw new InvalidOperationException("open boom");
            }

            LastPath = path;
            onOpen?.Invoke(path);
        }
    }

    private sealed class FakeTerminal(bool available, bool fail = false) : ITerminalLauncher
    {
        public bool IsAvailable => available;

        public string? LastPath { get; private set; }

        public void OpenTerminal(string workingDirectory)
        {
            if (!available)
            {
                throw new InvalidOperationException("No supported terminal was found.");
            }

            if (fail)
            {
                throw new InvalidOperationException("terminal boom");
            }

            LastPath = workingDirectory;
        }
    }

    private sealed class FakeHelp : IKeyboardShortcutsDialogService
    {
        public int Calls { get; private set; }

        public Task ShowAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private static async Task<MainWindowViewModel> CreateSutAsync(
        FakeDashboard dashboard,
        FakeClipboard? clipboard = null,
        FakeFolderLauncher? folderLauncher = null,
        FakeTerminal? terminal = null,
        FakeHelp? help = null)
    {
        var sut = new MainWindowViewModel(
            new FakeGitEnvironment(),
            dashboard,
            new CancelledPicker(),
            clipboard: clipboard ?? new FakeClipboard(),
            folderLauncher: folderLauncher ?? new FakeFolderLauncher(),
            terminalLauncher: terminal ?? new FakeTerminal(true),
            shortcutHelp: help ?? new FakeHelp());

        await sut.InitializeAsync();
        return sut;
    }

    [Fact]
    public async Task OpenFolder_uses_launcher_and_reports_status()
    {
        var tempDir = Directory.CreateTempSubdirectory("repomanager-open-folder").FullName;
        try
        {
            var dashboard = new FakeDashboard([FakeDashboard.Item("Store", tempDir)]);
            var launcher = new FakeFolderLauncher();
            var sut = await CreateSutAsync(dashboard, folderLauncher: launcher);
            sut.SelectedRepository = sut.Repositories[0];

            sut.OpenFolderCommand.Execute(null);

            launcher.LastPath.Should().Be(tempDir);
            sut.StatusText.Should().Be("Opened folder for 'Store'.");
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public async Task OpenFolder_missing_directory_reports_error_without_launcher()
    {
        var dashboard = new FakeDashboard([FakeDashboard.Item("Store", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))]);
        var launcher = new FakeFolderLauncher();
        var sut = await CreateSutAsync(dashboard, folderLauncher: launcher);
        sut.SelectedRepository = sut.Repositories[0];

        sut.OpenFolderCommand.Execute(null);

        launcher.LastPath.Should().BeNull();
        sut.StatusText.Should().Contain("Folder does not exist");
    }

    [Fact]
    public async Task OpenFolder_launcher_failure_reports_status()
    {
        var tempDir = Directory.CreateTempSubdirectory("repomanager-open-fail").FullName;
        try
        {
            var dashboard = new FakeDashboard([FakeDashboard.Item("Store", tempDir)]);
            var launcher = new FakeFolderLauncher(fail: true);
            var sut = await CreateSutAsync(dashboard, folderLauncher: launcher);
            sut.SelectedRepository = sut.Repositories[0];

            sut.OpenFolderCommand.Execute(null);

            sut.StatusText.Should().Contain("Could not open folder");
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public async Task OpenTerminal_is_disabled_without_available_terminal()
    {
        var dashboard = new FakeDashboard([FakeDashboard.Item("Store", Path.GetTempPath())]);
        var sut = await CreateSutAsync(dashboard, terminal: new FakeTerminal(false));
        sut.SelectedRepository = sut.Repositories[0];

        sut.OpenTerminalCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task OpenTerminal_uses_launcher_when_available()
    {
        var dashboard = new FakeDashboard([FakeDashboard.Item("Store", Path.GetTempPath())]);
        var terminal = new FakeTerminal(true);
        var sut = await CreateSutAsync(dashboard, terminal: terminal);
        sut.SelectedRepository = sut.Repositories[0];

        sut.OpenTerminalCommand.CanExecute(null).Should().BeTrue();
        sut.OpenTerminalCommand.Execute(null);

        terminal.LastPath.Should().Be(sut.Repositories[0].DetailsPath);
        sut.StatusText.Should().Be("Opened terminal for 'Store'.");
    }

    [Fact]
    public async Task OpenTerminal_failure_reports_status()
    {
        var dashboard = new FakeDashboard([FakeDashboard.Item("Store", Path.GetTempPath())]);
        var sut = await CreateSutAsync(dashboard, terminal: new FakeTerminal(true, fail: true));
        sut.SelectedRepository = sut.Repositories[0];

        sut.OpenTerminalCommand.Execute(null);

        sut.StatusText.Should().Contain("Could not open terminal");
    }

    [Fact]
    public async Task ShowHelp_invokes_dialog_service()
    {
        var dashboard = new FakeDashboard();
        var help = new FakeHelp();
        var sut = await CreateSutAsync(dashboard, help: help);

        await sut.ShowHelpCommand.ExecuteAsync(null);

        help.Calls.Should().Be(1);
    }
}
