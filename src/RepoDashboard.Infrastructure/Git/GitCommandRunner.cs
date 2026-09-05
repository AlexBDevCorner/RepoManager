using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RepoDashboard.Core.Git;

namespace RepoDashboard.Infrastructure.Git;

/// <summary>
/// Executes <c>git.exe</c> directly (never via a shell) with structured
/// logging (Task 41): command, duration, exit code. Standard output and
/// standard error are never logged — output can be large and stderr can
/// embed credential-bearing URLs. The working directory is never logged
/// either (it can contain user names). Callers log repository identity
/// plus the classified <see cref="GitFailureKind"/> instead.
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

        // Kill the process tree the moment cancellation is signalled —
        // synchronously from the token callback — instead of relying only
        // on the async catch block below. Shutdown (Task 44) is async-void
        // and cannot await our continuation before the process exits, so
        // without this the app could race process termination and orphan
        // git.exe. The catch block is kept as cleanup/wait.
        using var killRegistration = cancellationToken.Register(
            () => TryKillProcessTree(process));

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
            // (fetcher/updater) with repository context. Only the Git verb
            // is logged — never the full argument list (a remote argument
            // can be a credential-bearing URL), never the working directory
            // (may contain user names) and never stdout/stderr.
            var command = arguments.Count > 0 ? arguments[0] : "<none>";
            _logger.LogDebug(
                "git {Command} exited {ExitCode} in {DurationMs} ms",
                command,
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
            TryKillProcessTree(process);

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

    /// <summary>
    /// Best-effort synchronous process-tree kill. Safe to call from a
    /// cancellation callback (no awaits, no throws): races where the
    /// process already exited are swallowed via <c>HasExited</c> filters.
    /// </summary>
    internal static void TryKillProcessTree(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }
        }
        catch (InvalidOperationException)
        {
            // No process associated (already disposed/exited).
            return;
        }

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
        catch (InvalidOperationException)
        {
            // Process object already disposed — nothing to kill.
        }
        catch (Win32Exception)
        {
            // Access denied or already terminating — the catch-block wait
            // still gives the process a chance to exit on its own.
        }
        catch (NotSupportedException)
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // Best effort from a cancellation callback: never throw.
            }
        }
    }
}
