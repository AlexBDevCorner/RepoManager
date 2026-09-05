using FluentAssertions;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

public sealed class RepositoryFetcherUnitTests
{
    private static RepositoryConfiguration Config(
        string? preferredRemote = "origin") =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Store",
            Path = """C:\Source\Repos\Store""",
            PreferredRemote = preferredRemote!
        };

    private sealed class StubRunner : IGitCommandRunner
    {
        private readonly Func<string, IReadOnlyList<string>, GitCommandResult> _execute;

        public string? SeenPath { get; private set; }

        public IReadOnlyList<string>? SeenArguments { get; private set; }

        public StubRunner(
            Func<string, IReadOnlyList<string>, GitCommandResult>? execute = null)
        {
            _execute = execute ?? ((_, _) => new GitCommandResult
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
                Duration = TimeSpan.FromMilliseconds(12)
            });
        }

        public Task<GitCommandResult> ExecuteAsync(
            string repositoryPath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            SeenPath = repositoryPath;
            SeenArguments = arguments.ToList();
            return Task.FromResult(_execute(repositoryPath, arguments));
        }
    }

    private sealed class ThrowingRunner : IGitCommandRunner
    {
        public Task<GitCommandResult> ExecuteAsync(
            string repositoryPath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("git could not start");
    }

    [Fact]
    public async Task FetchAsync_ExecutesFetchWithPrune()
    {
        var runner = new StubRunner();
        IRepositoryFetcher sut = new RepositoryFetcher(runner);

        var result = await sut.FetchAsync(Config(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Operation.Should().Be(RepositoryOperationType.Fetch);
        runner.SeenPath.Should().Be("""C:\Source\Repos\Store""");
        runner.SeenArguments.Should().Equal("fetch", "origin", "--prune");
        result.ExitCode.Should().Be(0);
        result.Duration.Should().Be(TimeSpan.FromMilliseconds(12));
        result.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FetchAsync_UsesPreferredRemote()
    {
        var runner = new StubRunner();
        IRepositoryFetcher sut = new RepositoryFetcher(runner);

        await sut.FetchAsync(Config("upstream"), CancellationToken.None);

        runner.SeenArguments.Should().Equal("fetch", "upstream", "--prune");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FetchAsync_BlankRemote_FallsBackToOrigin(string? preferredRemote)
    {
        var runner = new StubRunner();
        IRepositoryFetcher sut = new RepositoryFetcher(runner);

        await sut.FetchAsync(Config(preferredRemote), CancellationToken.None);

        runner.SeenArguments.Should().Equal("fetch", "origin", "--prune");
    }

    [Fact]
    public async Task FetchAsync_GitFailure_ReturnsFailureWithOutputAndDuration()
    {
        var runner = new StubRunner((_, _) => new GitCommandResult
        {
            ExitCode = 128,
            StandardOutput = string.Empty,
            StandardError = "fatal: 'nope' does not appear to be a git repository",
            Duration = TimeSpan.FromMilliseconds(7)
        });
        IRepositoryFetcher sut = new RepositoryFetcher(runner);

        var result = await sut.FetchAsync(Config("nope"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Operation.Should().Be(RepositoryOperationType.Fetch);
        result.ExitCode.Should().Be(128);
        result.Duration.Should().Be(TimeSpan.FromMilliseconds(7));
        result.Message.Should().Contain("128");
        result.Message.Should().Contain("does not appear to be a git repository");
        result.RawOutput.Should().Contain("does not appear to be a git repository");
    }

    [Fact]
    public async Task FetchAsync_RunnerThrows_ReturnsFailureInsteadOfThrowing()
    {
        IRepositoryFetcher sut = new RepositoryFetcher(new ThrowingRunner());

        var result = await sut.FetchAsync(Config(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Operation.Should().Be(RepositoryOperationType.Fetch);
        result.Message.Should().Contain("git could not start");
    }

    [Fact]
    public async Task FetchAsync_NullRepository_Throws()
    {
        IRepositoryFetcher sut = new RepositoryFetcher(new StubRunner());

        var act = () => sut.FetchAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
