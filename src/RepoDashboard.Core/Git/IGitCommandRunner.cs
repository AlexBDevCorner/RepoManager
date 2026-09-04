namespace RepoDashboard.Core.Git;

public interface IGitCommandRunner
{
    Task<GitCommandResult> ExecuteAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}
