using RepoDashboard.Core.Models;

namespace RepoDashboard.Core.Sync;

/// <summary>
/// Pure classifier for safe updates. No IO, no Git execution —
/// the decision order is: missing → invalid → detached → operation →
/// dirty → no upstream → different remote → divergence.
/// </summary>
public sealed class UpdateEligibilityClassifier : IUpdateEligibilityClassifier
{
    private static string DescribeOperation(RepositorySnapshot snapshot)
    {
        if (snapshot.MergeInProgress)
        {
            return "merge";
        }

        if (snapshot.RebaseInProgress)
        {
            return "rebase";
        }

        return "cherry-pick";
    }

    public UpdateDecision Classify(
        RepositoryConfiguration configuration,
        RepositorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.DirectoryExists)
        {
            return new(
                UpdateEligibility.RepositoryMissing,
                $"Repository directory '{configuration.Path}' does not exist. " +
                "Automatic update is unavailable.");
        }

        if (!snapshot.IsGitRepository)
        {
            return new(
                UpdateEligibility.InvalidRepository,
                $"Directory '{configuration.Path}' is not a Git repository. " +
                "Automatic update is unavailable.");
        }

        if (snapshot.IsDetachedHead)
        {
            var detachedAt = string.IsNullOrWhiteSpace(snapshot.DetachedHeadSha)
                ? "HEAD is detached."
                : $"HEAD is detached at '{snapshot.DetachedHeadSha}'.";
            return new(
                UpdateEligibility.DetachedHead,
                $"{detachedAt} Automatic update needs a branch " +
                "with an upstream, so it was skipped.");
        }

        if (snapshot.MergeInProgress
            || snapshot.RebaseInProgress
            || snapshot.CherryPickInProgress)
        {
            return new(
                UpdateEligibility.OperationInProgress,
                $"A Git {DescribeOperation(snapshot)} is in progress. " +
                "Finish or abort it first. Automatic update was skipped.");
        }

        if (snapshot.IsDirty)
        {
            return new(
                UpdateEligibility.Dirty,
                "The working tree contains uncommitted changes. " +
                "Automatic update was skipped to avoid touching your edits.");
        }

        var branch = string.IsNullOrWhiteSpace(snapshot.CurrentBranch)
            ? "Current branch"
            : $"Branch '{snapshot.CurrentBranch}'";

        if (snapshot.UpstreamRef is null)
        {
            return new(
                UpdateEligibility.NoUpstream,
                $"{branch} does not track a remote branch. " +
                "Automatic update is unavailable.");
        }

        if (!string.Equals(
                snapshot.UpstreamRemote,
                configuration.PreferredRemote,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                UpdateEligibility.UpstreamUsesDifferentRemote,
                $"Current branch tracks '{snapshot.UpstreamRef}' on remote " +
                $"'{snapshot.UpstreamRemote}', but this repository uses " +
                $"'{configuration.PreferredRemote}'. " +
                "Automatic update was skipped.");
        }

        var divergence =
            snapshot.UpstreamDivergence;

        if (divergence is null)
        {
            return new(
                UpdateEligibility.Unknown,
                $"Upstream divergence of '{snapshot.UpstreamRef}' " +
                "could not be determined. Automatic update was skipped.");
        }

        if (divergence.Ahead == 0
            && divergence.Behind == 0)
        {
            return new(
                UpdateEligibility.AlreadyUpToDate,
                $"Current branch is already up to date with " +
                $"'{snapshot.UpstreamRef}'. There is nothing to pull.");
        }

        if (divergence.Ahead > 0
            && divergence.Behind == 0)
        {
            return new(
                UpdateEligibility.Ahead,
                $"Local branch has {divergence.Ahead} commit(s) that are " +
                $"not on '{snapshot.UpstreamRef}'. " +
                "There is nothing to pull.");
        }

        if (divergence.Ahead > 0
            && divergence.Behind > 0)
        {
            return new(
                UpdateEligibility.Diverged,
                $"Local branch: +{divergence.Ahead} commit(s). " +
                $"Remote branch '{snapshot.UpstreamRef}': " +
                $"+{divergence.Behind} commit(s). " +
                "Manual merge or rebase is required. " +
                "Automatic update was skipped.");
        }

        return new(
            UpdateEligibility.CanFastForward,
            $"Current branch can fast-forward by " +
            $"{divergence.Behind} commit(s) from '{snapshot.UpstreamRef}'.");
    }
}
