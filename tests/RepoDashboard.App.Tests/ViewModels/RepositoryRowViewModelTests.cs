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
        // No operation produced this row (plain load): a historical
        // timestamp alone must not read as "Fetch successful".
        row.DetailsLastOperation.Should().Be("—");
    }

    [Fact]
    public void Load_with_old_persisted_fetch_claims_no_operation()
    {
        var item = Item(lastFetch: DateTimeOffset.UtcNow.AddDays(-2));

        var row = new RepositoryRowViewModel(item);

        row.DetailsLastOperation.Should().Be("—");
        row.IsStale.Should().BeTrue();
    }

    [Fact]
    public void Fetch_item_reports_fetch_successful()
    {
        var item = Item(lastFetch: DateTimeOffset.UtcNow) with
        {
            LastOperation = RepositoryOperationType.Fetch
        };

        var row = new RepositoryRowViewModel(item);

        row.DetailsLastOperation.Should().Be("Fetch successful");
    }

    [Fact]
    public void Refresh_item_reports_refreshed()
    {
        var item = Item(lastFetch: DateTimeOffset.UtcNow) with
        {
            LastOperation = RepositoryOperationType.Refresh
        };

        var row = new RepositoryRowViewModel(item);

        row.DetailsLastOperation.Should().Be("Refreshed");
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
        row.DetailsLastOperation.Should().Be("—");
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
        row.DetailsLastOperation.Should().Be("—");
        row.DetailsWorkingTree.Should().Be("Error");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Skipped_activity_stays_compact_with_full_reason_in_details()
    {
        const string message =
            "The working tree contains uncommitted changes. " +
            "Automatic update was skipped to avoid touching your edits.";
        var loaded = Item();
        var skipped = loaded with
        {
            UpdateDecision = new UpdateDecision(UpdateEligibility.Dirty, message),
            UpdateResult = new RepositoryUpdateResult
            {
                RepositoryId = loaded.Configuration.Id,
                Outcome = RepositoryUpdateOutcome.Skipped,
                Message = message,
                Decision = new UpdateDecision(UpdateEligibility.Dirty, message),
                FinalSnapshot = loaded.Snapshot
            },
            LastOperation = RepositoryOperationType.Update
        };

        var row = new RepositoryRowViewModel(skipped);

        row.Activity.Should().Be(RepositoryActivity.Skipped);
        row.ActivityText.Should().Be("Skipped — dirty");
        row.Explanation.Should().Be(message);
        row.DetailsLastOperation.Should().Be("Update skipped");
    }

    [Fact]
    public void Failed_update_activity_stays_compact_with_error_in_details()
    {
        const string message = "fatal: Not possible to fast-forward to 'abc123'.";
        var loaded = Item();
        var failed = loaded with
        {
            UpdateResult = new RepositoryUpdateResult
            {
                RepositoryId = loaded.Configuration.Id,
                Outcome = RepositoryUpdateOutcome.Failed,
                Message = message,
                FinalSnapshot = loaded.Snapshot
            },
            LastOperation = RepositoryOperationType.Update
        };

        var row = new RepositoryRowViewModel(failed);

        row.Activity.Should().Be(RepositoryActivity.Failed);
        row.ActivityText.Should().Be("Update failed");
        row.DetailsGitError.Should().Be(message);
        row.DetailsLastOperation.Should().Be("Update failed");
    }

    [Fact]
    public void Failed_fetch_activity_stays_compact_with_error_in_details()
    {
        const string error = "fatal: unable to access 'https://example.invalid/': boom";
        var loaded = Item();
        var failed = loaded with
        {
            FetchError = error,
            LastOperation = RepositoryOperationType.Fetch
        };

        var row = new RepositoryRowViewModel(failed);

        row.Activity.Should().Be(RepositoryActivity.Failed);
        row.ActivityText.Should().Be("Fetch failed");
        row.DetailsGitError.Should().Be(error);
        row.DetailsLastOperation.Should().Be("Fetch failed");
    }

    [Fact]
    public void RefreshTimeDisplay_marks_row_stale_without_new_git_data()
    {
        var start = DateTimeOffset.UtcNow;
        var clock = new ManualTimeProvider { Now = start };
        var row = new RepositoryRowViewModel(Item(lastFetch: start))
        {
            TimeProvider = clock
        };

        row.IsStale.Should().BeFalse();

        clock.Now = start.AddHours(25);
        row.RefreshTimeDisplay();

        row.IsStale.Should().BeTrue();
        row.StaleText.Should().Be("Remote state may be stale");
        row.LastFetchText.Should().Be("1 d ago");
    }

    [Fact]
    public void RefreshTimeDisplay_preserves_in_progress_activity()
    {
        var row = new RepositoryRowViewModel(Item(lastFetch: DateTimeOffset.UtcNow));
        row.SetActivity(RepositoryActivity.Fetching, "Fetching...");

        row.RefreshTimeDisplay();

        row.Activity.Should().Be(RepositoryActivity.Fetching);
        row.ActivityText.Should().Be("Fetching...");
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
