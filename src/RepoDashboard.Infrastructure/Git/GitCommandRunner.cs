using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RepoDashboard.Core.Git;

namespace RepoDashboard.Infrastructure.Git;

/// <summary>
/// Executes <c>git.exe</c> directly (never via a shell) with structured
/// logging (Task 41): command, duration, exit code. Standard output is
/// never logged — it can be large and is not diagnostic. Standard error
/// is logged only on failure and only at Debug level, and never includes
/// secrets: arguments here are fixed Git verbs, never credentials or
/// environment variables.
/// </summary>
public sealed class GitCommandRunner : IGitCommandRunner
{
    private readonly ILogger<GitCommandRunner> _logger;

    public GitCommandRunner(ILogger<GitCommandRunner>? logger = null)
    {
        _logger = logger ?? NullLogger<GitCommandRunner>.Instance;
    }

    public async Task<GitCommandResult> ExecuteAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(arguments);

        // Never launch git.exe for an already-cancelled operation.
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };

        var stopwatch = Stopwatch.StartNew();

        process.Start();

        try
        {
            var outputTask =
                process.StandardOutput.ReadToEndAsync(cancellationToken);

            var errorTask =
                process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;

            stopwatch.Stop();

            // Debug only: success is routine, failures are warned by callers
            // (fetcher/updater) with repository context. Arguments are safe
            // Git verbs — never credentials — so logging them is fine.
            _logger.LogDebug(
                "git {Arguments} in {Path} exited {ExitCode} in {DurationMs} ms",
                string.Join(' ', arguments), repositoryPath,
                process.ExitCode, stopwatch.Elapsed.TotalMilliseconds);

            return new GitCommandResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = output,
                StandardError = error,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // Process exited between HasExited and Kill.
                }
                catch (Win32Exception) when (process.HasExited)
                {
                    // Process finished while Kill was being requested.
                }
            }

            if (!process.HasExited)
            {
                // Kill is fire-and-forget: confirm the git process is gone
                // before returning, so callers never race index.lock/handles.
                // CancellationToken.None is intentional — cleanup itself
                // must not be cancellable.
                await process.WaitForExitAsync(CancellationToken.None);
            }

            throw;
        }
    }
}
