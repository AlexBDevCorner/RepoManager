using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.Infrastructure.Git;

/// <summary>
/// Executes <c>git fetch &lt;preferredRemote&gt; --prune</c> via
/// <see cref="IGitCommandRunner"/>. Credentials are handled by
/// Git Credential Manager or SSH — interactive terminal output
/// is never parsed. A non-zero Git exit becomes a failed
/// <see cref="RepositoryOperationResult"/>, not an exception, so
/// batch operations can collect every result.
/// Structured logging (Task 41) records repository, operation,
/// duration, exit code and outcome. Secrets are never logged:
/// only the repository name, remote name, exit code, duration and
/// Git's own error text are emitted — never environment variables,
/// config values, or credential material.
/// </summary>
public sealed class RepositoryFetcher : IRepositoryFetcher
{
    private readonly IGitCommandRunner _runner;
    private readonly ILogger<RepositoryFetcher> _logger;

    public RepositoryFetcher(
        IGitCommandRunner runner,
        ILogger<RepositoryFetcher>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _logger = logger ?? NullLogger<RepositoryFetcher>.Instance;
    }

    public async Task<RepositoryOperationResult> FetchAsync(
        RepositoryConfiguration repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var remote = string.IsNullOrWhiteSpace(repository.PreferredRemote)
            ? "origin"
            : repository.PreferredRemote.Trim();

        _logger.LogInformation(
            "Fetching repository {Repository} (remote {Remote})",
            repository.Name, remote);

        var stopwatch = Stopwatch.StartNew();

        GitCommandResult result;

        try
        {
            result = await _runner.ExecuteAsync(
                repository.Path,
                ["fetch", remote, "--prune"],
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Fetch cancelled for {Repository} after {DurationMs} ms",
                repository.Name, stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            // The Git process never ran (for example the directory is
            // missing): report it as a failed operation so batch fetches
            // keep going instead of aborting on one broken entry.
            stopwatch.Stop();

            _logger.LogWarning(
                "Fetch failed for {Repository}: git process did not run ({Error}). "
                + "Duration {DurationMs} ms",
                repository.Name, ex.Message, stopwatch.Elapsed.TotalMilliseconds);

            return new RepositoryOperationResult
            {
                Success = false,
                Operation = RepositoryOperationType.Fetch,
                Message = $"Could not fetch '{remote}' for '{repository.Name}': {ex.Message}",
                FriendlyHint = GitErrorClassifier.GetFriendlyHint(ex.Message),
                ExitCode = null,
                Duration = stopwatch.Elapsed
            };
        }

        var rawOutput = CombineOutput(result.StandardOutput, result.StandardError);

        if (result.Success)
        {
            _logger.LogInformation(
                "Fetch completed for {Repository} in {DurationSec:F2} sec (exit {ExitCode})",
                repository.Name, result.Duration.TotalSeconds, result.ExitCode);

            return new RepositoryOperationResult
            {
                Success = true,
                Operation = RepositoryOperationType.Fetch,
                Message = $"Fetched '{remote}' (pruned).",
                RawOutput = rawOutput,
                ExitCode = result.ExitCode,
                Duration = result.Duration
            };
        }

        var detail = FirstNonEmpty(
            result.StandardError,
            result.StandardOutput);

        var hint = GitErrorClassifier.GetFriendlyHint(rawOutput ?? detail);

        var message = string.IsNullOrEmpty(detail)
            ? $"git fetch {remote} --prune failed with exit code {result.ExitCode}."
            : $"git fetch {remote} --prune failed with exit code {result.ExitCode}: {detail}";

        if (hint is not null)
        {
            message = $"{hint} {message}";
        }

        _logger.LogWarning(
            "Fetch failed for {Repository}. Git exit code {ExitCode}. "
            + "Duration {DurationSec:F2} sec. Error: {Error}",
            repository.Name, result.ExitCode,
            result.Duration.TotalSeconds, detail);

        return new RepositoryOperationResult
        {
            Success = false,
            Operation = RepositoryOperationType.Fetch,
            Message = message,
            RawOutput = rawOutput,
            FriendlyHint = hint,
            ExitCode = result.ExitCode,
            Duration = result.Duration
        };
    }

    private static string? CombineOutput(string standardOutput, string standardError)
    {
        var combined = string.Concat(
            standardOutput,
            standardError.Length > 0 && standardOutput.Length > 0 ? "\n" : string.Empty,
            standardError).Trim();

        return combined.Length == 0 ? null : combined;
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
