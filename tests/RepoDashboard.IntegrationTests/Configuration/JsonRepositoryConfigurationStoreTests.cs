using FluentAssertions;
using RepoDashboard.Core.Models;
using RepoDashboard.Infrastructure.Configuration;

namespace RepoDashboard.IntegrationTests.Configuration;

public sealed class JsonRepositoryConfigurationStoreTests
{
    [Fact]
    public async Task SaveThenLoad_RoundTripsRepositories()
    {
        // Arrange: a fresh store path simulates a first run with no file yet.
        var filePath = TemporaryFilePath();
        var store = new JsonRepositoryConfigurationStore(filePath);

        var repositories = new[]
        {
            new RepositoryConfiguration
            {
                Id = Guid.NewGuid(),
                Name = "Store",
                Path = """C:\Source\Repos\Store"""
            },
            new RepositoryConfiguration
            {
                Id = Guid.NewGuid(),
                Name = "Legacy",
                Path = """C:\Source\Repos\Legacy""",
                PreferredRemote = "upstream",
                DefaultBranchOverride = "develop",
                Enabled = false
            }
        };

        // Act: save, then load through a new instance (simulates app restart).
        await store.SaveAsync(repositories, CancellationToken.None);

        var reloaded = await new JsonRepositoryConfigurationStore(filePath)
            .LoadAsync(CancellationToken.None);

        // Assert
        reloaded.Should().BeEquivalentTo(repositories);
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsEmpty()
    {
        // Arrange
        var store = new JsonRepositoryConfigurationStore(TemporaryFilePath());

        // Act
        var repositories = await store.LoadAsync(CancellationToken.None);

        // Assert
        repositories.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_DuplicatePathsCaseInsensitive_Throws()
    {
        // Arrange: same folder, different casing plus a trailing separator.
        var store = new JsonRepositoryConfigurationStore(TemporaryFilePath());

        var repositories = new[]
        {
            new RepositoryConfiguration
            {
                Id = Guid.NewGuid(),
                Name = "First",
                Path = """C:\Source\Repos\Store"""
            },
            new RepositoryConfiguration
            {
                Id = Guid.NewGuid(),
                Name = "Second",
                Path = """c:\source\repos\store\"""
            }
        };

        // Act
        var act = () => store.SaveAsync(
            repositories, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*uplicate*");
    }

    [Fact]
    public async Task Save_DuplicatePaths_DoesNotWriteFile()
    {
        // Arrange
        var filePath = TemporaryFilePath();
        var store = new JsonRepositoryConfigurationStore(filePath);

        var repositories = new[]
        {
            new RepositoryConfiguration
            {
                Id = Guid.NewGuid(),
                Name = "First",
                Path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
            },
            new RepositoryConfiguration
            {
                Id = Guid.NewGuid(),
                Name = "Second",
                Path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
            }
        };

        repositories[1] = repositories[1] with
        {
            Path = repositories[0].Path.ToUpperInvariant() + Path.DirectorySeparatorChar
        };

        // Act
        await FluentActions.Awaiting(() => store.SaveAsync(
                repositories, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        // Assert: rejected before any write — no file, no temp leftovers.
        File.Exists(filePath).Should().BeFalse();
        File.Exists(filePath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public async Task Save_ReplacesAtomicallyWithoutTempLeftovers()
    {
        // Arrange
        var filePath = TemporaryFilePath();
        var store = new JsonRepositoryConfigurationStore(filePath);

        var repositories = new[]
        {
            new RepositoryConfiguration
            {
                Id = Guid.NewGuid(),
                Name = "Store",
                Path = """C:\Source\Repos\Store"""
            }
        };

        // Act
        await store.SaveAsync(repositories, CancellationToken.None);

        // Assert: no .tmp survives, and the file is complete, parseable JSON
        // (a crash mid-save could never leave half-written content behind
        // because readers only ever see the moved-into-place file).
        File.Exists(filePath + ".tmp").Should().BeFalse();

        var reloaded = await new JsonRepositoryConfigurationStore(filePath)
            .LoadAsync(CancellationToken.None);

        reloaded.Should().HaveCount(1);
    }

    private static string TemporaryFilePath() =>
        Path.Combine(
            Path.GetTempPath(),
            "RepoDashboard.Tests",
            Guid.NewGuid().ToString("N"),
            "repositories.json");
}
