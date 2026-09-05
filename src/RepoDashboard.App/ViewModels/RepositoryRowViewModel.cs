using System;
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

    /// <summary>
    /// Clock for time-derived display text. Defaults to the system clock;
    /// tests substitute a fake to cross the staleness boundary.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    public Guid RepositoryId { get; private set; }

    private RepositoryDashboardItem? _lastItem;

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

        _lastItem = item;
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
    /// Recalculates only time-derived display text (relative fetch age and
    /// the stale indicator) from the last item. Never touches
    /// <see cref="Activity"/>: an in-progress operation keeps its transient
    /// state. Called periodically so staleness evolves while the app sits
    /// open, not just when Git runs.
    /// </summary>
    public void RefreshTimeDisplay()
    {
        if (_lastItem is null)
        {
            return;
        }

        LastFetchText = FormatLastFetch(_lastItem.LastSuccessfulFetch);
        DetailsLastFetch = _lastItem.LastSuccessfulFetch?.ToLocalTime().ToString("g") ?? "Never";
        MapStaleness(_lastItem.LastSuccessfulFetch);
    }

    /// <summary>
    /// Derives the terminal inline activity from the fresh dashboard item:
    /// update outcomes win over fetch errors, fetch errors win over idle.
    /// A plain load carries neither, so the row returns to idle; callers
    /// reporting a successful fetch/refresh apply their explicit
    /// <c>Completed</c> state afterwards. Text stays compact — full reasons
    /// live in the details area.
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
                    var reason = item.UpdateResult.Decision?.Eligibility
                        ?? item.UpdateDecision.Eligibility;
                    ActivityText = $"Skipped — {UpdateEligibilityLabels.Short(reason)}";
                    return;
                default:
                    Activity = RepositoryActivity.Failed;
                    ActivityText = "Update failed";
                    return;
            }
        }

        if (item.FetchError is not null)
        {
            Activity = RepositoryActivity.Failed;
            ActivityText = "Fetch failed";
            return;
        }

        if (item.InspectionError is not null)
        {
            Activity = RepositoryActivity.Failed;
            ActivityText = "Failed";
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

    /// <summary>
    /// Reports last operation from explicit operation identity, never by
    /// inferring it from a persisted timestamp: a historical
    /// <c>LastSuccessfulFetch</c> on a loaded or refreshed row must not
    /// read as "Fetch successful".
    /// </summary>
    private static string DescribeLastOperation(RepositoryDashboardItem item)
    {
        switch (item.LastOperation)
        {
            case RepositoryOperationType.Update when item.UpdateResult is not null:
                return item.UpdateResult.Outcome switch
                {
                    RepositoryUpdateOutcome.Updated => "Update successful",
                    RepositoryUpdateOutcome.Skipped => "Update skipped",
                    _ => "Update failed"
                };
            case RepositoryOperationType.Update:
                return "Update failed";
            case RepositoryOperationType.Fetch when item.FetchError is not null:
                return "Fetch failed";
            case RepositoryOperationType.Fetch when item.InspectionError is not null:
                return "Inspection failed";
            case RepositoryOperationType.Fetch:
                return "Fetch successful";
            case RepositoryOperationType.Refresh when item.InspectionError is not null:
                return "Inspection failed";
            case RepositoryOperationType.Refresh:
                return "Refreshed";
            default:
                return "—";
        }
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

        var age = TimeProvider.GetUtcNow() - lastFetch.Value.ToUniversalTime();

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

    private string FormatLastFetch(DateTimeOffset? lastFetch)
    {
        if (lastFetch is null)
        {
            return "Never";
        }

        var age = TimeProvider.GetUtcNow() - lastFetch.Value.ToUniversalTime();

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
