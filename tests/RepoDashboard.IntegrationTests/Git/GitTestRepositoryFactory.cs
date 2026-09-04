using RepoDashboard.Core.Git;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

/// <summary>
/// Creates throwaway Git repositories under a temporary root directory:
/// one bare <c>remote.git</c> plus any number of clones. Clones get a local
/// test identity, so tests never depend on the developer's global Git config.
/// Owns the root directory and deletes it on dispose.
/// </summary>
public sealed class GitTestRepositoryFactory : IDisposable
{
    private const string DefaultBranch = "main";

    private readonly IGitCommandRunner _runner = new GitCommandRunner();
    private readonly string _root;

    public GitTestRepositoryFactory()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "RepoDashboard.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);
    }

    public string RootPath => _root;

    public void Dispose()
    {
        TestDirectories.DeleteRecursively(_root);
    }

    public async Task<string> CreateBareRepositoryAsync(
        string name = "remote.git",
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_root, name);

        await RunAsync(
            _root,
            ["init", "--bare", "-b", DefaultBranch, path],
            cancellationToken);

        return path;
    }

    public async Task<string> CloneAsync(
        string sourcePath,
        string name,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_root, name);

        await RunAsync(
            _root,
            ["clone", sourcePath, path],
            cancellationToken);

        await RunAsync(
            path,
            ["config", "user.email", "test@example.com"],
            cancellationToken);

        await RunAsync(
            path,
            ["config", "user.name", "RepoDashboardTests"],
            cancellationToken);

        await RunAsync(
            path,
            ["config", "commit.gpgsign", "false"],
            cancellationToken);

        return path;
    }

    public async Task<string> CommitFileAsync(
        string repositoryPath,
        string relativeFilePath,
        string content,
        string message,
        CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(repositoryPath, relativeFilePath);

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, content, cancellationToken);

        await RunAsync(
            repositoryPath,
            ["add", "--", relativeFilePath],
            cancellationToken);

        await RunAsync(
            repositoryPath,
            ["commit", "-m", message],
            cancellationToken);

        var head = await _runner.ExecuteAsync(
            repositoryPath,
            ["rev-parse", "HEAD"],
            cancellationToken);

        if (!head.Success)
        {
            throw new InvalidOperationException(
                $"git rev-parse HEAD in '{repositoryPath}' failed with exit code {head.ExitCode}: {head.StandardError.Trim()}");
        }

        return head.StandardOutput.Trim();
    }

    public async Task PushAsync(
        string repositoryPath,
        string branch = DefaultBranch,
        string remote = "origin",
        CancellationToken cancellationToken = default)
    {
        await RunAsync(
            repositoryPath,
            ["push", "-u", remote, branch],
            cancellationToken);
    }

    public async Task FetchAsync(
        string repositoryPath,
        string remote = "origin",
        CancellationToken cancellationToken = default)
    {
        await RunAsync(
            repositoryPath,
            ["fetch", remote],
            cancellationToken);
    }

    public async Task CheckoutAsync(
        string repositoryPath,
        string branch,
        CancellationToken cancellationToken = default)
    {
        await RunAsync(
            repositoryPath,
            ["checkout", branch],
            cancellationToken);
    }

    public async Task CreateBranchAsync(
        string repositoryPath,
        string branch,
        string? startPoint = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = startPoint is null
            ? ["branch", branch]
            : (IReadOnlyList<string>)["branch", branch, startPoint];

        await RunAsync(repositoryPath, arguments, cancellationToken);
    }

    private async Task RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await _runner.ExecuteAsync(
            workingDirectory, arguments, cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} in '{workingDirectory}' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }
    }
}
