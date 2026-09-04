using System.ComponentModel;
using FluentAssertions;
using RepoDashboard.Core.Git;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

public sealed class GitEnvironmentTests
{
    [Fact]
    public async Task CheckAsync_GitInstalled_ReturnsAvailableWithVersion()
    {
        // Arrange
        IGitEnvironment environment = new GitEnvironment(new GitCommandRunner());

        // Act
        var info = await environment.CheckAsync();

        // Assert
        info.Available.Should().BeTrue();
        info.Version.Should().NotBeNullOrWhiteSpace();
        info.Error.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_GitMissing_ReturnsUnavailableWithClearError()
    {
        // Arrange
        IGitEnvironment environment = new GitEnvironment(new MissingGitRunner());

        // Act
        var info = await environment.CheckAsync();

        // Assert
        info.Available.Should().BeFalse();
        info.Version.Should().BeNull();
        info.Error.Should().Contain("could not be found").And.Contain("PATH");
    }

    private sealed class MissingGitRunner : IGitCommandRunner
    {
        public Task<GitCommandResult> ExecuteAsync(
            string repositoryPath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            throw new Win32Exception("Simulated git.exe missing from PATH.");
        }
    }
}
