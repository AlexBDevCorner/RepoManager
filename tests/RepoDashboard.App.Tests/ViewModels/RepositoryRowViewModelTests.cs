using FluentAssertions;
using RepoDashboard.App.ViewModels;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.App.Tests.ViewModels;

public sealed class RepositoryRowViewModelTests
{
    private static RepositoryDashboardItem Item(
        string name = "Store",
        string? branch = "feature/search",
        bool dirty = false,
        Divergence? upstream = null,
        string? defaultBranch = "main",
        Divergence? defaultDivergence = null,
        UpdateEligibility eligibility = UpdateEligibility.Ahead,
        DateTimeOffset? lastFetch = null,
        bool detached = false,
        bool directoryExists = true,
        bool isGitRepository = true)
    {
        var configuration = new RepositoryConfiguration
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = """C:\Source\Repos\Store"""
        };

        var snapshot = new RepositorySnapshot
        {
            RepositoryId = configuration.Id,
            Path = configuration.Path,
            DirectoryExists = directoryExists,
            IsGitRepository = isGitRepository,
            CurrentBranch = detached ? null : branch,
            IsDetachedHead = detached,
            DetachedHeadSha = detached ? "a84c019" : null,
            IsDirty = dirty,
            UpstreamRef = upstream is null && !detached ? null : "origin/feature/search",
            UpstreamRemote = "origin",
            UpstreamBranch = "feature/search",
            DefaultRemoteBranch = defaultBranch,
            UpstreamDivergence = upstream,
            DefaultBranchDivergence = defaultDivergence,
            InspectedAt = DateTimeOffset.UtcNow
        };

        return new RepositoryDashboardItem
        {
            Configuration = configuration,
            Snapshot = snapshot,
            UpdateDecision = new UpdateDecision(eligibility, "explanation"),
            LastSuccessfulFetch = lastFetch
        };
    }

    [Fact]
    public void Null_divergence_formats_as_em_dash()
    {
        var row = new RepositoryRowViewModel(
            Item(upstream: null, defaultBranch: null));

        row.UpstreamStatus.Should().Be("—");
        row.DefaultBranchStatus.Should().Be("—");
    }

    [Fact]
    public void Divergence_formats_as_arrows()
    {
        var row = new RepositoryRowViewModel(
            Item(
                upstream: new Divergence(2, 0),
                defaultBranch: "main",
                defaultDivergence: new Divergence(5, 3)));

        row.UpstreamStatus.Should().Be("↑2 ↓0");
        row.DefaultBranchStatus.Should().Be("main ↑5 ↓3");
    }

    [Fact]
    public void Constructor_maps_presentation_fields()
    {
        var row = new RepositoryRowViewModel(
            Item(branch: "feature/search", eligibility: UpdateEligibility.Ahead));

        row.Name.Should().Be("Store");
        row.Branch.Should().Be("feature/search");
        row.WorktreeStatus.Should().Be("Clean");
        row.UpdateStatus.Should().Be(nameof(UpdateEligibility.Ahead));
        row.Explanation.Should().Be("explanation");
        row.LastFetchText.Should().Be("Never");
    }

    [Fact]
    public void Dirty_snapshot_shows_dirty()
    {
        var row = new RepositoryRowViewModel(Item(dirty: true));

        row.WorktreeStatus.Should().Be("Dirty");
    }

    [Fact]
    public void Detached_head_shows_short_sha()
    {
        var row = new RepositoryRowViewModel(Item(detached: true));

        row.Branch.Should().Be("Detached HEAD @ a84c019");
    }

    [Fact]
    public void Missing_directory_shows_missing()
    {
        var row = new RepositoryRowViewModel(
            Item(directoryExists: false, isGitRepository: false));

        row.Branch.Should().Be("—");
        row.WorktreeStatus.Should().Be("Missing");
    }

    [Fact]
    public void Failed_inspection_renders_error_row()
    {
        var configuration = new RepositoryConfiguration
        {
            Id = Guid.NewGuid(),
            Name = "Broken",
            Path = """C:\Source\Repos\Broken"""
        };

        const string error = "git status unexpectedly failed";

        var row = new RepositoryRowViewModel(new RepositoryDashboardItem
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
        });

        row.Name.Should().Be("Broken");
        row.Branch.Should().Be("—");
        row.WorktreeStatus.Should().Be("Error");
        row.UpstreamStatus.Should().Be("—");
        row.DefaultBranchStatus.Should().Be("—");
        row.UpdateStatus.Should().Be(nameof(UpdateEligibility.Unknown));
        row.Explanation.Should().Be(error);
    }

    [Fact]
    public void Never_fetched_row_is_flagged_as_potentially_stale()
    {
        var row = new RepositoryRowViewModel(Item(lastFetch: null));

        row.IsStale.Should().BeTrue();
        row.StaleText.Should().Be("Remote state may be stale");
        row.LastFetchText.Should().Be("Never");
    }

    [Fact]
    public void Freshly_fetched_row_is_not_stale()
    {
        var row = new RepositoryRowViewModel(
            Item(
                upstream: new Divergence(0, 0),
                lastFetch: DateTimeOffset.UtcNow));

        row.IsStale.Should().BeFalse();
        row.StaleText.Should().BeEmpty();
        row.DetailsLastOperation.Should().Be("Fetch successful");
    }

    [Fact]
    public void Days_old_fetch_is_flagged_as_potentially_stale()
    {
        var row = new RepositoryRowViewModel(
            Item(lastFetch: DateTimeOffset.UtcNow.AddDays(-3)));

        row.IsStale.Should().BeTrue();
        row.StaleText.Should().Be("Remote state may be stale");
        row.LastFetchText.Should().Be("3 d ago");
    }

    [Fact]
    public void Details_panel_maps_selection_fields()
    {
        var row = new RepositoryRowViewModel(
            Item(
                branch: "feature/search",
                upstream: new Divergence(2, 0),
                defaultBranch: "main",
                defaultDivergence: new Divergence(5, 3),
                eligibility: UpdateEligibility.Ahead,
                lastFetch: DateTimeOffset.UtcNow));

        row.DetailsPath.Should().Be("""C:\Source\Repos\Store""");
        row.DetailsBranch.Should().Be("feature/search");
        row.DetailsUpstream.Should().Be("origin/feature/search");
        row.DetailsRemoteDefault.Should().Be("origin/main");
        row.DetailsRemote.Should().Be("origin");
        row.DetailsVsUpstream.Should().Be("2 ahead / 0 behind");
        row.DetailsVsDefault.Should().Be("5 ahead / 3 behind");
        row.DetailsWorkingTree.Should().Be("Clean");
        row.DetailsLastFetch.Should().NotBe("Never");
        row.DetailsLastOperation.Should().Be("Fetch successful");
        row.DetailsGitError.Should().BeEmpty();
    }

    [Fact]
    public void Failed_inspection_details_show_git_error()
    {
        var configuration = new RepositoryConfiguration
        {
            Id = Guid.NewGuid(),
            Name = "Broken",
            Path = """C:\Source\Repos\Broken"""
        };

        const string error = "fatal: unable to access 'https://example.invalid/'";

        var row = new RepositoryRowViewModel(new RepositoryDashboardItem
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
        });

        row.DetailsGitError.Should().Be(error);
        row.DetailsLastOperation.Should().Be("Inspection failed");
        row.DetailsWorkingTree.Should().Be("Error");
    }

    [Fact]
    public void Update_refreshes_properties_in_place()
    {
        var row = new RepositoryRowViewModel(Item(branch: "main"));
        var configuration = new RepositoryConfiguration
        {
            Id = row.RepositoryId,
            Name = "Store",
            Path = """C:\Source\Repos\Store"""
        };
        var refreshed = new RepositoryDashboardItem
        {
            Configuration = configuration,
            Snapshot = new RepositorySnapshot
            {
                RepositoryId = configuration.Id,
                Path = configuration.Path,
                DirectoryExists = true,
                IsGitRepository = true,
                CurrentBranch = "feature/search",
                IsDirty = true,
                UpstreamRef = "origin/feature/search",
                UpstreamRemote = "origin",
                UpstreamBranch = "feature/search",
                UpstreamDivergence = new Divergence(0, 1),
                InspectedAt = DateTimeOffset.UtcNow
            },
            UpdateDecision = new UpdateDecision(
                UpdateEligibility.Dirty, "dirty")
        };

        var idBefore = row.RepositoryId;
        row.Update(refreshed);

        row.RepositoryId.Should().Be(idBefore);
        row.Branch.Should().Be("feature/search");
        row.WorktreeStatus.Should().Be("Dirty");
        row.UpdateStatus.Should().Be(nameof(UpdateEligibility.Dirty));
    }
}
