using CommunityToolkit.Mvvm.ComponentModel;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.App.ViewModels;

/// <summary>
/// Presentation-only row for one dashboard item.
/// Maps <see cref="RepositoryDashboardItem"/> to display strings;
/// contains no Git business rules.
/// </summary>
public sealed partial class RepositoryRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _branch = string.Empty;

    [ObservableProperty]
    private string _worktreeStatus = string.Empty;

    [ObservableProperty]
    private string _upstreamStatus = string.Empty;

    [ObservableProperty]
    private string _defaultBranchStatus = string.Empty;

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private string _explanation = string.Empty;

    [ObservableProperty]
    private string _lastFetchText = string.Empty;

    [ObservableProperty]
    private RepositoryActivity _activity = RepositoryActivity.Idle;

    [ObservableProperty]
    private string _activityText = string.Empty;

    [ObservableProperty]
    private string _detailsPath = string.Empty;

    [ObservableProperty]
    private string _detailsBranch = string.Empty;

    [ObservableProperty]
    private string _detailsUpstream = string.Empty;

    [ObservableProperty]
    private string _detailsRemoteDefault = string.Empty;

    [ObservableProperty]
    private string _detailsVsUpstream = string.Empty;

    [ObservableProperty]
    private string _detailsVsDefault = string.Empty;

    [ObservableProperty]
    private string _detailsWorkingTree = string.Empty;

    [ObservableProperty]
    private string _detailsLastFetch = string.Empty;

    [ObservableProperty]
    private string _detailsLastOperation = string.Empty;

    [ObservableProperty]
    private string _detailsRemote = string.Empty;

    [ObservableProperty]
    private string _detailsGitError = string.Empty;

    [ObservableProperty]
    private bool _isStale;

    [ObservableProperty]
    private string _staleText = string.Empty;

    /// <summary>
    /// Remote-tracking refs older than this are flagged as potentially
    /// stale: until the next fetch they may not represent the server.
    /// </summary>
    public static TimeSpan StaleAfter { get; } = TimeSpan.FromHours(24);

    public Guid RepositoryId { get; private set; }

    public RepositoryRowViewModel(RepositoryDashboardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Update(item);
    }

    /// <summary>
    /// Re-maps the row in place after a refresh, preserving selection.
    /// </summary>
    public void Update(RepositoryDashboardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        RepositoryId = item.Configuration.Id;
        Name = item.Configuration.Name;

        // A failed inspection keeps the row visible but shows no Git state:
        // the snapshot carries identity only, so render the error instead.
        if (item.InspectionError is not null)
        {
            Branch = "—";
            WorktreeStatus = "Error";
            UpstreamStatus = "—";
            DefaultBranchStatus = "—";
            UpdateStatus = UpdateEligibility.Unknown.ToString();
            Explanation = item.InspectionError;
            LastFetchText = FormatLastFetch(item.LastSuccessfulFetch);
            MapTerminalActivity(item);
            MapDetails(item);
            return;
        }

        Branch = FormatBranch(item.Snapshot);
        WorktreeStatus = FormatWorktreeStatus(item.Snapshot);
        UpstreamStatus = FormatDivergence(item.Snapshot.UpstreamDivergence);
        DefaultBranchStatus = FormatDefaultBranchStatus(item.Snapshot);
        UpdateStatus = item.UpdateDecision.Eligibility.ToString();
        Explanation = item.UpdateDecision.Explanation;
        LastFetchText = FormatLastFetch(item.LastSuccessfulFetch);
        MapTerminalActivity(item);
        MapDetails(item);
    }

    /// <summary>
    /// Drives the transient in-progress state while an operation runs
    /// (for example "Fetching..."). The next <see cref="Update"/> call
    /// replaces it with the terminal state derived from the fresh item.
    /// </summary>
    public void SetActivity(RepositoryActivity activity, string activityText)
    {
        Activity = activity;
        ActivityText = activityText;
    }

    /// <summary>
    /// Derives the terminal inline activity from the fresh dashboard item:
    /// update outcomes win over fetch errors, fetch errors win over idle.
    /// A plain refresh/load carries neither, so the row returns to idle.
    /// </summary>
    private void MapTerminalActivity(RepositoryDashboardItem item)
    {
        if (item.UpdateResult is not null)
        {
            switch (item.UpdateResult.Outcome)
            {
                case RepositoryUpdateOutcome.Updated:
                    Activity = RepositoryActivity.Completed;
                    ActivityText = "Updated";
                    return;
                case RepositoryUpdateOutcome.Skipped:
                    Activity = RepositoryActivity.Skipped;
                    ActivityText = string.IsNullOrWhiteSpace(item.UpdateResult.Message)
                        ? "Skipped"
                        : $"Skipped — {item.UpdateResult.Message}";
                    return;
                default:
                    Activity = RepositoryActivity.Failed;
                    ActivityText = string.IsNullOrWhiteSpace(item.UpdateResult.Message)
                        ? "Update failed"
                        : $"Update failed — {item.UpdateResult.Message}";
                    return;
            }
        }

        if (item.FetchError is not null)
        {
            Activity = RepositoryActivity.Failed;
            ActivityText = $"Fetch failed — {item.FetchError}";
            return;
        }

        if (item.InspectionError is not null)
        {
            Activity = RepositoryActivity.Failed;
            ActivityText = $"Failed — {item.InspectionError}";
            return;
        }

        Activity = RepositoryActivity.Idle;
        ActivityText = string.Empty;
    }

    /// <summary>
    /// Maps the selection details panel (Task 36) plus the stale-remote
    /// indicator (Task 39). Keeps the table compact while exposing
    /// diagnostics, including the raw Git error on failure.
    /// </summary>
    private void MapDetails(RepositoryDashboardItem item)
    {
        DetailsPath = item.Configuration.Path;
        DetailsRemote = string.IsNullOrWhiteSpace(item.Configuration.PreferredRemote)
            ? "origin"
            : item.Configuration.PreferredRemote;

        if (item.InspectionError is null)
        {
            var snapshot = item.Snapshot;
            DetailsBranch = Branch;
            DetailsUpstream = string.IsNullOrWhiteSpace(snapshot.UpstreamRef)
                ? "—"
                : snapshot.UpstreamRef;
            DetailsRemoteDefault = string.IsNullOrWhiteSpace(snapshot.DefaultRemoteBranch)
                ? "—"
                : $"{DetailsRemote}/{snapshot.DefaultRemoteBranch}";
            DetailsVsUpstream = FormatAheadBehind(snapshot.UpstreamDivergence);
            DetailsVsDefault = FormatAheadBehind(snapshot.DefaultBranchDivergence);
            DetailsWorkingTree = WorktreeStatus;
        }
        else
        {
            DetailsBranch = "—";
            DetailsUpstream = "—";
            DetailsRemoteDefault = "—";
            DetailsVsUpstream = "—";
            DetailsVsDefault = "—";
            DetailsWorkingTree = "Error";
        }

        DetailsLastFetch = item.LastSuccessfulFetch?.ToLocalTime().ToString("g") ?? "Never";
        DetailsLastOperation = DescribeLastOperation(item);
        DetailsGitError = item.InspectionError
            ?? item.FetchError
            ?? (item.UpdateResult?.Outcome == RepositoryUpdateOutcome.Failed
                ? item.UpdateResult.Message
                : null)
            ?? string.Empty;

        MapStaleness(item.LastSuccessfulFetch);
    }

    private static string FormatAheadBehind(Divergence? divergence)
    {
        if (divergence is null)
        {
            return "—";
        }

        if (divergence.Ahead == 0 && divergence.Behind == 0)
        {
            return "Up to date";
        }

        return $"{divergence.Ahead} ahead / {divergence.Behind} behind";
    }

    private static string DescribeLastOperation(RepositoryDashboardItem item)
    {
        if (item.UpdateResult is not null)
        {
            return item.UpdateResult.Outcome switch
            {
                RepositoryUpdateOutcome.Updated => "Update successful",
                RepositoryUpdateOutcome.Skipped => "Update skipped",
                _ => "Update failed"
            };
        }

        if (item.FetchError is not null)
        {
            return "Fetch failed";
        }

        if (item.InspectionError is not null)
        {
            return "Inspection failed";
        }

        return item.LastSuccessfulFetch is not null
            ? "Fetch successful"
            : "—";
    }

    /// <summary>
    /// Flags remote-tracking refs that may no longer represent the server:
    /// never fetched, or fetched longer ago than <see cref="StaleAfter"/>.
    /// </summary>
    private void MapStaleness(DateTimeOffset? lastFetch)
    {
        if (lastFetch is null)
        {
            IsStale = true;
            StaleText = "Remote state may be stale";
            return;
        }

        var age = DateTimeOffset.UtcNow - lastFetch.Value.ToUniversalTime();

        if (age > StaleAfter)
        {
            IsStale = true;
            StaleText = "Remote state may be stale";
            return;
        }

        IsStale = false;
        StaleText = string.Empty;
    }

    internal static string FormatDivergence(Divergence? divergence)
    {
        if (divergence is null)
        {
            return "—";
        }

        return $"↑{divergence.Ahead} ↓{divergence.Behind}";
    }

    private static string FormatBranch(RepositorySnapshot snapshot)
    {
        if (!snapshot.DirectoryExists || !snapshot.IsGitRepository)
        {
            return "—";
        }

        if (snapshot.IsDetachedHead)
        {
            return string.IsNullOrWhiteSpace(snapshot.DetachedHeadSha)
                ? "Detached HEAD"
                : $"Detached HEAD @ {snapshot.DetachedHeadSha}";
        }

        return string.IsNullOrWhiteSpace(snapshot.CurrentBranch)
            ? "—"
            : snapshot.CurrentBranch;
    }

    private static string FormatWorktreeStatus(RepositorySnapshot snapshot)
    {
        if (!snapshot.DirectoryExists)
        {
            return "Missing";
        }

        if (!snapshot.IsGitRepository)
        {
            return "—";
        }

        if (snapshot.MergeInProgress
            || snapshot.RebaseInProgress
            || snapshot.CherryPickInProgress)
        {
            return "Busy";
        }

        return snapshot.IsDirty ? "Dirty" : "Clean";
    }

    private static string FormatDefaultBranchStatus(RepositorySnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.DefaultRemoteBranch))
        {
            return "—";
        }

        return $"{snapshot.DefaultRemoteBranch} {FormatDivergence(snapshot.DefaultBranchDivergence)}";
    }

    private static string FormatLastFetch(DateTimeOffset? lastFetch)
    {
        if (lastFetch is null)
        {
            return "Never";
        }

        var age = DateTimeOffset.UtcNow - lastFetch.Value.ToUniversalTime();

        if (age < TimeSpan.FromMinutes(1))
        {
            return "Just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes} min ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return $"{(int)age.TotalHours} h ago";
        }

        if (age < TimeSpan.FromDays(30))
        {
            return $"{(int)age.TotalDays} d ago";
        }

        return lastFetch.Value.ToLocalTime().ToString("d");
    }
}
