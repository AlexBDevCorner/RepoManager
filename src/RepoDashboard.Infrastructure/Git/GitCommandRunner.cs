using System.Diagnostics;
using RepoDashboard.Core.Git;

namespace RepoDashboard.Infrastructure.Git;

public sealed class GitCommandRunner : IGitCommandRunner
{
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
                process.Kill(entireProcessTree: true);

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
