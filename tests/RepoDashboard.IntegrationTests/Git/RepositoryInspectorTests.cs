using FluentAssertions;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Repositories;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

public sealed class RepositoryInspectorTests
{
    private readonly IGitCommandRunner _git = new GitCommandRunner();

    private RepositoryInspector CreateInspector() =>
        new(_git, new DivergenceCalculator(_git));

    private static RepositoryConfiguration ConfigFor(
        string path, string? defaultBranchOverride = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Path = path,
            DefaultBranchOverride = defaultBranchOverride
        };

    private async Task RunAsync(string workingDirectory, params string[] arguments)
    {
        var result = await _git.ExecuteAsync(
            workingDirectory, arguments, CancellationToken.None);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} in '{workingDirectory}' " +
                $"failed with exit code {result.ExitCode}: {result.StandardOutput.Trim()} {result.StandardError.Trim()}");
        }
    }

    private async Task<string> OutputAsync(string workingDirectory, params string[] arguments)
    {
        var result = await _git.ExecuteAsync(
            workingDirectory, arguments, CancellationToken.None);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} in '{workingDirectory}' " +
                $"failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        return result.StandardOutput.Trim();
    }

    [Fact]
    public async Task InspectAsync_MissingDirectory_ReportsNotExists()
    {
        // Arrange
        var inspector = CreateInspector();
        var missing = Path.Combine(
            Path.GetTempPath(), "RepoDashboard.Tests", Guid.NewGuid().ToString("N"));

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(missing), CancellationToken.None);

        // Assert: no Git process is needed for a folder that is not there.
        snapshot.DirectoryExists.Should().BeFalse();
        snapshot.IsGitRepository.Should().BeFalse();
    }

    [Fact]
    public async Task InspectAsync_OrdinaryFolder_ReportsNotAGitRepository()
    {
        // Arrange
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();
        var ordinary = Path.Combine(factory.RootPath, "not-a-repo");
        Directory.CreateDirectory(ordinary);

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(ordinary), CancellationToken.None);

        // Assert
        snapshot.DirectoryExists.Should().BeTrue();
        snapshot.IsGitRepository.Should().BeFalse();
    }

    [Fact]
    public async Task InspectAsync_CleanCheckout_PopulatesFullSnapshot()
    {
        // Arrange: remote/main = A, clone at A.
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");

        var before = DateTimeOffset.UtcNow;

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        // Assert
        snapshot.DirectoryExists.Should().BeTrue();
        snapshot.IsGitRepository.Should().BeTrue();
        snapshot.CurrentBranch.Should().Be("main");
        snapshot.IsDetachedHead.Should().BeFalse();
        snapshot.DetachedHeadSha.Should().BeNull();
        snapshot.IsDirty.Should().BeFalse();
        snapshot.UpstreamRef.Should().Be("origin/main");
        snapshot.UpstreamRemote.Should().Be("origin");
        snapshot.UpstreamBranch.Should().Be("main");
        snapshot.DefaultRemoteBranch.Should().Be("main");
        snapshot.UpstreamDivergence.Should().Be(new Divergence(0, 0));
        snapshot.DefaultBranchDivergence.Should().Be(new Divergence(0, 0));
        snapshot.MergeInProgress.Should().BeFalse();
        snapshot.RebaseInProgress.Should().BeFalse();
        snapshot.CherryPickInProgress.Should().BeFalse();
        snapshot.InspectedAt.Should().BeOnOrAfter(before)
            .And.BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task InspectAsync_DetachedHead_ReportsDetachedWithShortSha()
    {
        // Arrange
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");

        var expectedSha = await OutputAsync(repo, "rev-parse", "--short", "HEAD");
        await RunAsync(repo, "checkout", "--detach", "HEAD");

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        // Assert
        snapshot.IsDetachedHead.Should().BeTrue();
        snapshot.CurrentBranch.Should().BeNull();
        snapshot.DetachedHeadSha.Should().Be(expectedSha);
    }

    [Fact]
    public async Task InspectAsync_ModifiedFile_IsDirty()
    {
        // Arrange
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "a.txt", "A", "Commit A");
        await File.AppendAllTextAsync(Path.Combine(repo, "a.txt"), "more");

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        // Assert
        snapshot.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task InspectAsync_UntrackedFile_IsDirty()
    {
        // Arrange
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "a.txt", "A", "Commit A");
        await File.WriteAllTextAsync(Path.Combine(repo, "new.txt"), "untracked");

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        // Assert
        snapshot.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task InspectAsync_StagedNewFile_IsDirty()
    {
        // Arrange
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "a.txt", "A", "Commit A");
        await File.WriteAllTextAsync(Path.Combine(repo, "staged.txt"), "staged");
        await RunAsync(repo, "add", "--", "staged.txt");

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        // Assert
        snapshot.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task InspectAsync_DeletedFile_IsDirty()
    {
        // Arrange
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "a.txt", "A", "Commit A");
        File.Delete(Path.Combine(repo, "a.txt"));

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        // Assert
        snapshot.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task InspectAsync_FeatureBranchWithSlashes_ParsesRemoteAndBranch()
    {
        // Arrange: branch names may contain slashes — split on the FIRST one.
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CreateBranchAsync(repo, "feature/search");
        await factory.CheckoutAsync(repo, "feature/search");
        await factory.CommitFileAsync(repo, "b.txt", "B", "Commit B");
        await factory.PushAsync(repo, "feature/search");
        await factory.CommitFileAsync(repo, "c.txt", "C", "Commit C");

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        // Assert
        snapshot.CurrentBranch.Should().Be("feature/search");
        snapshot.UpstreamRef.Should().Be("origin/feature/search");
        snapshot.UpstreamRemote.Should().Be("origin");
        snapshot.UpstreamBranch.Should().Be("feature/search");
        snapshot.UpstreamDivergence.Should().Be(new Divergence(1, 0));
        snapshot.DefaultBranchDivergence.Should().Be(new Divergence(2, 0));
    }

    [Fact]
    public async Task InspectAsync_BranchWithoutUpstream_ReturnsNullsNotError()
    {
        // Arrange
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CreateBranchAsync(repo, "local-only");
        await factory.CheckoutAsync(repo, "local-only");

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        // Assert: valid state, not an application error.
        snapshot.CurrentBranch.Should().Be("local-only");
        snapshot.UpstreamRef.Should().BeNull();
        snapshot.UpstreamRemote.Should().BeNull();
        snapshot.UpstreamBranch.Should().BeNull();
        snapshot.UpstreamDivergence.Should().BeNull();
    }

    [Fact]
    public async Task InspectAsync_DefaultBranchOverride_IsRespected()
    {
        // Arrange: origin/HEAD points at main, but the override wins.
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "a.txt", "A", "Commit A");
        await factory.PushAsync(repo);
        await factory.CreateBranchAsync(repo, "develop");
        await factory.CheckoutAsync(repo, "develop");
        await factory.CommitFileAsync(repo, "b.txt", "B", "Commit B");
        await factory.PushAsync(repo, "develop");

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo, defaultBranchOverride: "develop"),
            CancellationToken.None);

        // Assert
        snapshot.DefaultRemoteBranch.Should().Be("develop");
        snapshot.DefaultBranchDivergence.Should().Be(new Divergence(0, 0));
    }

    [Fact]
    public async Task InspectAsync_RemoteHeadMissing_FallsBackToMain()
    {
        // Arrange: origin/HEAD deleted locally, origin/main still exists.
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");
        await RunAsync(repo, "remote", "set-head", "origin", "--delete");

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        // Assert
        snapshot.DefaultRemoteBranch.Should().Be("main");
    }

    [Fact]
    public async Task InspectAsync_OnlyMasterExists_FallsBackToMaster()
    {
        // Arrange: remote default is master and origin/HEAD is unknown.
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = Path.Combine(factory.RootPath, "remote.git");
        await RunAsync(factory.RootPath, "init", "--bare", "-b", "master", remote);

        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed, "master");

        var repo = await factory.CloneAsync(remote, "repo");
        await RunAsync(repo, "remote", "set-head", "origin", "--delete");

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        // Assert
        snapshot.CurrentBranch.Should().Be("master");
        snapshot.DefaultRemoteBranch.Should().Be("master");
    }

    [Fact]
    public async Task InspectAsync_NoKnownDefaultBranch_ReturnsNull()
    {
        // Arrange: remote default is develop; main/master do not exist.
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = Path.Combine(factory.RootPath, "remote.git");
        await RunAsync(factory.RootPath, "init", "--bare", "-b", "develop", remote);

        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed, "develop");

        var repo = await factory.CloneAsync(remote, "repo");
        await RunAsync(repo, "remote", "set-head", "origin", "--delete");

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        // Assert: unknown yields null, never a guess.
        snapshot.DefaultRemoteBranch.Should().BeNull();
        snapshot.DefaultBranchDivergence.Should().BeNull();
    }

    [Fact]
    public async Task InspectAsync_MergeInProgress_IsDetected()
    {
        // Arrange: merge staged but not committed.
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "a.txt", "A", "Commit A");
        await factory.CreateBranchAsync(repo, "side");
        await factory.CheckoutAsync(repo, "side");
        await factory.CommitFileAsync(repo, "side.txt", "S", "Side commit");
        await factory.CheckoutAsync(repo, "main");
        await RunAsync(repo, "merge", "--no-ff", "--no-commit", "side");

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        // Assert
        snapshot.MergeInProgress.Should().BeTrue();
        snapshot.RebaseInProgress.Should().BeFalse();
        snapshot.CherryPickInProgress.Should().BeFalse();
    }

    [Fact]
    public async Task InspectAsync_CherryPickInProgress_IsDetected()
    {
        // Arrange: a conflicting cherry-pick left stopped.
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "file.txt", "base", "Base commit");
        await factory.CreateBranchAsync(repo, "side");
        await factory.CheckoutAsync(repo, "side");
        var picked = await factory.CommitFileAsync(repo, "file.txt", "side", "Side change");
        await factory.CheckoutAsync(repo, "main");
        await factory.CommitFileAsync(repo, "file.txt", "main", "Main change");

        // Note: no --no-commit here — a conflicting pick stops on its own,
        // and --no-commit would suppress the CHERRY_PICK_HEAD marker file.
        var cherryPick = await _git.ExecuteAsync(
            repo, ["cherry-pick", picked], CancellationToken.None);

        // Sanity: the cherry-pick really did stop on a conflict.
        cherryPick.Success.Should().BeFalse();

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        // Assert
        snapshot.CherryPickInProgress.Should().BeTrue();
        snapshot.MergeInProgress.Should().BeFalse();
        snapshot.RebaseInProgress.Should().BeFalse();
    }

    [Fact]
    public async Task InspectAsync_RebaseInProgress_IsDetected()
    {
        // Arrange: conflicting rebase left stopped.
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "file.txt", "base", "Base commit");
        await factory.CreateBranchAsync(repo, "side");
        await factory.CheckoutAsync(repo, "side");
        await factory.CommitFileAsync(repo, "file.txt", "side", "Side change");
        await factory.CheckoutAsync(repo, "main");
        await factory.CommitFileAsync(repo, "file.txt", "main", "Main change");
        await factory.CheckoutAsync(repo, "side");

        var rebase = await _git.ExecuteAsync(
            repo, ["rebase", "main"], CancellationToken.None);

        // Sanity: the rebase really did stop on a conflict.
        rebase.Success.Should().BeFalse();

        // Act
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        // Assert
        snapshot.RebaseInProgress.Should().BeTrue();
        snapshot.MergeInProgress.Should().BeFalse();
        snapshot.CherryPickInProgress.Should().BeFalse();
    }

    [Fact]
    public async Task InspectAsync_DoesNotMutateRepository()
    {
        // Arrange: record all refs plus FETCH_HEAD before inspection.
        using var factory = new GitTestRepositoryFactory();
        var inspector = CreateInspector();

        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.FetchAsync(repo);

        // Force the worktree stat information to differ from the index's
        // cached stat data without changing any contents: this gives a plain
        // git status a reason to refresh and rewrite .git/index, so the
        // index comparison below genuinely guards inspector read-onlyness.
        var trackedFile = Path.Combine(repo, "a.txt");
        File.SetLastWriteTimeUtc(trackedFile, DateTime.UtcNow.AddMinutes(1));

        var refsBefore = await OutputAsync(repo, "for-each-ref");
        var fetchHeadPath = Path.Combine(repo, ".git", "FETCH_HEAD");
        var fetchHeadBefore = await File.ReadAllTextAsync(fetchHeadPath);
        var indexPath = Path.Combine(repo, ".git", "index");
        var indexBefore = await File.ReadAllBytesAsync(indexPath);

        // Act
        var snapshot = await inspector.InspectAsync(ConfigFor(repo), CancellationToken.None);

        // Assert: the inspector only reads — refs, fetched state and the
        // index are intact. The tree still reports clean: only the timestamp
        // changed, so there is nothing to update.
        snapshot.IsDirty.Should().BeFalse();
        (await OutputAsync(repo, "for-each-ref")).Should().Be(refsBefore);
        (await File.ReadAllTextAsync(fetchHeadPath)).Should().Be(fetchHeadBefore);
        (await File.ReadAllBytesAsync(indexPath)).Should().Equal(indexBefore);
    }
}
