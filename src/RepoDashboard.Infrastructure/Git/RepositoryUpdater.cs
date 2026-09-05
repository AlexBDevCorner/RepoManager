using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.Infrastructure.Git;

/// <summary>
/// Safe automatic updates of a single repository. The only Git mutations
/// this component can ever perform are <c>fetch --prune</c> (via
/// <see cref="IRepositoryFetcher"/>) and, exclusively when the fresh
/// post-fetch snapshot classifies as fast-forwardable,
/// <c>pull --ff-only --no-rebase</c>. Classification is advisory only:
/// Git's own <c>--ff-only</c> refusal is the final safety net, surfaced
/// as a safe failure with no fallback to merge / rebase / reset / stash.
/// <para/>
/// Holds no locks itself — see <see cref="IRepositoryUpdater"/>.
/// </summary>
public sealed class RepositoryUpdater : IRepositoryUpdater
{
    private readonly IGitCommandRunner _runner;
    private readonly IRepositoryFetcher _fetcher;
    private readonly IRepositoryInspector _inspector;
    private readonly IUpdateEligibilityClassifier _classifier;
    private readonly ILogger<RepositoryUpdater> _logger;

    public RepositoryUpdater(
        IGitCommandRunner runner,
        IRepositoryFetcher fetcher,
        IRepositoryInspector inspector,
        IUpdateEligibilityClassifier classifier,
        ILogger<RepositoryUpdater>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(classifier);
        _runner = runner;
        _fetcher = fetcher;
        _inspector = inspector;
        _classifier = classifier;
        _logger = logger ?? NullLogger<RepositoryUpdater>.Instance;
    }

    public async Task<RepositoryUpdateResult> UpdateAsync(
        RepositoryConfiguration repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _logger.LogInformation(
            "Updating repository {Repository}", repository.Name);

        var stopwatch = Stopwatch.StartNew();

        // Step 1 — Fetch. Safe even when the working tree is dirty;
        // without it every divergence below would be stale.
        var fetchResult = await FetchBestEffortAsync(repository, cancellationToken);

        if (fetchResult is null)
        {
            // The fetcher threw unexpectedly (it normally maps Git failures
            // to failed results instead of throwing).
            stopwatch.Stop();
            _logger.LogWarning(
                "Update failed for {Repository}: fetch threw unexpectedly. "
                + "Duration {DurationSec:F2} sec",
                repository.Name, stopwatch.Elapsed.TotalSeconds);

            return new RepositoryUpdateResult
            {
                RepositoryId = repository.Id,
                Outcome = RepositoryUpdateOutcome.Failed,
                Message = "Could not fetch remote state.",
                FinalSnapshot = await InspectBestEffortAsync(repository, cancellationToken)
            };
        }

        if (!fetchResult.Success)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "Update failed for {Repository}: fetch failed with exit code {ExitCode}. "
                + "Duration {DurationSec:F2} sec. FailureKind: {FailureKind}",
                repository.Name, fetchResult.ExitCode,
                stopwatch.Elapsed.TotalSeconds,
                GitErrorClassifier.Classify(fetchResult.RawOutput ?? fetchResult.Message)?.ToString() ?? "Unknown");

            return new RepositoryUpdateResult
            {
                RepositoryId = repository.Id,
                Outcome = RepositoryUpdateOutcome.Failed,
                Message = fetchResult.Message,
                FetchResult = fetchResult,
                FriendlyHint = fetchResult.FriendlyHint,
                FinalSnapshot = await InspectBestEffortAsync(repository, cancellationToken)
            };
        }

        // Step 2 — Inspect from Git. Never patched manually.
        RepositorySnapshot snapshot;

        try
        {
            snapshot = await _inspector.InspectAsync(repository, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "Update failed for {Repository}: inspection failed ({ErrorType}). "
                + "Duration {DurationSec:F2} sec",
                repository.Name, ex.GetType().Name, stopwatch.Elapsed.TotalSeconds);

            return new RepositoryUpdateResult
            {
                RepositoryId = repository.Id,
                Outcome = RepositoryUpdateOutcome.Failed,
                Message = ex.Message,
                FetchResult = fetchResult,
                FriendlyHint = GitErrorClassifier.GetFriendlyHint(ex.Message),
                FinalSnapshot = null
            };
        }

        // Step 3 — Classify. Pure function, no IO.
        var decision = _classifier.Classify(repository, snapshot);

        // Step 4 — Skip anything that is not provably fast-forwardable.
        if (!decision.CanUpdate)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Update skipped for {Repository}: {Reason}. "
                + "Duration {DurationSec:F2} sec",
                repository.Name, decision.Eligibility,
                stopwatch.Elapsed.TotalSeconds);

            return new RepositoryUpdateResult
            {
                RepositoryId = repository.Id,
                Outcome = RepositoryUpdateOutcome.Skipped,
                Message = decision.Explanation,
                Decision = decision,
                FetchResult = fetchResult,
                FinalSnapshot = snapshot
            };
        }

        // Step 5 — Pull, fast-forward only. --no-rebase keeps a global
        // pull.rebase=true from turning this into a rebase. No remote or
        // branch arguments: the classifier already verified the upstream
        // is the preferred remote, so plain pull follows exactly that.
        GitCommandResult pull;

        try
        {
            pull = await _runner.ExecuteAsync(
                repository.Path,
                ["pull", "--ff-only", "--no-rebase"],
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Update cancelled for {Repository} after {DurationSec:F2} sec",
                repository.Name, stopwatch.Elapsed.TotalSeconds);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "Update failed for {Repository}: could not run git pull ({ErrorType}). "
                + "Duration {DurationSec:F2} sec",
                repository.Name, ex.GetType().Name, stopwatch.Elapsed.TotalSeconds);

            return new RepositoryUpdateResult
            {
                RepositoryId = repository.Id,
                Outcome = RepositoryUpdateOutcome.Failed,
                Message = $"Could not run git pull: {ex.Message}",
                Decision = decision,
                FetchResult = fetchResult,
                FriendlyHint = GitErrorClassifier.GetFriendlyHint(ex.Message),
                FinalSnapshot = await InspectBestEffortAsync(repository, cancellationToken)
            };
        }

        if (!pull.Success)
        {
            // State moved between classify and pull (or Git refused for any
            // other reason): a safe failure. Never fall back to another
            // strategy — especially not merge / rebase / reset / stash.
            var detail = FirstNonEmpty(pull.StandardError, pull.StandardOutput);
            var rawCombined = string.Concat(
                pull.StandardOutput,
                pull.StandardError.Length > 0 && pull.StandardOutput.Length > 0 ? "\n" : string.Empty,
                pull.StandardError).Trim();
            var hint = GitErrorClassifier.GetFriendlyHint(
                rawCombined.Length == 0 ? detail : rawCombined);

            var message = string.IsNullOrEmpty(detail)
                ? $"Update failed safely: git pull --ff-only --no-rebase failed with exit code {pull.ExitCode}."
                : $"Update failed safely: git pull --ff-only --no-rebase failed with exit code {pull.ExitCode}: {detail}";

            if (hint is not null)
            {
                message = $"{hint} {message}";
            }

            stopwatch.Stop();
            _logger.LogWarning(
                "Update failed for {Repository}. Git exit code {ExitCode}. "
                + "Duration {DurationSec:F2} sec. FailureKind: {FailureKind}",
                repository.Name, pull.ExitCode,
                stopwatch.Elapsed.TotalSeconds, hint is not null
                    ? GitErrorClassifier.Classify(rawCombined)?.ToString() ?? "Unknown"
                    : "Unknown");

            return new RepositoryUpdateResult
            {
                RepositoryId = repository.Id,
                Outcome = RepositoryUpdateOutcome.Failed,
                Message = message,
                Decision = decision,
                FetchResult = fetchResult,
                FriendlyHint = hint,
                FinalSnapshot = await InspectBestEffortAsync(repository, cancellationToken)
            };
        }

        // Step 6 — Reinspect. Expected final: Ahead 0, Behind 0 vs upstream.
        try
        {
            var final = await _inspector.InspectAsync(repository, cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation(
                "Update completed for {Repository} in {DurationSec:F2} sec",
                repository.Name, stopwatch.Elapsed.TotalSeconds);

            return new RepositoryUpdateResult
            {
                RepositoryId = repository.Id,
                Outcome = RepositoryUpdateOutcome.Updated,
                Message = FormatUpdatedMessage(snapshot),
                Decision = decision,
                FetchResult = fetchResult,
                FinalSnapshot = final
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The pull succeeded — reporting failure would lie. The caller
            // re-inspects when FinalSnapshot is null.
            stopwatch.Stop();
            _logger.LogInformation(
                "Update completed for {Repository} in {DurationSec:F2} sec "
                + "(final re-inspection failed: {ErrorType})",
                repository.Name, stopwatch.Elapsed.TotalSeconds, ex.GetType().Name);

            return new RepositoryUpdateResult
            {
                RepositoryId = repository.Id,
                Outcome = RepositoryUpdateOutcome.Updated,
                Message = $"{FormatUpdatedMessage(snapshot)} Final re-inspection failed: {ex.Message}",
                Decision = decision,
                FetchResult = fetchResult,
                FinalSnapshot = null
            };
        }
    }

    private async Task<RepositoryOperationResult?> FetchBestEffortAsync(
        RepositoryConfiguration repository,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _fetcher.FetchAsync(repository, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Best-effort inspection for failure paths: keeps the dashboard row
    /// populated with local state. Cancellation still propagates —
    /// it is never converted into an ordinary result.
    /// </summary>
    private async Task<RepositorySnapshot?> InspectBestEffortAsync(
        RepositoryConfiguration repository,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _inspector.InspectAsync(repository, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatUpdatedMessage(RepositorySnapshot before)
    {
        var branch = string.IsNullOrWhiteSpace(before.CurrentBranch)
            ? "HEAD"
            : before.CurrentBranch;

        var upstream = string.IsNullOrWhiteSpace(before.UpstreamRef)
            ? "upstream"
            : before.UpstreamRef;

        var behind = before.UpstreamDivergence?.Behind;

        return behind.HasValue
            ? $"Fast-forwarded '{branch}' by {behind.Value} commit(s) from '{upstream}'."
            : $"Fast-forwarded '{branch}' from '{upstream}'.";
    }

    private static string FirstNonEmpty(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var trimmed = candidate?.Trim();

            if (!string.IsNullOrEmpty(trimmed))
            {
                return trimmed;
            }
        }

        return string.Empty;
    }
}
