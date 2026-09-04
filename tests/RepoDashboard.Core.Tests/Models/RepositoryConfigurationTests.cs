using FluentAssertions;
using RepoDashboard.Core.Models;

namespace RepoDashboard.Core.Tests.Models;

public sealed class RepositoryConfigurationTests
{
    [Fact]
    public void Defaults_MatchDomainConcepts()
    {
        // Arrange + Act
        var configuration = new RepositoryConfiguration
        {
            Id = Guid.NewGuid(),
            Name = "Store",
            Path = """C:\Source\Repos\Store"""
        };

        // Assert
        configuration.PreferredRemote.Should().Be("origin");
        configuration.DefaultBranchOverride.Should().BeNull();
        configuration.Enabled.Should().BeTrue();
    }
}
