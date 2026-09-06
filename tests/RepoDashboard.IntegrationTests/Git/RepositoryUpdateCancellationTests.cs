using FluentAssertions;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Lifetime;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Repositories;
using RepoDashboard.Core.Sync;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

/// <summary>
/// Regression test (review): once git pull has succeeded, the working tree
/// is already mutated, so a Cancel arriving during the final local
/// re-inspection must not hide the completed update. The post-pull sync
/// ignores user cancellation but still honors application shutdown, and the
/// batch still contains Updated.
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

        public CancellationToken LastToken { get; private set; }

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
            // Receives the shutdown-only token after the fix, so a user
            // Cancel does not abort it but shutdown does; before the
            // shutdown/user split it received CancellationToken.None and
            // ignored both.
            LastToken = cancellationToken;
            await _gate.Task.WaitAsync(cancellationToken);
            return Snapshot(repository);
        }
    }

    private sealed class FailFirstFinalUpdater : IRepositoryUpdater
    {
        private readonly CancellationTokenSource _userCts;
        private readonly RepositoryConfiguration _config;

        public FailFirstFinalUpdater(
            RepositoryConfiguration config,
            CancellationTokenSource userCts)
        {
            _config = config;
            _userCts = userCts;
        }

        public Task<RepositoryUpdateResult> UpdateAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken)
        {
            // Simulate Cancel pressed after git pull committed but before
            // the dashboard fallback inspection runs.
            _userCts.Cancel();

            return Task.FromResult(new RepositoryUpdateResult
            {
                RepositoryId = _config.Id,
                Outcome = RepositoryUpdateOutcome.Updated,
                Message = "Fast-forwarded 'main' by 1 commit(s) from 'origin/main'.",
                Decision = new UpdateDecision(
                    UpdateEligibility.CanFastForward,
                    "Current branch can fast-forward by 1 commit(s)."),
                FetchResult = new RepositoryOperationResult
                {
                    Success = true,
                    Operation = RepositoryOperationType.Fetch,
                    Message = "Fetched 'origin' (pruned).",
                    Duration = TimeSpan.Zero
                },
                FinalSnapshot = null
            });
        }
    }

    private sealed class CancellingInspector : IRepositoryInspector
    {
        public CancellationToken LastToken { get; private set; }

        public Task<RepositorySnapshot> InspectAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot(repository));
        }
    }

    private sealed class FastForwardInspector : IRepositoryInspector
    {
        public Task<RepositorySnapshot> InspectAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Pre-pull: fast-forwardable. Post-pull final: up to date.
            // Distinguished by caller stage is unnecessary here because the
            // pull-boundary tests only assert the pull token behaviour and
            // the final Updated outcome.
            return Task.FromResult(Snapshot(repository) with
            {
                UpstreamDivergence = new Divergence(0, 1)
            });
        }
    }

    private sealed class BlockingPullRunner : IGitCommandRunner
    {
        private readonly TaskCompletionSource _gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        private int _pullStarted;

        public bool PullStarted => _pullStarted != 0;

        public CancellationToken PullToken { get; private set; }

        public void Release() => _gate.TrySetResult();

        public async Task<GitCommandResult> ExecuteAsync(
            string repositoryPath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            if (arguments.Count > 0 && arguments[0] == "pull")
            {
                Interlocked.Exchange(ref _pullStarted, 1);
                PullToken = cancellationToken;
                await _gate.Task.WaitAsync(cancellationToken);

                return new GitCommandResult
                {
                    ExitCode = 0,
                    StandardOutput = string.Empty,
                    StandardError = string.Empty,
                    Duration = TimeSpan.Zero
                };
            }

            return new GitCommandResult
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
                Duration = TimeSpan.Zero
            };
        }
    }

    private sealed class FinalSnapshotInspector : IRepositoryInspector
    {
        private int _calls;

        public async Task<RepositorySnapshot> InspectAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);

            if (call == 1)
            {
                return Snapshot(repository) with
                {
                    UpstreamDivergence = new Divergence(0, 1)
                };
            }

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

    [Fact]
    public async Task UpdateAsync_UserCancelAfterPull_WithShutdownWired_StillReportsUpdated()
    {
        var config = Config("Store");
        var runner = new PullSuccessRunner();
        var inspector = new BlockingFinalInspector();
        using var shutdown = new ApplicationShutdown();
        var sut = new RepositoryUpdater(
            runner,
            new RepositoryFetcher(runner),
            inspector,
            new UpdateEligibilityClassifier(),
            applicationShutdown: shutdown);

        using var userCts = new CancellationTokenSource();
        var updateTask = sut.UpdateAsync(config, userCts.Token);

        await WaitForAsync(() => inspector.Calls >= 2, "final inspection to start");
        await userCts.CancelAsync();
        inspector.Release();

        var result = await updateTask;

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Updated);
        inspector.LastToken.Should().Be(shutdown.ShutdownToken);
    }

    [Fact]
    public async Task UpdateAsync_ShutdownDuringPostPullInspection_CancelsGitWork()
    {
        var config = Config("Store");
        var runner = new PullSuccessRunner();
        var inspector = new BlockingFinalInspector();
        using var shutdown = new ApplicationShutdown();
        var sut = new RepositoryUpdater(
            runner,
            new RepositoryFetcher(runner),
            inspector,
            new UpdateEligibilityClassifier(),
            applicationShutdown: shutdown);

        using var userCts = new CancellationTokenSource();
        var updateTask = sut.UpdateAsync(config, userCts.Token);

        await WaitForAsync(() => inspector.Calls >= 2, "final inspection to start");

        // Shutdown (not user Cancel) must still terminate the post-pull
        // Git work: the final inspection observes only the shutdown token.
        shutdown.NotifyShuttingDown();

        var act = () => updateTask;

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task UpdateAsync_UserCancelDuringPull_DoesNotAbortPull_StillReportsUpdated()
    {
        var config = Config("Store");
        var pullRunner = new BlockingPullRunner();
        using var shutdown = new ApplicationShutdown();
        var sut = new RepositoryUpdater(
            pullRunner,
            new RepositoryFetcher(new PullSuccessRunner()),
            new FinalSnapshotInspector(),
            new UpdateEligibilityClassifier(),
            applicationShutdown: shutdown);

        using var userCts = new CancellationTokenSource();
        var updateTask = sut.UpdateAsync(config, userCts.Token);

        await WaitForAsync(() => pullRunner.PullStarted, "git pull to start");
        await userCts.CancelAsync();
        await Task.Delay(100);

        // User Cancel must not reach the mutating pull: the pull observes
        // only shutdown, so it stays blocked and the update stays in flight.
        updateTask.IsCompleted.Should().BeFalse();
        pullRunner.PullToken.CanBeCanceled.Should().BeTrue();
        pullRunner.PullToken.IsCancellationRequested.Should().BeFalse();

        pullRunner.Release();
        var result = await updateTask;

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Updated);
    }

    [Fact]
    public async Task UpdateAsync_ShutdownDuringPull_CancelsPull()
    {
        var config = Config("Store");
        var pullRunner = new BlockingPullRunner();
        using var shutdown = new ApplicationShutdown();
        var sut = new RepositoryUpdater(
            pullRunner,
            new RepositoryFetcher(new PullSuccessRunner()),
            new FinalSnapshotInspector(),
            new UpdateEligibilityClassifier(),
            applicationShutdown: shutdown);

        using var userCts = new CancellationTokenSource();
        var updateTask = sut.UpdateAsync(config, userCts.Token);

        await WaitForAsync(() => pullRunner.PullStarted, "git pull to start");
        shutdown.NotifyShuttingDown();

        var act = () => updateTask;

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task UpdateAllAsync_FirstFinalInspectionFails_UserCancelAlreadySet_StillReportsUpdated()
    {
        var config = Config("Store");
        using var userCts = new CancellationTokenSource();
        using var shutdown = new ApplicationShutdown();
        var inspector = new CancellingInspector();
        var updater = new FailFirstFinalUpdater(config, userCts);
        var sut = new RepositoryDashboardService(
            new InMemoryStore([config]),
            inspector,
            new UpdateEligibilityClassifier(),
            new RepositoryFetcher(new PullSuccessRunner()),
            updater,
            applicationShutdown: shutdown);

        var batch = await sut.UpdateAllAsync(userCts.Token);

        // User Cancel was pressed after the pull committed (the updater
        // cancelled the user CTS) but the fallback uses the shutdown-only
        // token, so the committed Updated outcome survives in the batch.
        batch.CompletedItems.Should().ContainSingle();
        batch.CompletedItems[0].UpdateResult.Should().NotBeNull();
        batch.CompletedItems[0].UpdateResult!.Outcome
            .Should().Be(RepositoryUpdateOutcome.Updated);
        inspector.LastToken.Should().Be(shutdown.ShutdownToken);
    }
}
