using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Repositories;

namespace RepoDashboard.Infrastructure.Git;

/// <summary>
/// Read-only repository inspection. Observes via machine-readable Git output
/// (<c>rev-parse</c>, <c>symbolic-ref</c>, <c>status --porcelain</c>,
/// <c>show-ref</c>, <c>rev-list --count</c>) and direct reads of well-known
/// files inside the Git directory. Never fetches, pulls, checks out, merges,
/// rebases, resets or stashes.
/// </summary>
public sealed class RepositoryInspector : IRepositoryInspector
{
    private readonly IGitCommandRunner _runner;
    private readonly IDivergenceCalculator _divergenceCalculator;

    public RepositoryInspector(
        IGitCommandRunner runner,
        IDivergenceCalculator divergenceCalculator)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(divergenceCalculator);
        _runner = runner;
        _divergenceCalculator = divergenceCalculator;
    }

    public async Task<RepositorySnapshot> InspectAsync(
        RepositoryConfiguration repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var inspectedAt = DateTimeOffset.UtcNow;

        if (!Directory.Exists(repository.Path))
        {
            return new RepositorySnapshot
            {
                RepositoryId = repository.Id,
                Path = repository.Path,
                DirectoryExists = false,
                IsGitRepository = false,
                InspectedAt = inspectedAt
            };
        }

        if (!await IsGitRepositoryAsync(repository.Path, cancellationToken))
        {
            return new RepositorySnapshot
            {
                RepositoryId = repository.Id,
                Path = repository.Path,
                DirectoryExists = true,
                IsGitRepository = false,
                InspectedAt = inspectedAt
            };
        }

        var (currentBranch, isDetachedHead, detachedHeadSha) =
            await GetBranchAsync(repository.Path, cancellationToken);

        var isDirty = await GetDirtyStateAsync(repository.Path, cancellationToken);

        var gitDirectory = await GetGitDirectoryAsync(repository.Path, cancellationToken);

        var (mergeInProgress, rebaseInProgress, cherryPickInProgress) =
            GetOperationState(gitDirectory);

        var (upstreamRef, upstreamRemote, upstreamBranch) =
            await GetUpstreamAsync(repository.Path, cancellationToken);

        var preferredRemote = string.IsNullOrWhiteSpace(repository.PreferredRemote)
            ? "origin"
            : repository.PreferredRemote.Trim();

        var defaultBranch = await ResolveDefaultBranchAsync(
            repository, preferredRemote, cancellationToken);

        Divergence? upstreamDivergence = null;

        if (upstreamRef is not null)
        {
            upstreamDivergence =
                await _divergenceCalculator.CalculateAsync(
                    repository.Path,
                    "HEAD",
                    upstreamRef,
                    cancellationToken);
        }

        Divergence? defaultBranchDivergence = null;

        if (defaultBranch is not null)
        {
            defaultBranchDivergence =
                await _divergenceCalculator.CalculateAsync(
                    repository.Path,
                    "HEAD",
                    $"{preferredRemote}/{defaultBranch}",
                    cancellationToken);
        }

        return new RepositorySnapshot
        {
            RepositoryId = repository.Id,
            Path = repository.Path,
            DirectoryExists = true,
            IsGitRepository = true,
            CurrentBranch = currentBranch,
            IsDetachedHead = isDetachedHead,
            DetachedHeadSha = detachedHeadSha,
            IsDirty = isDirty,
            UpstreamRef = upstreamRef,
            UpstreamRemote = upstreamRemote,
            UpstreamBranch = upstreamBranch,
            DefaultRemoteBranch = defaultBranch,
            UpstreamDivergence = upstreamDivergence,
            DefaultBranchDivergence = defaultBranchDivergence,
            MergeInProgress = mergeInProgress,
            RebaseInProgress = rebaseInProgress,
            CherryPickInProgress = cherryPickInProgress,
            InspectedAt = inspectedAt
        };
    }

    /// <summary>
    /// Asks Git itself whether the folder is inside a work tree.
    /// Never guesses from the presence of a <c>.git</c> entry —
    /// worktrees and other configurations do not look like a plain folder.
    /// </summary>
    private async Task<bool> IsGitRepositoryAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var result = await _runner.ExecuteAsync(
            repositoryPath,
            ["rev-parse", "--is-inside-work-tree"],
            cancellationToken);

        return result.Success &&
            result.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(string? CurrentBranch, bool IsDetachedHead, string? DetachedHeadSha)> GetBranchAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var symbolicRef = await _runner.ExecuteAsync(
            repositoryPath,
            ["symbolic-ref", "--quiet", "--short", "HEAD"],
            cancellationToken);

        if (symbolicRef.Success)
        {
            return (symbolicRef.StandardOutput.Trim(), false, null);
        }

        var headSha = await _runner.ExecuteAsync(
            repositoryPath,
            ["rev-parse", "--short", "HEAD"],
            cancellationToken);

        if (!headSha.Success)
        {
            throw new InvalidOperationException(
                $"Unable to determine HEAD in '{repositoryPath}': {headSha.StandardError.Trim()}");
        }

        return (null, true, headSha.StandardOutput.Trim());
    }

    private async Task<bool> GetDirtyStateAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var result = await _runner.ExecuteAsync(
            repositoryPath,
            ["status", "--porcelain=v2"],
            cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"git status in '{repositoryPath}' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        // Header lines (starting with '#') describe the branch, not changes.
        // Any other non-empty line is a modified, staged, deleted
        // or untracked entry — all of them make the tree dirty.
        var changeLines = result.StandardOutput
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith("#", StringComparison.Ordinal));

        return changeLines.Any();
    }

    private async Task<string> GetGitDirectoryAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var result = await _runner.ExecuteAsync(
            repositoryPath,
            ["rev-parse", "--absolute-git-dir"],
            cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"git rev-parse --absolute-git-dir in '{repositoryPath}' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        return result.StandardOutput.Trim();
    }

    internal static (bool MergeInProgress, bool RebaseInProgress, bool CherryPickInProgress) GetOperationState(
        string gitDirectory)
    {
        var mergeInProgress =
            File.Exists(Path.Combine(gitDirectory, "MERGE_HEAD"));

        var cherryPickInProgress =
            File.Exists(Path.Combine(gitDirectory, "CHERRY_PICK_HEAD"));

        var rebaseInProgress =
            Directory.Exists(Path.Combine(gitDirectory, "rebase-merge"))
            || Directory.Exists(Path.Combine(gitDirectory, "rebase-apply"));

        return (mergeInProgress, rebaseInProgress, cherryPickInProgress);
    }

    private async Task<(string? UpstreamRef, string? UpstreamRemote, string? UpstreamBranch)> GetUpstreamAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var result = await _runner.ExecuteAsync(
            repositoryPath,
            [
                "rev-parse",
                "--abbrev-ref",
                "--symbolic-full-name",
                "@{upstream}"
            ],
            cancellationToken);

        // A failure normally means no upstream is configured —
        // a valid state, not an application error.
        if (!result.Success)
        {
            return (null, null, null);
        }

        var upstream = result.StandardOutput.Trim();

        // Split on the FIRST slash: branch names may contain slashes,
        // so origin/feature/search is remote 'origin' + branch 'feature/search'.
        var separatorIndex = upstream.IndexOf('/');

        if (separatorIndex <= 0 || separatorIndex == upstream.Length - 1)
        {
            // No remote part (for example a branch tracking another
            // local branch): keep the full ref as the branch name.
            return (upstream, null, upstream);
        }

        var remote = upstream[..separatorIndex];
        var branch = upstream[(separatorIndex + 1)..];

        return (upstream, remote, branch);
    }

    /// <summary>
    /// Resolves the preferred remote's default branch without hard-coding
    /// <c>main</c> or <c>master</c>: explicit override first, then the remote
    /// HEAD symbolic ref, then <c>main</c>, then <c>master</c>.
    /// Returns null when nothing resolves — never a guess.
    /// </summary>
    private async Task<string?> ResolveDefaultBranchAsync(
        RepositoryConfiguration repository,
        string preferredRemote,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(repository.DefaultBranchOverride))
        {
            return repository.DefaultBranchOverride.Trim();
        }

        var remoteHead = await _runner.ExecuteAsync(
            repository.Path,
            ["symbolic-ref", "--quiet", "--short", $"refs/remotes/{preferredRemote}/HEAD"],
            cancellationToken);

        if (remoteHead.Success)
        {
            var full = remoteHead.StandardOutput.Trim();

            // Example: origin/main -> main.
            var separatorIndex = full.IndexOf('/');

            if (separatorIndex >= 0 && separatorIndex < full.Length - 1)
            {
                return full[(separatorIndex + 1)..];
            }

            return full;
        }

        foreach (var candidate in (string[])["main", "master"])
        {
            var showRef = await _runner.ExecuteAsync(
                repository.Path,
                ["show-ref", "--verify", "--quiet", $"refs/remotes/{preferredRemote}/{candidate}"],
                cancellationToken);

            if (showRef.Success)
            {
                return candidate;
            }
        }

        return null;
    }
}
