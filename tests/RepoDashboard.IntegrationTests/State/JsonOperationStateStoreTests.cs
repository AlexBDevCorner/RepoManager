using FluentAssertions;
using RepoDashboard.Infrastructure.State;

namespace RepoDashboard.IntegrationTests.State;

public sealed class JsonOperationStateStoreTests
{
    [Fact]
    public async Task SaveThenLoad_RoundTripsTimestamps()
    {
        // Arrange: a fresh store path simulates a first run with no file yet.
        var filePath = TemporaryFilePath();
        var store = new JsonOperationStateStore(filePath);

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var state = new Dictionary<Guid, DateTimeOffset>
        {
            [firstId] = new DateTimeOffset(2026, 9, 4, 12, 44, 0, TimeSpan.FromHours(3)),
            [secondId] = DateTimeOffset.UtcNow
        };

        // Act: save, then load through a new instance (simulates app restart).
        await store.SaveAsync(state, CancellationToken.None);

        var reloaded = await new JsonOperationStateStore(filePath)
            .LoadAsync(CancellationToken.None);

        // Assert
        reloaded.Should().BeEquivalentTo(state);
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsEmpty()
    {
        // Arrange
        var store = new JsonOperationStateStore(TemporaryFilePath());

        // Act
        var state = await store.LoadAsync(CancellationToken.None);

        // Assert
        state.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsEmptyInsteadOfThrowing()
    {
        // Arrange: operational state must never break startup.
        var filePath = TemporaryFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "{ not valid json");

        // Act
        var state = await new JsonOperationStateStore(filePath)
            .LoadAsync(CancellationToken.None);

        // Assert
        state.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_ReplacesAtomicallyWithoutTempLeftovers()
    {
        // Arrange
        var filePath = TemporaryFilePath();
        var store = new JsonOperationStateStore(filePath);

        // Act
        await store.SaveAsync(
            new Dictionary<Guid, DateTimeOffset>
            {
                [Guid.NewGuid()] = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        // Assert
        File.Exists(filePath + ".tmp").Should().BeFalse();

        var reloaded = await new JsonOperationStateStore(filePath)
            .LoadAsync(CancellationToken.None);

        reloaded.Should().HaveCount(1);
    }

    private static string TemporaryFilePath() =>
        Path.Combine(
            Path.GetTempPath(),
            "RepoDashboard.Tests",
            Guid.NewGuid().ToString("N"),
            "state.json");
}
