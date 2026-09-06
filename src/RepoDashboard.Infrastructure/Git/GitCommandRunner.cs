using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Lifetime;

namespace RepoDashboard.Infrastructure.Git;

/// <summary>
/// Executes <c>git.exe</c> directly (never via a shell) with structured
/// logging (Task 41): command, duration, exit code. Standard output and
/// standard error are never logged — output can be large and stderr can
/// embed credential-bearing URLs. The working directory is never logged
/// either (it can contain user names). Callers log repository identity
/// plus the classified <see cref="GitFailureKind"/> instead.
/// <para/>
/// Every spawned process observes both the caller's token (user Cancel)
/// and the application shutdown token (Task 44). Post-commit work
/// deliberately ignores user cancellation by passing a shutdown-only
/// token, yet its Git processes are still killed when the application
/// exits — shutdown can never be bypassed with
/// <see cref="CancellationToken.None"/>.
/// </summary>
public sealed class GitCommandRunner : IGitCommandRunner
{
    private readonly ILogger<GitCommandRunner> _logger;
    private readonly CancellationToken _shutdownToken;

    public GitCommandRunner(
        ILogger<GitCommandRunner>? logger = null,
        IApplicationShutdown? applicationShutdown = null)
    {
        _logger = logger ?? NullLogger<GitCommandRunner>.Instance;
        _shutdownToken = applicationShutdown?.ShutdownToken ?? CancellationToken.None;
    }

    public async Task<GitCommandResult> ExecuteAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(arguments);

        // Observe shutdown for every process: link the caller's (user-cancel)
        // token with the application lifetime token. Post-commit work passes
        // a shutdown-only token (ignoring Cancel) yet is still killed when
        // the application exits. When no lifetime is wired (unit tests) the
        // shutdown side is None and behaviour is unchanged.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _shutdownToken);
        var effectiveToken = linked.Token;

        // Never launch git.exe for an already-cancelled operation
        // (either user Cancel or shutdown).
        effectiveToken.ThrowIfCancellationRequested();

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
        // Registered on the linked (user + shutdown) token so shutdown
        // kills even post-commit processes that ignore user Cancel.
        using var killRegistration = effectiveToken.Register(
            () => TryKillProcessTree(process));

        try
        {
            var outputTask =
                process.StandardOutput.ReadToEndAsync(effectiveToken);

            var errorTask =
                process.StandardError.ReadToEndAsync(effectiveToken);

            await process.WaitForExitAsync(effectiveToken);

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
