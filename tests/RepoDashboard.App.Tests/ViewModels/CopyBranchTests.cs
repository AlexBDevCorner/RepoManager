using System.Windows;
using FluentAssertions;
using RepoDashboard.App.Services;
using RepoDashboard.App.ViewModels;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.App.Tests.ViewModels;

/// <summary>
/// RM-003: Copy Branch copies the exact current branch name to the
/// clipboard, stays disabled without a usable branch, and never touches
/// Git. The dashboard and Git environment fakes are strict: any call
/// throws, proving the action is presentation-only. Clipboard success
/// paths run on an STA thread (a WPF requirement); disabled paths never
/// reach the clipboard and run inline.
/// </summary>
public sealed class CopyBranchTests
{
    private sealed class StrictGitEnvironment : IGitEnvironment
    {
        public Task<GitEnvironmentInfo> CheckAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Copy Branch must not query Git availability.");
    }

    private sealed class StrictDashboard : IRepositoryDashboardService
    {
        private static Exception Refused() => new InvalidOperationException(
            "Copy Branch must not reach the dashboard.");

        public Task<IReadOnlyList<RepositoryDashboardItem>> LoadAsync(
            CancellationToken cancellationToken) =>
            throw Refused();

        public Task<IReadOnlyList<RepositoryConfiguration>> LoadConfigurationsAsync(
            CancellationToken cancellationToken) =>
            throw Refused();

        public Task<RepositoryDashboardItem> RefreshAsync(
            Guid repositoryId,
            CancellationToken cancellationToken) =>
            throw Refused();

        public Task<IReadOnlyList<RepositoryDashboardItem>> RefreshAllAsync(
            CancellationToken cancellationToken) =>
            throw Refused();

        public Task<RepositoryDashboardItem> FetchAsync(
            Guid repositoryId,
            CancellationToken cancellationToken) =>
            throw Refused();

        public Task<RepositoryBatchResult> FetchAllAsync(
            CancellationToken cancellationToken) =>
            throw Refused();

        public Task<RepositoryDashboardItem> UpdateAsync(
            Guid repositoryId,
            CancellationToken cancellationToken) =>
            throw Refused();

        public Task<RepositoryBatchResult> UpdateAllAsync(
            CancellationToken cancellationToken) =>
            throw Refused();

        public Task<RepositoryDashboardItem> AddAsync(
            string path,
            CancellationToken cancellationToken) =>
            throw Refused();

        public Task RemoveAsync(
            Guid repositoryId,
            CancellationToken cancellationToken) =>
            throw Refused();

        public Task<RepositoryConfiguration> RenameAsync(
            Guid repositoryId,
            string name,
            CancellationToken cancellationToken) =>
            throw Refused();

        public Task MoveAsync(
            Guid repositoryId,
            int newIndex,
            CancellationToken cancellationToken) =>
            throw Refused();
    }

    private sealed class CancelledPicker : IFolderPickerService
    {
        public string? PickFolder(string title) => null;

        public IReadOnlyList<string>? PickFolders(string title) => null;
    }

    private static RepositoryDashboardItem Item(
        string name = "Store",
        string? branch = "feature/foo",
        bool detached = false,
        bool directoryExists = true,
        bool isGitRepository = true,
        string? inspectionError = null)
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
                DirectoryExists = directoryExists,
                IsGitRepository = isGitRepository,
                CurrentBranch = detached ? null : branch,
                IsDetachedHead = detached,
                DetachedHeadSha = detached ? "a84c019" : null,
                UpstreamRef = "origin/feature/foo",
                UpstreamRemote = "origin",
                UpstreamBranch = "feature/foo",
                UpstreamDivergence = new Divergence(0, 0),
                InspectedAt = DateTimeOffset.UtcNow
            },
            UpdateDecision = new UpdateDecision(
                UpdateEligibility.AlreadyUpToDate, "up to date"),
            InspectionError = inspectionError
        };
    }

    private static MainWindowViewModel CreateSut() => new(
        new StrictGitEnvironment(), new StrictDashboard(), new CancelledPicker());

    private static RepositoryRowViewModel AddRow(
        MainWindowViewModel sut,
        RepositoryDashboardItem item,
        bool select = true)
    {
        var row = new RepositoryRowViewModel(item);
        sut.Repositories.Add(row);

        if (select)
        {
            sut.SelectedRepository = row;
        }

        return row;
    }

    /// <summary>
    /// WPF clipboard access requires an STA thread; xUnit runs MTA.
    /// </summary>
    private static T RunOnSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(error).Throw();
        }

        return result!;
    }

    [Fact]
    public void CopyBranch_is_disabled_without_selection()
    {
        var sut = CreateSut();

        sut.CopyBranchCommand.CanExecute(null).Should().BeFalse();

        sut.CopyBranchCommand.Execute(null);

        sut.StatusText.Should().Be("Select a repository first.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CopyBranch_is_disabled_without_usable_branch(string? branch)
    {
        var sut = CreateSut();
        var row = AddRow(sut, Item(branch: branch));

        row.Branch.Should().Be("—");
        row.CopyableBranch.Should().BeNull();
        sut.CopyBranchCommand.CanExecute(null).Should().BeFalse();
        sut.CopyBranchCommand.CanExecute(row).Should().BeFalse();

        sut.CopyBranchCommand.Execute(null);

        sut.StatusText.Should().Be("No branch to copy for 'Store'.");
    }

    [Fact]
    public void CopyBranch_is_disabled_for_detached_head()
    {
        var sut = CreateSut();
        var row = AddRow(sut, Item(detached: true));

        row.Branch.Should().Be("Detached HEAD @ a84c019");
        row.CopyableBranch.Should().BeNull();
        sut.CopyBranchCommand.CanExecute(null).Should().BeFalse();
        sut.CopyBranchCommand.CanExecute(row).Should().BeFalse();
    }

    [Fact]
    public void CopyBranch_is_disabled_when_repository_is_missing()
    {
        var sut = CreateSut();
        var row = AddRow(sut, Item(directoryExists: false, isGitRepository: false));

        row.CopyableBranch.Should().BeNull();
        sut.CopyBranchCommand.CanExecute(null).Should().BeFalse();
        sut.CopyBranchCommand.CanExecute(row).Should().BeFalse();
    }

    [Fact]
    public void CopyBranch_is_disabled_when_inspection_failed()
    {
        var sut = CreateSut();
        var row = AddRow(
            sut, Item(inspectionError: "git status unexpectedly failed"));

        row.CopyableBranch.Should().BeNull();
        sut.CopyBranchCommand.CanExecute(null).Should().BeFalse();
        sut.CopyBranchCommand.CanExecute(row).Should().BeFalse();
    }

    [Fact]
    public void CopyBranch_is_disabled_while_busy()
    {
        var sut = CreateSut();
        var row = AddRow(sut, Item(branch: "feature/foo"));

        row.CopyableBranch.Should().Be("feature/foo");
        sut.IsBusy = true;

        sut.CopyBranchCommand.CanExecute(null).Should().BeFalse();
        sut.CopyBranchCommand.CanExecute(row).Should().BeFalse();
    }

    [Fact]
    public void CopyBranch_is_enabled_for_valid_branch()
    {
        var sut = CreateSut();
        var row = AddRow(sut, Item(branch: "feature/foo"));

        sut.CopyBranchCommand.CanExecute(null).Should().BeTrue();
        sut.CopyBranchCommand.CanExecute(row).Should().BeTrue();
    }

    [Fact]
    public void CopyBranch_accepts_explicit_target_without_selection()
    {
        var sut = CreateSut();
        var target = new RepositoryRowViewModel(Item(branch: "feature/foo"));

        sut.CopyBranchCommand.CanExecute(null).Should().BeFalse();
        sut.CopyBranchCommand.CanExecute(target).Should().BeTrue();
    }

    [Fact]
    public void CopyBranch_copies_exact_branch_without_git()
    {
        var sut = CreateSut();
        AddRow(sut, Item(branch: "feature/foo"));

        // Never initialized: Git availability was never queried and the
        // action still works from already-mapped presentation state.
        sut.IsGitAvailable.Should().BeFalse();

        var (status, clipboard) = RunOnSta(() =>
        {
            sut.CopyBranchCommand.Execute(null);
            return (sut.StatusText, Clipboard.GetText());
        });

        status.Should().Be("Copied branch 'feature/foo'.");
        clipboard.Should().Be("feature/foo");
    }

    [Fact]
    public void CopyBranch_uses_explicit_target_not_selection()
    {
        var sut = CreateSut();
        AddRow(sut, Item(name: "Store", branch: "main"));
        var target = AddRow(
            sut, Item(name: "Legacy", branch: "feature/foo"), select: false);

        var (status, clipboard) = RunOnSta(() =>
        {
            sut.CopyBranchCommand.Execute(target);
            return (sut.StatusText, Clipboard.GetText());
        });

        status.Should().Be("Copied branch 'feature/foo'.");
        clipboard.Should().Be("feature/foo");
    }

    [Fact]
    public void CopyBranch_maps_exact_branch_name_onto_row()
    {
        var row = new RepositoryRowViewModel(Item(branch: "feature/foo"));

        row.Branch.Should().Be("feature/foo");
        row.CopyableBranch.Should().Be("feature/foo");
    }

    [Fact]
    public void CopyBranch_placeholder_row_has_no_copyable_branch()
    {
        var configuration = new RepositoryConfiguration
        {
            Id = Guid.NewGuid(),
            Name = "Store",
            Path = """C:\Source\Repos\Store"""
        };

        var row = RepositoryRowViewModel.FromConfiguration(configuration);

        row.CopyableBranch.Should().BeNull();
    }

    [Fact]
    public void CopyPath_behavior_is_unchanged()
    {
        // RM-003 acceptance: existing Copy Path behavior is preserved.
        var sut = CreateSut();
        AddRow(sut, Item(name: "Store", branch: "feature/foo"));

        var (status, clipboard) = RunOnSta(() =>
        {
            sut.CopyPathCommand.Execute(null);
            return (sut.StatusText, Clipboard.GetText());
        });

        status.Should().Be("Copied path for 'Store'.");
        clipboard.Should().Be("""C:\Source\Repos\Store""");
    }
}
