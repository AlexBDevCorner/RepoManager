using FluentAssertions;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

public sealed class RepositoryDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "RepoDashboard.Discovery",
        Guid.NewGuid().ToString("N"));

    public RepositoryDiscoveryTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestDirectories.DeleteRecursively(_root);
    }

    private string MakeDir(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void MakeRepo(string path) =>
        Directory.CreateDirectory(Path.Combine(path, ".git"));

    [Fact]
    public async Task DiscoverAsync_FindsDirectChildren_AsRepositories()
    {
        var store = MakeDir("Store");
        var viewer = MakeDir("Viewer");
        MakeRepo(store);
        MakeRepo(viewer);
        MakeDir("NotARepo");

        var sut = new RepositoryDiscoveryService();

        var found = await sut.DiscoverAsync(_root, 3, CancellationToken.None);

        found.Select(r => r.Name).Should().BeEquivalentTo("Store", "Viewer");
    }

    [Fact]
    public async Task DiscoverAsync_StopsDescending_OnceRepoFound()
    {
        var outer = MakeDir("Store");
        MakeRepo(outer);
        var inner = Path.Combine(outer, "nested", "inner");
        Directory.CreateDirectory(inner);
        MakeRepo(inner);

        var sut = new RepositoryDiscoveryService();

        var found = await sut.DiscoverAsync(_root, 3, CancellationToken.None);

        found.Should().ContainSingle().Which.Path.Should().Be(outer);
    }

    [Fact]
    public async Task DiscoverAsync_Respects_MaxDepth()
    {
        // root/a (1) /b (2) /c (3) /deep (4): depth-4 repo is invisible
        // at maxDepth 3 but visible at maxDepth 4.
        var deep = MakeDir("a", "b", "c", "deep");
        MakeRepo(deep);

        var sut = new RepositoryDiscoveryService();

        var shallow = await sut.DiscoverAsync(_root, 3, CancellationToken.None);
        shallow.Should().BeEmpty();

        var full = await sut.DiscoverAsync(_root, 4, CancellationToken.None);
        full.Should().ContainSingle().Which.Path.Should().Be(deep);
    }

    [Fact]
    public async Task DiscoverAsync_Skips_Hidden_And_Dot_Folders()
    {
        var visible = MakeDir("Visible");
        MakeRepo(visible);
        var hidden = MakeDir("Hidden");
        MakeRepo(hidden);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        var dot = MakeDir(".vs");
        MakeRepo(dot);

        var sut = new RepositoryDiscoveryService();

        var found = await sut.DiscoverAsync(_root, 3, CancellationToken.None);

        found.Select(r => r.Path).Should().Contain(visible);
        found.Select(r => r.Path).Should().NotContain(hidden);
        found.Select(r => r.Path).Should().NotContain(dot);
    }

    [Fact]
    public async Task DiscoverAsync_MissingRoot_ThrowsDirectoryNotFound()
    {
        var sut = new RepositoryDiscoveryService();

        var act = () => sut.DiscoverAsync(
            Path.Combine(_root, "does-not-exist"), 3, CancellationToken.None);

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task DiscoverAsync_AlreadyCancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = new RepositoryDiscoveryService();

        var act = () => sut.DiscoverAsync(_root, 3, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
