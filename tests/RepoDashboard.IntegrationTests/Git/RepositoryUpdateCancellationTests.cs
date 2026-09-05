using FluentAssertions;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Repositories;
using RepoDashboard.Core.Sync;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

/// <summary>
/// Regression test (review): once git pull has succeeded, the working tree
/// is already mutated, so a Cancel arriving during the final local
/// re-inspection must not hide the completed update. The post-pull sync
/// ignores user cancellation and the batch still contains Updated.
/// </summary>
public sealed class RepositoryUpdateCancellationTests
{
    private static RepositoryConfiguration Config(string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = $"""C:\Source\Repos\{name}"""
        };

    private static RepositorySnapshot Snapshot(RepositoryConfiguration c) =>
        new()
        {
            RepositoryId = c.Id,
            Path = c.Path,
            DirectoryExists = true,
            IsGitRepository = true,
            CurrentBranch = "main",
            UpstreamRef = "origin/main",
            UpstreamRemote = "origin",
            UpstreamBranch = "main",
            UpstreamDivergence = new Divergence(0, 0),
            InspectedAt = DateTimeOffset.UtcNow
        };

    private sealed class InMemoryStore(
        IReadOnlyList<RepositoryConfiguration> seed) : IRepositoryConfigurationStore
    {
        public Task<IReadOnlyList<RepositoryConfiguration>> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(seed);

        public Task SaveAsync(
            IReadOnlyCollection<RepositoryConfiguration> repositories,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class PullSuccessRunner : IGitCommandRunner
    {
        public Task<GitCommandResult> ExecuteAsync(
            string repositoryPath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitCommandResult
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
                Duration = TimeSpan.Zero
            });
    }

    private sealed class BlockingFinalInspector : IRepositoryInspector
    {
        private int _calls;
        private readonly TaskCompletionSource _gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => _calls;

        public void Release() => _gate.TrySetResult();

        public async Task<RepositorySnapshot> InspectAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);

            if (call == 1)
            {
                // Pre-pull inspection: fast-forwardable (behind 1).
                return Snapshot(repository) with
                {
                    UpstreamDivergence = new Divergence(0, 1)
                };
            }

            // Post-pull final inspection: block until the test releases.
            // Receives CancellationToken.None after the fix, so a user
            // Cancel does not abort it; before the fix it observed the
            // caller's token and threw OperationCanceledException.
            await _gate.Task.WaitAsync(cancellationToken);
            return Snapshot(repository);
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task UpdateAllAsync_CancelAfterSuccessfulPull_StillReportsUpdated()
    {
        var config = Config("Store");
        var runner = new PullSuccessRunner();
        var inspector = new BlockingFinalInspector();
        var sut = new RepositoryDashboardService(
            new InMemoryStore([config]),
            inspector,
            new UpdateEligibilityClassifier(),
            new RepositoryFetcher(runner),
            new RepositoryUpdater(
                runner,
                new RepositoryFetcher(runner),
                inspector,
                new UpdateEligibilityClassifier()));

        using var cts = new CancellationTokenSource();
        var batchTask = sut.UpdateAllAsync(cts.Token);

        await WaitForAsync(() => inspector.Calls >= 2, "final inspection to start");
        await cts.CancelAsync();
        inspector.Release();

        var batch = await batchTask;

        batch.CompletedItems.Should().ContainSingle();
        batch.CompletedItems[0].UpdateResult.Should().NotBeNull();
        batch.CompletedItems[0].UpdateResult!.Outcome
            .Should().Be(RepositoryUpdateOutcome.Updated);
    }

    [Fact]
    public async Task UpdateAsync_CancelAfterSuccessfulPull_StillReportsUpdated()
    {
        var config = Config("Store");
        var runner = new PullSuccessRunner();
        var inspector = new BlockingFinalInspector();
        var sut = new RepositoryUpdater(
            runner,
            new RepositoryFetcher(runner),
            inspector,
            new UpdateEligibilityClassifier());

        using var cts = new CancellationTokenSource();
        var updateTask = sut.UpdateAsync(config, cts.Token);

        await WaitForAsync(() => inspector.Calls >= 2, "final inspection to start");
        await cts.CancelAsync();
        inspector.Release();

        // Must not throw OperationCanceledException: the mutation committed.
        var result = await updateTask;

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Updated);
    }
}
