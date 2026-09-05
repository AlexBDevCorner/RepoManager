using FluentAssertions;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

/// <summary>
/// Verifies the safety-critical algorithm shape with scripted Git output:
/// exact step order, exact pull flags, and no fallback mutation.
/// Real-Git behaviour is covered by <c>RepositoryUpdaterTests</c>.
/// </summary>
public sealed class RepositoryUpdaterUnitTests
{
    private static RepositoryConfiguration Config() =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Store",
            Path = """C:\Source\Repos\Store""",
            PreferredRemote = "origin"
        };

    private static RepositorySnapshot Snapshot(
        int ahead = 0,
        int behind = 1,
        bool dirty = false,
        bool detached = false) =>
        new()
        {
            RepositoryId = Guid.NewGuid(),
            Path = """C:\Source\Repos\Store""",
            DirectoryExists = true,
            IsGitRepository = true,
            CurrentBranch = detached ? null : "main",
            IsDetachedHead = detached,
            IsDirty = dirty,
            UpstreamRef = "origin/main",
            UpstreamRemote = "origin",
            UpstreamBranch = "main",
            DefaultRemoteBranch = "main",
            UpstreamDivergence = new Divergence(ahead, behind),
            DefaultBranchDivergence = new Divergence(ahead, behind),
            InspectedAt = DateTimeOffset.UtcNow
        };

    private sealed class Script
    {
        public readonly List<string> Events = [];
        public readonly List<IReadOnlyList<string>> RunnerCalls = [];
        public Func<IReadOnlyList<string>, GitCommandResult>? OnRun;
        public RepositoryOperationResult? FetchResult;
        public RepositorySnapshot SnapshotToReturn = Snapshot();
    }

    private sealed class RecordingRunner : IGitCommandRunner
    {
        private readonly Script _script;

        public RecordingRunner(Script script)
        {
            _script = script;
        }

        public Task<GitCommandResult> ExecuteAsync(
            string repositoryPath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            _script.RunnerCalls.Add(arguments.ToList());

            if (arguments is ["pull", ..])
            {
                _script.Events.Add("pull");
            }

            var result = _script.OnRun?.Invoke(arguments) ?? new GitCommandResult
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
                Duration = TimeSpan.Zero
            };

            return Task.FromResult(result);
        }
    }

    private sealed class RecordingFetcher : IRepositoryFetcher
    {
        private readonly Script _script;

        public RecordingFetcher(Script script)
        {
            _script = script;
        }

        public Task<RepositoryOperationResult> FetchAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken)
        {
            _script.Events.Add("fetch");

            return Task.FromResult(_script.FetchResult ?? new RepositoryOperationResult
            {
                Success = true,
                Operation = RepositoryOperationType.Fetch,
                Message = "Fetched 'origin' (pruned).",
                Duration = TimeSpan.Zero
            });
        }
    }

    private sealed class RecordingInspector : IRepositoryInspector
    {
        private readonly Script _script;

        public RecordingInspector(Script script)
        {
            _script = script;
        }

        public Task<RepositorySnapshot> InspectAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken)
        {
            _script.Events.Add("inspect");
            return Task.FromResult(_script.SnapshotToReturn);
        }
    }

    private static bool StartsWith(IReadOnlyList<string> arguments, string command) =>
        arguments.Count > 0 && arguments[0] == command;

    private static bool IsForbiddenMutation(IReadOnlyList<string> arguments) =>
        StartsWith(arguments, "merge")
            || StartsWith(arguments, "rebase")
            || StartsWith(arguments, "reset")
            || StartsWith(arguments, "stash")
            || StartsWith(arguments, "checkout");

    private static RepositoryUpdater CreateSut(Script script) =>
        new(
            new RecordingRunner(script),
            new RecordingFetcher(script),
            new RecordingInspector(script),
            new UpdateEligibilityClassifier());

    [Fact]
    public async Task UpdateAsync_FastForwardable_RunsExactOrderWithFfOnlyPull()
    {
        var script = new Script();
        var sut = CreateSut(script);

        var result = await sut.UpdateAsync(Config(), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Updated);
        result.Decision?.Eligibility.Should().Be(UpdateEligibility.CanFastForward);
        result.FetchResult?.Success.Should().BeTrue();
        result.FinalSnapshot.Should().NotBeNull();

        // Task 29 acceptance: exact order fetch → inspect → classify → pull → reinspect.
        script.Events.Should().Equal("fetch", "inspect", "pull", "inspect");

        var pull = script.RunnerCalls.Should().ContainSingle(
            c => StartsWith(c, "pull")).Subject;
        pull.Should().Equal("pull", "--ff-only", "--no-rebase");

        // The fetch runs through IRepositoryFetcher (recorded in Events);
        // the only Git command the updater issues itself is the pull.
        script.RunnerCalls.Should().HaveCount(1);

        result.Message.Should().Contain("Fast-forward");
    }

    [Fact]
    public async Task UpdateAsync_DirtyTree_SkipsWithoutPulling()
    {
        var script = new Script { SnapshotToReturn = Snapshot(dirty: true) };
        var sut = CreateSut(script);

        var result = await sut.UpdateAsync(Config(), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Skipped);
        result.Decision?.Eligibility.Should().Be(UpdateEligibility.Dirty);
        result.Message.Should().Contain("uncommitted changes");
        result.FinalSnapshot.Should().NotBeNull();
        script.RunnerCalls.Should().NotContain(c => StartsWith(c, "pull"));
        script.Events.Should().Equal("fetch", "inspect");
    }

    [Theory]
    [InlineData(1, 0, UpdateEligibility.Ahead)]
    [InlineData(2, 3, UpdateEligibility.Diverged)]
    public async Task UpdateAsync_UnsafeDivergence_SkipsWithoutPulling(
        int ahead, int behind, UpdateEligibility expected)
    {
        var script = new Script { SnapshotToReturn = Snapshot(ahead, behind) };
        var sut = CreateSut(script);

        var result = await sut.UpdateAsync(Config(), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Skipped);
        result.Decision?.Eligibility.Should().Be(expected);
        script.RunnerCalls.Should().NotContain(c => StartsWith(c, "pull"));
    }

    [Fact]
    public async Task UpdateAsync_AlreadyUpToDate_SkipsWithoutPulling()
    {
        var script = new Script { SnapshotToReturn = Snapshot(ahead: 0, behind: 0) };
        var sut = CreateSut(script);

        var result = await sut.UpdateAsync(Config(), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Skipped);
        result.Decision?.Eligibility.Should().Be(UpdateEligibility.AlreadyUpToDate);
        script.RunnerCalls.Should().NotContain(c => StartsWith(c, "pull"));
    }

    [Fact]
    public async Task UpdateAsync_RefusedPull_FailsSafelyWithNoFallback()
    {
        var script = new Script
        {
            OnRun = args => args is ["pull", ..]
                ? new GitCommandResult
                {
                    ExitCode = 128,
                    StandardOutput = string.Empty,
                    StandardError = "fatal: Not possible to fast-forward to 'abc'.",
                    Duration = TimeSpan.Zero
                }
                : new GitCommandResult
                {
                    ExitCode = 0,
                    StandardOutput = string.Empty,
                    StandardError = string.Empty,
                    Duration = TimeSpan.Zero
                }
        };
        var sut = CreateSut(script);

        var result = await sut.UpdateAsync(Config(), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Failed);
        result.Message.Should().Contain("safely");
        result.Message.Should().Contain("128");
        result.Decision?.Eligibility.Should().Be(UpdateEligibility.CanFastForward);

        // Exactly one pull attempt and no fallback mutation strategy.
        script.RunnerCalls.Count(c => StartsWith(c, "pull")).Should().Be(1);
        script.RunnerCalls.Should().NotContain(c => IsForbiddenMutation(c));

        // Still re-inspected after the refused pull.
        script.Events.Should().Equal("fetch", "inspect", "pull", "inspect");
    }

    [Fact]
    public async Task UpdateAsync_FailedFetch_FailsWithoutPulling()
    {
        var script = new Script
        {
            FetchResult = new RepositoryOperationResult
            {
                Success = false,
                Operation = RepositoryOperationType.Fetch,
                Message = "git fetch origin --prune failed with exit code 128: boom",
                ExitCode = 128,
                Duration = TimeSpan.Zero
            }
        };
        var sut = CreateSut(script);

        var result = await sut.UpdateAsync(Config(), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Failed);
        result.Message.Should().Contain("boom");
        result.Decision.Should().BeNull();
        result.FetchResult?.Success.Should().BeFalse();
        // Local state is still observed best-effort for the row.
        result.FinalSnapshot.Should().NotBeNull();
        script.RunnerCalls.Should().NotContain(c => StartsWith(c, "pull"));
    }

    [Fact]
    public async Task UpdateAsync_NullRepository_Throws()
    {
        var sut = CreateSut(new Script());

        var act = () => sut.UpdateAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
