using System.Diagnostics;
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
/// </summary>
public sealed class RepositoryFetcher : IRepositoryFetcher
{
    private readonly IGitCommandRunner _runner;

    public RepositoryFetcher(IGitCommandRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<RepositoryOperationResult> FetchAsync(
        RepositoryConfiguration repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var remote = string.IsNullOrWhiteSpace(repository.PreferredRemote)
            ? "origin"
            : repository.PreferredRemote.Trim();

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
            throw;
        }
        catch (Exception ex)
        {
            // The Git process never ran (for example the directory is
            // missing): report it as a failed operation so batch fetches
            // keep going instead of aborting on one broken entry.
            stopwatch.Stop();

            return new RepositoryOperationResult
            {
                Success = false,
                Operation = RepositoryOperationType.Fetch,
                Message = $"Could not fetch '{remote}' for '{repository.Name}': {ex.Message}",
                ExitCode = null,
                Duration = stopwatch.Elapsed
            };
        }

        var rawOutput = CombineOutput(result.StandardOutput, result.StandardError);

        if (result.Success)
        {
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

        var message = string.IsNullOrEmpty(detail)
            ? $"git fetch {remote} --prune failed with exit code {result.ExitCode}."
            : $"git fetch {remote} --prune failed with exit code {result.ExitCode}: {detail}";

        return new RepositoryOperationResult
        {
            Success = false,
            Operation = RepositoryOperationType.Fetch,
            Message = message,
            RawOutput = rawOutput,
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
