using FluentAssertions;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.Core.Tests.Sync;

public sealed class UpdateEligibilityClassifierTests
{
    private readonly UpdateEligibilityClassifier _sut = new();

    private static readonly RepositoryConfiguration Configuration = new()
    {
        Id = Guid.NewGuid(),
        Name = "Store",
        Path = """C:\Source\Repos\Store""",
        PreferredRemote = "origin"
    };

    [Fact]
    public void Missing_directory_cannot_be_updated()
    {
        var snapshot = CreateSnapshot(
            directoryExists: false);

        var result = _sut.Classify(Configuration, snapshot);

        result.Eligibility.Should().Be(UpdateEligibility.RepositoryMissing);
        result.CanUpdate.Should().BeFalse();
        result.Explanation.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Non_git_directory_cannot_be_updated()
    {
        var snapshot = CreateSnapshot(
            isGitRepository: false);

        var result = _sut.Classify(Configuration, snapshot);

        result.Eligibility.Should().Be(UpdateEligibility.InvalidRepository);
        result.CanUpdate.Should().BeFalse();
        result.Explanation.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Detached_head_cannot_be_updated()
    {
        var snapshot = CreateSnapshot(
            isDetachedHead: true,
            isDirty: true,
            upstream: "origin/main",
            ahead: 0,
            behind: 5);

        var result = _sut.Classify(Configuration, snapshot);

        result.Eligibility.Should().Be(UpdateEligibility.DetachedHead);
        result.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void Merge_in_progress_cannot_be_updated()
    {
        var snapshot = CreateSnapshot(
            mergeInProgress: true,
            isDirty: true,
            upstream: "origin/main",
            ahead: 0,
            behind: 5);

        var result = _sut.Classify(Configuration, snapshot);

        result.Eligibility.Should().Be(UpdateEligibility.OperationInProgress);
        result.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void Rebase_in_progress_cannot_be_updated()
    {
        var snapshot = CreateSnapshot(
            rebaseInProgress: true,
            isDirty: true,
            upstream: "origin/main",
            ahead: 0,
            behind: 5);

        var result = _sut.Classify(Configuration, snapshot);

        result.Eligibility.Should().Be(UpdateEligibility.OperationInProgress);
        result.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void Cherry_pick_in_progress_cannot_be_updated()
    {
        var snapshot = CreateSnapshot(
            cherryPickInProgress: true,
            isDirty: true,
            upstream: "origin/main",
            ahead: 0,
            behind: 5);

        var result = _sut.Classify(Configuration, snapshot);

        result.Eligibility.Should().Be(UpdateEligibility.OperationInProgress);
        result.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void Dirty_repository_cannot_be_updated()
    {
        var snapshot = CreateSnapshot(
            dirty: true,
            upstream: "origin/main",
            ahead: 0,
            behind: 5);

        var result = _sut.Classify(Configuration, snapshot);

        result.Eligibility
            .Should()
            .Be(UpdateEligibility.Dirty);
        result.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void Branch_without_upstream_cannot_be_updated()
    {
        var snapshot = CreateSnapshot(
            upstream: null,
            ahead: 0,
            behind: 0);

        var result = _sut.Classify(Configuration, snapshot);

        result.Eligibility.Should().Be(UpdateEligibility.NoUpstream);
        result.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void Upstream_on_different_remote_cannot_be_updated()
    {
        var snapshot = CreateSnapshot(
            upstream: "upstream/main",
            upstreamRemote: "upstream",
            ahead: 0,
            behind: 3);

        var result = _sut.Classify(Configuration, snapshot);

        result.Eligibility.Should().Be(UpdateEligibility.UpstreamUsesDifferentRemote);
        result.CanUpdate.Should().BeFalse();
        result.Explanation.Should().Contain("upstream");
    }

    [Fact]
    public void Up_to_date_repository_needs_no_update()
    {
        var snapshot = CreateSnapshot(
            upstream: "origin/main",
            ahead: 0,
            behind: 0);

        var result = _sut.Classify(Configuration, snapshot);

        result.Eligibility.Should().Be(UpdateEligibility.AlreadyUpToDate);
        result.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void Ahead_repository_cannot_be_updated()
    {
        var snapshot = CreateSnapshot(
            upstream: "origin/main",
            ahead: 2,
            behind: 0);

        var result = _sut.Classify(Configuration, snapshot);

        result.Eligibility.Should().Be(UpdateEligibility.Ahead);
        result.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void Behind_repository_can_fast_forward()
    {
        var snapshot = CreateSnapshot(
            upstream: "origin/main",
            ahead: 0,
            behind: 5);

        var result = _sut.Classify(Configuration, snapshot);

        result.Eligibility.Should().Be(UpdateEligibility.CanFastForward);
        result.CanUpdate.Should().BeTrue();
    }

    [Fact]
    public void Diverged_repository_cannot_be_updated()
    {
        var snapshot = CreateSnapshot(
            upstream: "origin/main",
            ahead: 2,
            behind: 4);

        var result = _sut.Classify(Configuration, snapshot);

        result.Eligibility.Should().Be(UpdateEligibility.Diverged);
        result.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void Unknown_divergence_cannot_be_updated()
    {
        var snapshot = CreateSnapshot(
            upstream: "origin/main",
            unknownDivergence: true);

        var result = _sut.Classify(Configuration, snapshot);

        result.Eligibility.Should().Be(UpdateEligibility.Unknown);
        result.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void Dirty_explanation_states_cause_and_consequence()
    {
        var snapshot = CreateSnapshot(
            dirty: true,
            upstream: "origin/main",
            ahead: 0,
            behind: 5);

        var result = _sut.Classify(Configuration, snapshot);

        result.Explanation.Should().Contain("uncommitted changes");
        result.Explanation.Should().Contain("skipped");
    }

    [Fact]
    public void Ahead_explanation_states_nothing_to_pull()
    {
        var snapshot = CreateSnapshot(
            upstream: "origin/main",
            ahead: 2,
            behind: 0);

        var result = _sut.Classify(Configuration, snapshot);

        result.Explanation.Should().Contain("2");
        result.Explanation.Should().Contain("origin/main");
        result.Explanation.Should().Contain("nothing to pull");
    }

    [Fact]
    public void Diverged_explanation_requires_manual_resolution()
    {
        var snapshot = CreateSnapshot(
            upstream: "origin/main",
            ahead: 3,
            behind: 5);

        var result = _sut.Classify(Configuration, snapshot);

        result.Explanation.Should().Contain("+3");
        result.Explanation.Should().Contain("+5");
        result.Explanation.Should().Contain("Manual merge or rebase");
    }

    [Fact]
    public void NoUpstream_explanation_names_branch()
    {
        var snapshot = CreateSnapshot(
            upstream: null,
            ahead: 0,
            behind: 0);

        var result = _sut.Classify(Configuration, snapshot);

        result.Explanation.Should().Contain("main");
        result.Explanation.Should().Contain("does not track a remote branch");
    }

    private static RepositorySnapshot CreateSnapshot(
        bool directoryExists = true,
        bool isGitRepository = true,
        bool isDetachedHead = false,
        bool dirty = false,
        bool isDirty = false,
        string? upstream = "origin/main",
        string? upstreamRemote = "origin",
        int ahead = 0,
        int behind = 0,
        bool unknownDivergence = false,
        bool mergeInProgress = false,
        bool rebaseInProgress = false,
        bool cherryPickInProgress = false)
    {
        // 'dirty' matches the ticket example (CreateSnapshot(dirty: true));
        // 'isDirty' is an alias so both spellings work.
        var effectiveDirty = dirty || isDirty;

        Divergence? effectiveDivergence =
            upstream is null || unknownDivergence
                ? null
                : new Divergence(ahead, behind);

        return new RepositorySnapshot
        {
            RepositoryId = Configuration.Id,
            Path = Configuration.Path,
            DirectoryExists = directoryExists,
            IsGitRepository = isGitRepository,
            CurrentBranch = isDetachedHead ? null : "main",
            IsDetachedHead = isDetachedHead,
            IsDirty = effectiveDirty,
            UpstreamRef = upstream,
            UpstreamRemote = upstream is null ? null : upstreamRemote,
            UpstreamBranch = upstream is null ? null : "main",
            UpstreamDivergence = effectiveDivergence,
            MergeInProgress = mergeInProgress,
            RebaseInProgress = rebaseInProgress,
            CherryPickInProgress = cherryPickInProgress,
            InspectedAt = DateTimeOffset.UtcNow
        };
    }
}
