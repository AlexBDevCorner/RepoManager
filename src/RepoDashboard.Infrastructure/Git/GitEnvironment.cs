using System.ComponentModel;
using RepoDashboard.Core.Git;

namespace RepoDashboard.Infrastructure.Git;

public sealed class GitEnvironment : IGitEnvironment
{
    private readonly IGitCommandRunner _runner;

    public GitEnvironment(IGitCommandRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<GitEnvironmentInfo> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        GitCommandResult result;

        try
        {
            // git --version needs no repository; any existing directory works.
            result = await _runner.ExecuteAsync(
                AppContext.BaseDirectory,
                ["--version"],
                cancellationToken);
        }
        catch (Win32Exception)
        {
            return new GitEnvironmentInfo(
                false,
                null,
                "Git could not be found.\n\nInstall Git for Windows and ensure git.exe\nis available through PATH.");
        }

        if (!result.Success)
        {
            return new GitEnvironmentInfo(
                false,
                null,
                $"git --version failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        var version = ParseVersion(result.StandardOutput);

        return version is null
            ? new GitEnvironmentInfo(
                false,
                null,
                $"Unexpected output from git --version: {result.StandardOutput.Trim()}")
            : new GitEnvironmentInfo(true, version, null);
    }

    private static string? ParseVersion(string output)
    {
        const string prefix = "git version ";

        var trimmed = output.Trim();

        return trimmed.StartsWith(prefix, StringComparison.Ordinal)
            ? trimmed[prefix.Length..].Trim()
            : null;
    }
}
