# Task 3 — Implement GitCommandRunner

- Milestone: 1 — Git foundation
- Type: backend
- Priority: critical — everything talking to Git goes through this class.

## Interface (Core)

```csharp
public interface IGitCommandRunner
{
    Task<GitCommandResult> ExecuteAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}
```

## Result

```csharp
public sealed record GitCommandResult
{
    public required int ExitCode { get; init; }

    public required string StandardOutput { get; init; }

    public required string StandardError { get; init; }

    public required TimeSpan Duration { get; init; }

    public bool Success => ExitCode == 0;
}
```

Do not throw simply because Git returned exit code `1`. Example: `git symbolic-ref HEAD` failing may mean HEAD is detached — valid state. Reserve exceptions for `git.exe` cannot start, cancellation, serious OS/process failures.

## Implementation (Infrastructure)

```csharp
public sealed class GitCommandRunner : IGitCommandRunner
{
    public async Task<GitCommandResult> ExecuteAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
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

        var outputTask =
            process.StandardOutput.ReadToEndAsync(cancellationToken);

        var errorTask =
            process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

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
}
```

## Rules

- Use `ArgumentList`, not `Arguments = $"fetch {remote}"` — avoids quoting issues.
- Execute `git.exe` directly, never via `cmd.exe` / `powershell.exe`.
- Runner knows working directory / args / stdout / stderr / exit code only — no `fetch`/`pull`/`ahead`/`behind` business concepts (see architectural rules).

## Acceptance criteria

- [ ] Git commands execute with captured stdout/stderr/exit code/duration.
- [ ] Non-zero exit is returned, not thrown (except infra failures).
- [ ] Cancellation kills the process tree.
