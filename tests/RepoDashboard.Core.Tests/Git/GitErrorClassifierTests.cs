using FluentAssertions;
using RepoDashboard.Core.Git;

namespace RepoDashboard.Core.Tests.Git;

public sealed class GitErrorClassifierTests
{
    [Theory]
    [InlineData("fatal: Authentication failed for 'https://example.invalid/'")]
    [InlineData("fatal: could not read Username for 'https://example.invalid': terminal prompts disabled")]
    [InlineData("git@github.com: Permission denied (publickey).")]
    [InlineData("Logon failed, use ctrl+c to cancel basic credential prompt.")]
    public void Authentication_patterns_map_to_auth_hint(string raw)
    {
        GitErrorClassifier.Classify(raw).Should().Be(GitFailureKind.Authentication);
        GitErrorClassifier.GetFriendlyHint(raw).Should().Be(
            "Authentication failed. Check Git Credential Manager or SSH credentials.");
    }

    [Theory]
    [InlineData("fatal: Could not resolve host github.example.invalid")]
    [InlineData("fatal: unable to access 'https://example.invalid/': Could not resolve host")]
    [InlineData("fatal: unable to access 'https://example.invalid/': Connection timed out")]
    public void Network_patterns_map_to_unreachable_hint(string raw)
    {
        GitErrorClassifier.Classify(raw).Should().Be(GitFailureKind.NetworkUnreachable);
        GitErrorClassifier.GetFriendlyHint(raw).Should().Be(
            "Remote could not be reached.");
    }

    [Theory]
    [InlineData("ERROR: Repository not found.")]
    [InlineData("fatal: repository 'https://example.invalid/missing.git' not found")]
    [InlineData("remote: Repository not found. fatal: repository 'https://example.invalid/x.git/' not found")]
    public void Removed_patterns_map_to_not_found_hint(string raw)
    {
        GitErrorClassifier.Classify(raw).Should().Be(GitFailureKind.RemoteNotFound);
        GitErrorClassifier.GetFriendlyHint(raw).Should().Be(
            "Remote repository could not be found or access was denied.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fatal: Not possible to fast-forward to 'abc123'.")]
    [InlineData("error: some other git failure")]
    public void Unknown_or_empty_output_has_no_hint(string? raw)
    {
        GitErrorClassifier.Classify(raw).Should().BeNull();
        GitErrorClassifier.GetFriendlyHint(raw).Should().BeNull();
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        GitErrorClassifier.Classify("FATAL: AUTHENTICATION FAILED").Should()
            .Be(GitFailureKind.Authentication);
        GitErrorClassifier.Classify("Fatal: Could Not Resolve Host x").Should()
            .Be(GitFailureKind.NetworkUnreachable);
        GitErrorClassifier.Classify("ERROR: REPOSITORY NOT FOUND.").Should()
            .Be(GitFailureKind.RemoteNotFound);
    }

    [Fact]
    public void Authentication_wins_over_network_when_both_match()
    {
        // Permission-denied over an unreachable host is still an auth problem
        // to the user — deterministic precedence keeps hints stable.
        const string raw =
            "fatal: unable to access 'https://example.invalid/': " +
            "Permission denied (publickey)";

        GitErrorClassifier.Classify(raw).Should().Be(GitFailureKind.Authentication);
    }
}
