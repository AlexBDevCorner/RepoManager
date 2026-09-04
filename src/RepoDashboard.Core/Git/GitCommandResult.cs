namespace RepoDashboard.Core.Git;

public sealed record GitCommandResult
{
    public required int ExitCode { get; init; }

    public required string StandardOutput { get; init; }

    public required string StandardError { get; init; }

    public required TimeSpan Duration { get; init; }

    public bool Success => ExitCode == 0;
}
