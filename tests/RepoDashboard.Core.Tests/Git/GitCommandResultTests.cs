using FluentAssertions;
using RepoDashboard.Core.Git;

namespace RepoDashboard.Core.Tests.Git;

public sealed class GitCommandResultTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(128, false)]
    public void Success_ReflectsExitCode(int exitCode, bool expected)
    {
        // Arrange
        var result = new GitCommandResult
        {
            ExitCode = exitCode,
            StandardOutput = string.Empty,
            StandardError = string.Empty,
            Duration = TimeSpan.Zero
        };

        // Act
        var success = result.Success;

        // Assert
        success.Should().Be(expected);
    }
}
