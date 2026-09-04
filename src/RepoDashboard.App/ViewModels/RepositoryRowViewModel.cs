using CommunityToolkit.Mvvm.ComponentModel;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Models;

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
        Branch = FormatBranch(item.Snapshot);
        WorktreeStatus = FormatWorktreeStatus(item.Snapshot);
        UpstreamStatus = FormatDivergence(item.Snapshot.UpstreamDivergence);
        DefaultBranchStatus = FormatDefaultBranchStatus(item.Snapshot);
        UpdateStatus = item.UpdateDecision.Eligibility.ToString();
        Explanation = item.UpdateDecision.Explanation;
        LastFetchText = FormatLastFetch(item.LastSuccessfulFetch);
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
