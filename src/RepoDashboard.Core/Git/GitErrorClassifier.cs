namespace RepoDashboard.Core.Git;

/// <summary>
/// Maps raw Git stderr/stdout to friendly hints (Task 42).
/// Pure function, no IO. Matching is case-insensitive substring search
/// over the combined output — Git messages vary by version and transport,
/// so patterns stay deliberately broad. Raw output is always preserved
/// by callers; this only adds a hint alongside it.
/// </summary>
public static class GitErrorClassifier
{
    public static string? GetFriendlyHint(string? rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return null;
        }

        var kind = Classify(rawOutput);

        return kind is null ? null : GetHint(kind.Value);
    }

    public static GitFailureKind? Classify(string? rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return null;
        }

        // Lower once: all patterns are lowercase.
        var text = rawOutput.ToLowerInvariant();

        if (IsAuthenticationFailure(text))
        {
            return GitFailureKind.Authentication;
        }

        if (IsRemoteNotFound(text))
        {
            return GitFailureKind.RemoteNotFound;
        }

        if (IsNetworkFailure(text))
        {
            return GitFailureKind.NetworkUnreachable;
        }

        return null;
    }

    public static string GetHint(GitFailureKind kind) =>
        kind switch
        {
            GitFailureKind.Authentication =>
                "Authentication failed. Check Git Credential Manager or SSH credentials.",
            GitFailureKind.NetworkUnreachable =>
                "Remote could not be reached.",
            GitFailureKind.RemoteNotFound =>
                "Remote repository could not be found or access was denied.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private static bool IsAuthenticationFailure(string text) =>
        text.Contains("authentication failed", StringComparison.Ordinal)
        || text.Contains("could not read username", StringComparison.Ordinal)
        || text.Contains("could not read password", StringComparison.Ordinal)
        || text.Contains("permission denied", StringComparison.Ordinal)
        || text.Contains("logon failed", StringComparison.Ordinal)
        || text.Contains("invalid credentials", StringComparison.Ordinal)
        || text.Contains("401 unauthorized", StringComparison.Ordinal)
        || text.Contains("403 forbidden", StringComparison.Ordinal)
        // libcurl/Git HTTP errors: "The requested URL returned error: 403".
        // Must precede the broad "unable to access" network pattern.
        || text.Contains("returned error: 401", StringComparison.Ordinal)
        || text.Contains("returned error: 403", StringComparison.Ordinal)
        || text.Contains("access denied", StringComparison.Ordinal)
        // SSH: no access rights on the remote.
        || text.Contains("could not read from remote repository", StringComparison.Ordinal)
        || text.Contains("please make sure you have the correct access rights", StringComparison.Ordinal);

    private static bool IsRemoteNotFound(string text) =>
        text.Contains("repository not found", StringComparison.Ordinal)
        // GitHub/GitLab style with a URL in between:
        // "fatal: repository 'https://.../missing.git' not found".
        || (text.Contains("repository", StringComparison.Ordinal)
            && text.Contains("not found", StringComparison.Ordinal))
        || text.Contains("could not find repository", StringComparison.Ordinal)
        || text.Contains("no such repository", StringComparison.Ordinal)
        || (text.Contains("does not exist", StringComparison.Ordinal)
            && text.Contains("remote", StringComparison.Ordinal));

    private static bool IsNetworkFailure(string text) =>
        text.Contains("could not resolve host", StringComparison.Ordinal)
        || text.Contains("unable to access", StringComparison.Ordinal)
        || text.Contains("could not connect", StringComparison.Ordinal)
        || text.Contains("failed to connect", StringComparison.Ordinal)
        || text.Contains("network is unreachable", StringComparison.Ordinal)
        || text.Contains("connection timed out", StringComparison.Ordinal)
        || text.Contains("timed out", StringComparison.Ordinal)
        || text.Contains("connection reset", StringComparison.Ordinal)
        || text.Contains("no route to host", StringComparison.Ordinal);
}
